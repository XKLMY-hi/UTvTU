using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Xunit;

namespace OpenUtau.Core.Vst {
    /// <summary>LoadAtAsync 的串行化与生命周期（FakeVstBridge 注入）。</summary>
    [Collection("VstShared")]
    public class VstTrackInstancesTest {
        private const string TestUid = "test:async-load";

        public VstTrackInstancesTest() {
            VstTestSetup.Register(VstTestSetup.MakeEntry(TestUid));
        }

        [Fact]
        public async Task ReplacingPluginDoesNotOverwriteTheIncomingStateWithTheRetiredInstance() {
            const string nextUid = "test:replacement-state-owner";
            VstTestSetup.Register(VstTestSetup.MakeEntry(nextUid));
            using var track = new VstTrackInstances(VstTestSetup.NextTrackNo()) { Bridge = new FakeVstBridge() };
            var slot = VstTestSetup.CreateSlot(TestUid);
            Assert.NotNull(await track.LoadAtAsync(0, slot));
            slot.PluginUid = nextUid;
            slot.StateData = new byte[] { 77, 88 };
            var replacement = await track.LoadAtAsync(0, slot);
            Assert.NotNull(replacement);
            Assert.Equal(nextUid, replacement.Entry.Uid);
            Assert.Equal(new byte[] { 77, 88 }, slot.StateData);
        }

        [Fact]
        public async Task LoadAtAsync_LoadsAndPublishes() {
            var bridge = new FakeVstBridge();
            var ti = new VstTrackInstances(0) { Bridge = bridge };
            var slot = VstTestSetup.CreateSlot(TestUid);

            var fx = await ti.LoadAtAsync(0, slot);
            Assert.NotNull(fx);
            Assert.True(fx.IsLoaded);
            Assert.Same(fx, ti[0]);
        }

        [Fact]
        public async Task LoadAtAsync_ConcurrentLoads_AreSerialized() {
            var bridge = new FakeVstBridge { LoadDelay = TimeSpan.FromMilliseconds(50) };
            var ti = new VstTrackInstances(0) { Bridge = bridge };
            var slot0 = VstTestSetup.CreateSlot(TestUid, 0);
            var slot1 = VstTestSetup.CreateSlot(TestUid, 1);

            // 两个 slot 并发加载——_loadGate 串行化：总耗时 ≥ 2 × LoadDelay
            var sw = Stopwatch.StartNew();
            var t0 = ti.LoadAtAsync(0, slot0);
            var t1 = ti.LoadAtAsync(1, slot1);
            await Task.WhenAll(t0, t1);
            sw.Stop();

            Assert.NotNull(t0.Result);
            Assert.NotNull(t1.Result);
            Assert.NotSame(t0.Result, t1.Result); // 两个独立实例
            Assert.True(sw.ElapsedMilliseconds >= 90, $"串行化未生效：耗时 {sw.ElapsedMilliseconds}ms（预期 ≥ 100ms）");
            Assert.Same(t0.Result, ti[0]);
            Assert.Same(t1.Result, ti[1]);
        }

        [Fact]
        public async Task LoadAtAsync_Reload_DisposesOldInstance() {
            var bridge = new FakeVstBridge();
            var ti = new VstTrackInstances(0) { Bridge = bridge };
            var slot = VstTestSetup.CreateSlot(TestUid);

            var first = await ti.LoadAtAsync(0, slot);
            Assert.NotNull(first);
            var second = await ti.LoadAtAsync(0, slot);

            Assert.NotNull(second);
            Assert.NotSame(first, second);
            // 旧实例进延迟销毁队列（不立即 Dispose——音频线程可能仍在用）
            Assert.Empty(bridge.Unloaded);
            var firstHandle = first.GetBridgeHandle(); // Dispose 后 handle 清零，先记录
            ti.FlushPendingDispose();
            Assert.Contains(firstHandle, bridge.Unloaded);
        }

        [Fact]
        public async Task LoadAtAsync_BypassedSlot_ReturnsNull() {
            var bridge = new FakeVstBridge();
            var ti = new VstTrackInstances(0) { Bridge = bridge };
            var slot = VstTestSetup.CreateSlot(TestUid);
            slot.Bypassed = true;

            var fx = await ti.LoadAtAsync(0, slot);
            Assert.Null(fx);
        }
    }
}

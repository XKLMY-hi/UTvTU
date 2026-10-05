using System;
using System.Diagnostics;
using System.Threading;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Core.Vst {
    /// <summary>
    /// TrackMixCommands 的 VST 槽位命令 do/undo 往返——数据层（slot）与实例层
    /// （VstPluginManager 异步加载）双重断言。FakeVstBridge 注入 + 轮询等待异步 Load。
    ///
    /// **并发纪律（W23 #3 收口）**：<c>VstPluginManager</c> 是进程级单例，<c>Bridge</c> 与
    /// 「按 (trackNo, slot) 索引的实例表」都是全局的，而 collection 只保证同集合串行 ⇒
    /// 本类所有用例体走 <see cref="VstTestSetup.RunExclusive"/> 闸；轨道用
    /// <see cref="VstTestSetup.NextTrackNo"/> **专属键**（裸 <c>new UTrack()</c> 的 TrackNo 默认
    /// 是 0，会和并行用例撞键）；清理只用 <c>RemoveTrack(自己的 trackNo)</c>，
    /// **不再调 <c>ClearAll()</c>**——那是全表清空，会把并行用例正在用的实例一起卸掉
    /// （原实现 3 处 ClearAll 正是本用例偶发红的机制之一）；<c>Bridge</c> 也在 finally 还原。
    /// </summary>
    [Collection("VstShared")]
    public class TrackMixCommandsTest {
        const string UidA = "test:cmd-a";
        const string UidB = "test:cmd-b";

        public TrackMixCommandsTest() {
            VstTestSetup.Register(VstTestSetup.MakeEntry(UidA, "CmdA"));
            VstTestSetup.Register(VstTestSetup.MakeEntry(UidB, "CmdB"));
        }

        static UTrack MakeTrack() {
            var track = new UTrack {
                TrackNo = VstTestSetup.NextTrackNo(),   // 专属键：与其它用例/集合彻底隔离
            };
            track.VstSlots = VstPluginManager.CreateDefaultSlots(3);
            return track;
        }

        /// <summary>轮询等待条件成立（异步 Load 完成后断言）。</summary>
        static void WaitFor(Func<bool> cond, int timeoutMs = 3000) {
            var sw = Stopwatch.StartNew();
            while (!cond()) {
                Assert.True(sw.ElapsedMilliseconds < timeoutMs,
                    $"WaitFor timed out after {sw.ElapsedMilliseconds}ms");
                Thread.Sleep(10);
            }
        }

        /// <summary>把假的 bridge 装上、跑用例体、然后**定点**清理自己的轨道并还原 bridge。</summary>
        static void WithTrack(UTrack track, Action body) {
            var bridge = new FakeVstBridge();
            var saved = VstPluginManager.Inst.Bridge;
            VstTestSetup.RunExclusive(() => {
                try {
                    VstPluginManager.Inst.Bridge = bridge;
                    body();
                } finally {
                    // 只清自己的键；不碰全局 ClearAll（会卸掉并行用例的实例）
                    VstPluginManager.Inst.RemoveTrack(track.TrackNo);
                    VstPluginManager.Inst.Bridge = saved;
                }
            });
        }

        [Fact]
        public void AddVstSlot_DoUndo_RoundTrip() {
            var track = MakeTrack();
            WithTrack(track, () => {
                var cmd = TrackMixCommands.AddVstSlot(track, 3, UidA);

                cmd.Execute();
                Assert.Equal(UidA, track.VstSlots[3].PluginUid);
                WaitFor(() => VstPluginManager.Inst.GetEffect(track.TrackNo, 3) != null);

                cmd.Unexecute();
                Assert.Equal("", track.VstSlots[3].PluginUid);
                WaitFor(() => VstPluginManager.Inst.GetEffect(track.TrackNo, 3) == null);
            });
        }

        [Fact]
        public void RemoveVstSlot_Undo_RestoresUidAndInstance() {
            var track = MakeTrack();
            WithTrack(track, () => {
                // 先加载一个插件到槽 0
                var loadCmd = TrackMixCommands.AddVstSlot(track, 0, UidA);
                loadCmd.Execute();
                WaitFor(() => VstPluginManager.Inst.GetEffect(track.TrackNo, 0) != null);

                var cmd = TrackMixCommands.RemoveVstSlot(track, 0);
                cmd.Execute();
                Assert.Equal("", track.VstSlots[0].PluginUid);
                WaitFor(() => VstPluginManager.Inst.GetEffect(track.TrackNo, 0) == null);

                // undo：恢复 UID + 重载实例
                cmd.Unexecute();
                Assert.Equal(UidA, track.VstSlots[0].PluginUid);
                WaitFor(() => VstPluginManager.Inst.GetEffect(track.TrackNo, 0) != null);
            });
        }

        [Fact]
        public void SetVstPlugin_DoUndo_RestoresOldUid() {
            var track = MakeTrack();
            WithTrack(track, () => {
                // 槽 0 初始为 UidA
                TrackMixCommands.AddVstSlot(track, 0, UidA).Execute();
                WaitFor(() => VstPluginManager.Inst.GetEffect(track.TrackNo, 0) != null);

                var cmd = TrackMixCommands.SetVstPlugin(track, 0, UidB);
                cmd.Execute();
                Assert.Equal(UidB, track.VstSlots[0].PluginUid);
                // 旧实例进延迟销毁；新实例加载
                WaitFor(() => VstPluginManager.Inst.GetEffect(track.TrackNo, 0)?.Slot?.PluginUid == UidB);

                cmd.Unexecute();
                Assert.Equal(UidA, track.VstSlots[0].PluginUid);
                WaitFor(() => VstPluginManager.Inst.GetEffect(track.TrackNo, 0)?.Slot?.PluginUid == UidA);
            });
        }

        [Fact]
        public void ToggleVstBypass_DoUndo_RoundTrip() {
            var track = MakeTrack();
            var slot = track.VstSlots[0];
            Assert.False(slot.Bypassed);

            var cmd = TrackMixCommands.ToggleVstBypass(track, 0);
            cmd.Execute();
            Assert.True(slot.Bypassed);

            cmd.Unexecute();
            Assert.False(slot.Bypassed);
        }
    }
}

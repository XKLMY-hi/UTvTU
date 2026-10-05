using System;
using OpenUtau.Core.Vst;
using Xunit;

namespace OpenUtau.Test.Core.Vst {
    /// <summary>
    /// W23 #3 的**定性证据**：证明"测试里调 <c>VstPluginManager.ClearAll()</c>"是全局破坏性操作，
    /// 也是 <c>TrackMixCommandsTest</c> 偶发红的机制之一。
    ///
    /// <c>VstPluginManager</c> 是进程级单例，实例表按 <c>(trackNo, slot)</c> 索引：
    /// - 裸 <c>new UTrack()</c> 的 <c>TrackNo</c> 默认 0 ⇒ 不同用例（含并行 collection）共用键；
    /// - <c>ClearAll()</c> 清空**所有**轨道的实例 ⇒ 别的用例正在用的实例被卸掉；
    /// - <c>Bridge</c> 同样是全局的 ⇒ 用例跑完不还原，后续用例的加载会走假的桥。
    ///
    /// 因此正确姿势是：专属 trackNo（<see cref="VstTestSetup.NextTrackNo"/>）+
    /// 定点清理（<c>RemoveTrack(自己的 trackNo)</c> / <c>UnloadEffect</c>）+ 还原 Bridge +
    /// 用例体走 <see cref="VstTestSetup.RunExclusive"/> 闸。
    /// </summary>
    [Collection("VstShared")]
    public class VstGlobalStateHazardTest {
        const string UidMine = "test:hazard-mine";
        const string UidOther = "test:hazard-other";

        [Fact]
        public void ClearAll_WipesInstancesOfOtherTrackKeys_RemoveTrackOnlyTouchesItsOwn() {
            VstTestSetup.RunExclusive(() => {
                VstTestSetup.Register(VstTestSetup.MakeEntry(UidMine, "Mine"));
                VstTestSetup.Register(VstTestSetup.MakeEntry(UidOther, "Other"));
                var manager = VstPluginManager.Inst;
                var savedBridge = manager.Bridge;
                int myTrack = VstTestSetup.NextTrackNo();
                int otherTrack = VstTestSetup.NextTrackNo();
                try {
                    manager.Bridge = new FakeVstBridge();
                    var mine = manager.LoadEffect(myTrack, VstTestSetup.CreateSlot(UidMine));
                    var other = manager.LoadEffect(otherTrack, VstTestSetup.CreateSlot(UidOther));
                    Assert.NotNull(mine);
                    Assert.NotNull(other);

                    // ① 定点清理：只动自己的键，别人的实例必须毫发无损
                    manager.RemoveTrack(otherTrack);
                    Assert.Null(manager.GetEffect(otherTrack, 0));
                    Assert.NotNull(manager.GetEffect(myTrack, 0));   // ← 关键：不受影响

                    // ② 全局清空：连自己带别人一起卸（= 并行用例互相顶掉的机制）
                    manager.ClearAll();
                    Assert.Null(manager.GetEffect(myTrack, 0));
                    Assert.Null(manager.GetEffect(otherTrack, 0));
                } finally {
                    manager.RemoveTrack(myTrack);
                    manager.RemoveTrack(otherTrack);
                    manager.Bridge = savedBridge;
                }
            });
        }

        [Fact]
        public void UniqueTrackNo_IsolatesConcurrentKeys() {
            // 裸 new UTrack() 默认 TrackNo=0 ⇒ 所有此类用例共用键（这就是 #3 的根因之一）；
            // NextTrackNo() 给出互不相同的键，供裸 UTrack 的 VST 用例隔离使用。
            var bare = new OpenUtau.Core.Ustx.UTrack();
            Assert.Equal(0, bare.TrackNo);

            int a = VstTestSetup.NextTrackNo();
            int b = VstTestSetup.NextTrackNo();
            Assert.NotEqual(a, b);
            Assert.True(a > 0 && b > 0);
        }
    }
}

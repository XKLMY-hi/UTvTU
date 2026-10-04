using System;
using System.Collections.Generic;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Vst;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.Audio {
    /// <summary>
    /// W7-3 用例组 5：内置三件套 ↔ VST 链序。
    ///
    /// 用**假 VST 桥**（本仓已有的注入点 <c>VstPluginManager.Bridge</c>）走产品路径：
    /// 轨道槽位 → VstPluginManager 实例 → RenderEngine.BuildTrackOutputs → EffectChain(VST)。
    /// 两个注意点（实测踩到）：
    /// ① 链序是 <c>Fader → MixFxSource(内置) → EffectChain(VST)</c>，声像等功率律的
    ///    ×0.7071 发生在**效果之前**，算期望值必须计入；
    /// ② 管理器的活动实例是按轨道常驻的（与工程里声明几个槽位无关），所以"干声对照"
    ///    必须在安装假 VST **之前**渲染；旁通判据用的是加载时那个 slot 实例，
    ///    所以工程必须引用句柄给出的同一个 <c>VstPluginSlot</c> 对象。
    /// </summary>
    [Collection("AudioFixture")]
    public class ChainOrderMeasurementTests {
        readonly ITestOutputHelper output;

        public ChainOrderMeasurementTests(ITestOutputHelper output) {
            this.output = output;
        }

        const string Uid = "test:w7-chain-order";
        const double ProbeFreq = 8000;   // EQ 高架拐点：+12 dB 高架 → 该频率 ×2
        const double ProbeAmp = 0.2;
        const double PanLaw = 0.70710678;
        const double DryPeak = ProbeAmp * PanLaw;          // 0.1414
        const double EqPeak = ProbeAmp * PanLaw * 2;       // 0.2828（+12 dB 高架在拐点 = +6 dB）

        static int Samples(double seconds) => (int)Math.Round(seconds * AudioFixtures.Rate) * AudioFixtures.Channels;

        static UMixFx BrightEq() => new() {
            Enabled = true,
            EqEnabled = true,
            EqLowDb = 0, EqMidFreq = 3000, EqMidDb = 0, EqHighDb = 12,
            CompEnabled = false,
            ReverbEnabled = false,
            ReverbPreset = "off", ReverbWet = 0,
        };

        static AudioFixtures.TrackSpec Spec(UMixFx? fx, VstPluginSlot? slot) => new() {
            Samples = AudioFixtures.Tone(ProbeFreq, 0.4, ProbeAmp),
            AttachMixFx = fx != null,
            MixFx = fx,
            VstSlots = slot == null ? null : new List<VstPluginSlot> { slot },
        };

        [Fact]
        public void BuiltInEq_AppliesBeforeVst() {
            // 1) 对照：只有内置 EQ（此时还没装 VST 实例）
            var eqOnly = AudioFixtures.RenderMixdown(
                AudioFixtures.BuildWaveProject(Spec(BrightEq(), slot: null)), Samples(0.4));
            double pEq = AudioMeasure.Peak(eqOnly);

            // 2) 装一个硬限幅 0.2 的假 VST，再把**同一个 slot 对象**放进工程
            using var handle = AudioFixtures.InstallFakeVst(0, Uid, new AudioFixtures.ClipVstBridge(0.2f));
            var eqThenVst = AudioFixtures.RenderMixdown(
                AudioFixtures.BuildWaveProject(Spec(BrightEq(), handle.Slot)), Samples(0.4));
            double pBoth = AudioMeasure.Peak(eqThenVst);

            output.WriteLine($"内置 EQ 峰值={pEq:F6}（期望 {EqPeak:F6}）");
            output.WriteLine($"EQ→限幅(0.2) 峰值={pBoth:F6}；若 VST 排在内置之前应为 {EqPeak:F6}（干声 {DryPeak:F6} 未达限幅阈值）");
            Assert.InRange(pEq, EqPeak * 0.99, EqPeak * 1.01);
            Assert.InRange(pBoth, 0.198, 0.202);                // 限幅生效 = VST 在内置之后
            Assert.True(pBoth < pEq - 0.05, "限幅未生效：VST 可能排在内置 EQ 之前");
        }

        [Fact]
        public void VstValues_AreHeard_AndScaleLinearly() {
            var dry = AudioFixtures.RenderMixdown(
                AudioFixtures.BuildWaveProject(Spec(null, slot: null)), Samples(0.4));
            using var handle = AudioFixtures.InstallFakeVst(0, Uid, new AudioFixtures.GainVstBridge(2f));
            var vst = AudioFixtures.RenderMixdown(
                AudioFixtures.BuildWaveProject(Spec(null, handle.Slot)), Samples(0.4));

            double dryPeak = AudioMeasure.Peak(dry);
            double vstPeak = AudioMeasure.Peak(vst);
            double ratioDb = AudioMeasure.ToDb(vstPeak / dryPeak);
            output.WriteLine($"干轨峰值={dryPeak:F6}，VST(×2) 峰值={vstPeak:F6}，Δ={ratioDb:F3} dB（期望 +6.02）");
            Assert.InRange(dryPeak, DryPeak * 0.99, DryPeak * 1.01);
            Assert.InRange(vstPeak, DryPeak * 2 * 0.99, DryPeak * 2 * 1.01);
            Assert.InRange(ratioDb, 5.9, 6.1);
        }

        [Fact]
        public void BypassedVstSlot_IsSilentPath() {
            using var handle = AudioFixtures.InstallFakeVst(0, Uid, new AudioFixtures.GainVstBridge(4f));
            handle.Slot.Bypassed = true;
            var rendered = AudioFixtures.RenderMixdown(
                AudioFixtures.BuildWaveProject(Spec(null, handle.Slot)), Samples(0.4));
            double peak = AudioMeasure.Peak(rendered);
            output.WriteLine($"槽位 Bypassed=true：峰值={peak:F6}（干声 {DryPeak:F6}；旁通生效则不应出现 ×4）");
            Assert.InRange(peak, DryPeak * 0.99, DryPeak * 1.01);
        }

        [Fact]
        public void IdentityVst_IsBitIdenticalToDryChain() {
            var dry = AudioFixtures.RenderMixdown(
                AudioFixtures.BuildWaveProject(Spec(null, slot: null)), Samples(0.4));
            using var handle = AudioFixtures.InstallFakeVst(0, Uid, new AudioFixtures.GainVstBridge(1f));
            var vst = AudioFixtures.RenderMixdown(
                AudioFixtures.BuildWaveProject(Spec(null, handle.Slot)), Samples(0.4));
            int diff = AudioMeasure.FirstDifference(vst, dry);
            output.WriteLine($"恒等 VST 链 vs 干轨：首个逐样本差索引={diff}（-1 = 完全一致）");
            Assert.Equal(-1, diff);
        }
    }
}

using System;
using System.Linq;
using OpenUtau.Core.Render;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.SignalChain.Effects;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Vst;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.Audio {
    /// <summary>
    /// W7b：VST 音频线程每块分配的回归与对照测量。
    ///
    /// 被测对象是**音频线程入口本身**：<c>EffectChain.Mix(position, buffer, 0, count)</c>
    /// （`RenderEngine` 建链后由 MasterAdapter/ExportAdapter 逐块调用），
    /// 用 <see cref="GC.GetAllocatedBytesForCurrentThread"/> 精确测本线程分配——
    /// 不靠第三方库，也不受其它线程影响。
    ///
    /// 背景（W7 发现）：<c>VstEffect.Process</c> 在 `buffer.Length != count` 时会
    /// `new float[count]` 复制一份连续缓冲；而 `EffectChain.scratch` 是预分配的 4096
    /// （块长 512）⇒ 条件恒真 ⇒ 每块一次分配。
    /// </summary>
    [Collection("AudioFixture")]
    public class VstProcessAllocationTests {
        readonly ITestOutputHelper output;

        public VstProcessAllocationTests(ITestOutputHelper output) {
            this.output = output;
        }

        const int TrackNo = 0;
        const string Uid = "test:w7b-alloc";
        const int Block = 512;
        const int Blocks = 512;
        const int WarmupBlocks = 8;

        /// <summary>单块 count=512 样本的数组分配量（x64 数组头 24 B + 512×4 B）。</summary>
        const int ExpectedSliceBytes = 24 + Block * 4;

        static int Samples(double seconds) => (int)Math.Round(seconds * AudioFixtures.Rate) * AudioFixtures.Channels;

        /// <summary>按产品形状建链：Fader → MixFxSource(干轨) → EffectChain(该 VstEffect)。</summary>
        static ISignalSource BuildChain(IEffect[] vstEffects, out UTrack track) {
            track = new UTrack { TrackNo = TrackNo };   // MixFx == null → MixFxSource 快速直通
            var inner = AudioFixtures.LoopingSource(AudioFixtures.Tone(1000, 0.05, 0.2));
            return RenderEngine.WrapTrackFx(inner, track, MixFxMode.Live, vstEffects);
        }

        (long totalBytes, long perBlock, int gen0) MeasureChain(ISignalSource chain, int blocks) {
            var buffer = new float[Block];
            int position = 0;
            for (int i = 0; i < WarmupBlocks; i++) {
                position = chain.Mix(position, buffer, 0, Block);
            }
            int gen0Before = GC.CollectionCount(0);
            long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < blocks; i++) {
                position = chain.Mix(position, buffer, 0, Block);
            }
            long bytes = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;
            int gen0 = GC.CollectionCount(0) - gen0Before;
            return (bytes, bytes / blocks, gen0);
        }

        [Fact]
        public void VstInProductChain_SteadyState_DoesNotAllocatePerBlock() {
            using var handle = AudioFixtures.InstallFakeVst(TrackNo, Uid, new AudioFixtures.GainVstBridge(1.5f));
            var vstEffects = VstPluginManager.Inst.GetActiveEffects(TrackNo).Cast<IEffect>().ToArray();
            Assert.Single(vstEffects);   // 确认真的挂上了 VST（假桥注入）

            var chain = BuildChain(vstEffects, out _);
            var (bytes, perBlock, gen0) = MeasureChain(chain, Blocks);
            output.WriteLine($"VST 链：{Blocks} 块（块长 {Block}）× 稳态 → 总分配 {bytes} B，" +
                             $"每块 {perBlock} B ≈ {perBlock / (double)ExpectedSliceBytes:F2} 次数组分配，" +
                             $"Gen0 回收 {gen0} 次");
            output.WriteLine($"（1444 次/秒 的等效推算：每块 {perBlock} B → " +
                             $"{perBlock * 86 * 16 / 1024.0:F0} KiB/s @16 轨 × 86 块/s）");
            Assert.True(bytes == 0,
                $"稳态每块仍有分配：{bytes} B / {Blocks} 块 = {perBlock} B/块" +
                $"（≈ {perBlock / (double)ExpectedSliceBytes:F2} 次 new float[{Block}]）");
        }

        [Fact]
        public void DryChain_SteadyState_HasNoAllocations_Baseline() {
            // 基线对照：没有 VST 时本该零分配（W7 已验证 MixFxSource 快路径）
            var chain = BuildChain(Array.Empty<IEffect>(), out _);
            var (bytes, perBlock, gen0) = MeasureChain(chain, Blocks);
            output.WriteLine($"干轨链（无 VST）：{Blocks} 块 → 总分配 {bytes} B，每块 {perBlock} B，Gen0 {gen0} 次");
            Assert.Equal(0, bytes);
        }

        /// <summary>
        /// **不动点不变量**（修复所依赖的前提，必须钉死）：容量 &gt; count 且 offset=0 时
        /// VST 只能处理 [0, count)，count 之后的样本一个字节都不能动。
        /// 依据：native `vst_process` 只按 frames 访问 buffer[i*2]、buffer[i*2+1]（i &lt; nf）。
        /// </summary>
        [Fact]
        public void InPlacePath_ProcessesExactlyCountSamples_AndLeavesTailUntouched() {
            using var handle = AudioFixtures.InstallFakeVst(TrackNo, Uid, new AudioFixtures.GainVstBridge(2f));
            var fx = VstPluginManager.Inst.GetActiveEffects(TrackNo)[0];
            const int count = 512;
            const int tail = 256;
            var buffer = new float[count + tail];
            Array.Fill(buffer, 0.1f);

            fx.Process(buffer, 0, count);   // 容量 768 > count 512 → 应走原地路径

            for (int i = 0; i < count; i++) {
                Assert.InRange(buffer[i], 0.1999f, 0.2001f);          // 处理过（×2）
            }
            for (int i = count; i < buffer.Length; i++) {
                Assert.InRange(buffer[i], 0.0999f, 0.1001f);          // 尾部未被触碰
            }
            output.WriteLine($"原地路径：前 {count} 样本 0.1→0.2 ✓，容量尾部 {tail} 样本保持 0.1 ✓");
        }

        /// <summary>offset != 0 的复制路径同样只处理目标区间（非产品链路，但保持正确）。</summary>
        [Fact]
        public void OffsetPath_ProcessesOnlyTargetRange() {
            using var handle = AudioFixtures.InstallFakeVst(TrackNo, Uid, new AudioFixtures.GainVstBridge(2f));
            var fx = VstPluginManager.Inst.GetActiveEffects(TrackNo)[0];
            const int count = 512;
            const int offset = 128;
            var buffer = new float[count + 512];
            Array.Fill(buffer, 0.1f);

            fx.Process(buffer, offset, count);

            for (int i = 0; i < offset; i++) {
                Assert.InRange(buffer[i], 0.0999f, 0.1001f);
            }
            for (int i = offset; i < offset + count; i++) {
                Assert.InRange(buffer[i], 0.1999f, 0.2001f);
            }
            for (int i = offset + count; i < buffer.Length; i++) {
                Assert.InRange(buffer[i], 0.0999f, 0.1001f);
            }
            output.WriteLine($"offset 路径：仅 [{offset}, {offset + count}) 被处理，两侧保持原值 ✓");
        }

        /// <summary>
        /// 端到端对照：同一工程开/关假 VST，**按设备块长 512 样本**逐块读取混音结果，
        /// 比对分配量（读取循环跑在测试线程上 = 产品里音频回调的位置）。
        /// </summary>
        [Fact]
        public void RenderMixdown_WithAndWithoutVst_AllocationTrend() {
            long ReadAllocations(bool withVst) {
                using var handle = withVst
                    ? AudioFixtures.InstallFakeVst(TrackNo, Uid, new AudioFixtures.GainVstBridge(1.5f))
                    : null;
                var spec = new AudioFixtures.TrackSpec {
                    Samples = AudioFixtures.Tone(1000, 0.5, 0.2),
                    AttachMixFx = false,
                    VstSlots = withVst
                        ? new System.Collections.Generic.List<VstPluginSlot> { handle!.Slot }
                        : null,
                };
                var project = AudioFixtures.BuildWaveProject(spec);
                var cts = new System.Threading.CancellationTokenSource();
                var mix = RenderEngine.RenderMixdown(
                    project, System.Threading.Tasks.TaskScheduler.Default, ref cts,
                    wait: true, applyMixFx: true).Item1;
                var adapter = new ExportAdapter(mix);
                var buffer = new float[Block];
                int total = Samples(0.5);
                // 预热（JIT + scratch 首次扩容）
                for (int i = 0; i < WarmupBlocks; i++) {
                    adapter.Read(buffer, 0, Block);
                }
                int read = 0;
                long before = GC.GetTotalAllocatedBytes(precise: true);
                while (read < total) {
                    int n = adapter.Read(buffer, 0, Block);
                    if (n <= 0) {
                        break;
                    }
                    read += n;
                }
                return GC.GetTotalAllocatedBytes(precise: true) - before;
            }

            long withVst = ReadAllocations(true);
            long withoutVst = ReadAllocations(false);
            output.WriteLine($"逐块读取 0.5 s（块长 {Block}）——无 VST {withoutVst} B，有 VST {withVst} B，" +
                             $"差 {withVst - withoutVst} B（≈ {(withVst - withoutVst) / (double)ExpectedSliceBytes:F0} 次切片分配）");
        }
    }
}

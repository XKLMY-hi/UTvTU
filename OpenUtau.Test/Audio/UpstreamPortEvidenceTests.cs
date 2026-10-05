using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using NAudio.Wave;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.Audio {
    /// <summary>
    /// W14 上游选择性合并的**数值证据**（复用 W7 装置：AudioMeasure/AudioFixtures/
    /// DummyVoicebank/ClassicRenderSetup）。
    ///
    /// 覆盖本批取的几条"能用测量说话"的改动：
    /// 1) ClassicSinger oto 原子快照 + 真释放（上游 83e02c7e/bfb01058）
    /// 2) oto preutter=0 的 NaN 包络（上游 f7c86f08/47a7af0d）—— 给出可复现触发条件
    /// 3) 缓存 per-path 锁（上游 5d17f141/5ca69219 本树落地）—— 共享锁 vs 拆分锁 A/B
    /// </summary>
    [Collection("AudioFixture")]
    public class UpstreamPortEvidenceTests {
        readonly ITestOutputHelper output;
        static readonly string TempRoot = Path.Combine(Path.GetTempPath(), "w14-evidence");

        public UpstreamPortEvidenceTests(ITestOutputHelper output) {
            this.output = output;
        }

        static int Samples(double seconds) =>
            (int)Math.Round(seconds * AudioFixtures.Rate) * AudioFixtures.Channels;

        /// <summary>生成伪声库并改写 oto 的 preutter/overlap（默认 50/20）。
        /// 每次用**独立目录**：声库目录会被 OtoWatcher 监视/占用，共用目录会让并发或
        /// 连续运行的用例互相踩文件锁。</summary>
        static string VoicebankWithOto(double preutter, double overlap) {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);   // oto.ini 走 shift_jis
            Directory.CreateDirectory(TempRoot);
            string source = Path.Combine(TempRoot, DummyVoicebank.FolderName);
            // **每次都重建**（不再"存在就复用"）：`%TEMP%` 是跨分支、跨轮次共享的，
            // 上次留下的成品可能来自**别的装置版本**（采样率/别名频率/oto 格式不同），
            // 复用会让"频谱/时长/preutter"这类断言的结果随残留物漂移 —— W26 定性的
            // "失败成员在轮次间漂移"就有这一类贡献。`Create` 本身是全量覆写、幂等。
            DummyVoicebank.Create(TempRoot);
            string dir = Path.Combine(TempRoot, $"{DummyVoicebank.FolderName}-p{preutter}-o{overlap}");
            Directory.CreateDirectory(dir);
            foreach (string name in new[] { "character.txt" }.Concat(DummyVoicebank.Aliases.Keys.Select(a => a + ".wav"))) {
                File.Copy(Path.Combine(source, name), Path.Combine(dir, name), overwrite: true);
            }
            var sb = new StringBuilder();
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var alias in DummyVoicebank.Aliases.Keys) {
                sb.AppendLine($"{alias}.wav={alias},0,0,0,{preutter.ToString(ci)},{overlap.ToString(ci)}");
            }
            File.WriteAllText(Path.Combine(dir, "oto.ini"), sb.ToString(), Encoding.GetEncoding("shift_jis"));
            return dir;
        }

        // ── 1) oto 原子快照 + 真释放 ─────────────────────────────────────

        [Fact]
        public void ClassicSinger_FreeMemory_TrulyReleasesSnapshot_AndReloads() {
            string dir = VoicebankWithOto(50, 20);
            var singer = DummyVoicebank.Load(dir);
            int before = singer.Otos.Count;
            int subbanksBefore = singer.Subbanks.Count;
            Assert.True(before > 0 && singer.Loaded);
            Assert.True(singer.TryGetOto("a", out _));

            singer.FreeMemory();   // 上游 83e02c7e：换快照 → 真释放
            int released = singer.Otos.Count;
            bool loadedAfterFree = singer.Loaded;
            bool otoGone = !singer.TryGetOto("a", out _);

            singer.EnsureLoaded(); // 快照重建
            int after = singer.Otos.Count;
            bool otoBack = singer.TryGetOto("a", out var oto);

            output.WriteLine($"Otos: {before} → FreeMemory 后 {released} → EnsureLoaded 后 {after}；" +
                             $"Subbanks {subbanksBefore}；Loaded={loadedAfterFree}；oto[a] 释放后消失={otoGone} 重建后命中={otoBack}");

            Assert.Equal(0, released);          // 旧实现（只置 loaded=false）此处仍是 5
            Assert.False(loadedAfterFree);
            Assert.True(otoGone);
            Assert.Equal(before, after);
            Assert.True(otoBack);
            Assert.Equal("a", oto.Alias);
        }

        [Fact]
        public void ClassicRender_StillWorks_AfterSnapshotReleaseAndReload() {
            if (!NativeWorldline.EnsureLoaded(out string reason)) {
                Assert.Skip($"原生 worldline 不可用：{reason}");
            }
            string dir = VoicebankWithOto(50, 20);
            var singer = DummyVoicebank.Load(dir);
            singer.FreeMemory();
            singer.EnsureLoaded();   // 渲染前必须已加载（SingerManager 的正常流程）

            var (project, part) = ClassicRenderSetup.BuildProject(singer, ("a", 60, 480), ("i", 62, 480));
            using var session = ClassicRenderSetup.StartPhonemizer(project);
            ClassicRenderSetup.PhonemizeAndWait(project, part, output.WriteLine);
            var rendered = AudioFixtures.RenderMixdown(project, Samples(1.4));

            var note2 = AudioMeasure.SliceMs(rendered, AudioFixtures.Channels, AudioFixtures.Rate, 650, 920);
            double f2 = AudioMeasure.DominantFreq(note2, AudioFixtures.Channels, AudioFixtures.Rate, 100, 2000);
            double expected = MusicMath.ToneToFreq(62);            output.WriteLine($"释放→重载后渲染：tone 62 期望 {expected:F2} Hz，实测 {f2:F2} Hz，RMS={AudioMeasure.RmsDb(note2):F2} dBFS");
            Assert.InRange(f2, expected * 0.97, expected * 1.03);   // 快照重构未影响渲染
            Assert.True(AudioMeasure.RmsDb(note2) > -40);
        }

        // ── 2) preutter=0 的 NaN 包络 ───────────────────────────────────

        /// <summary>
        /// 可复现触发条件（读代码 + 实测得到）：oto 的 preutter/overlap 都为 0，且**前一个
        /// 音符极短**使 <c>maxPreutter = prevDur + Prev.preutter - 5</c> 变成负数
        /// （UNote 下限 10 tick，故需要高 BPM：600 BPM 时 10 tick ≈ 2.1 ms &lt; 5 ms）——
        /// 此时 <c>autoPreutter(0) &gt; maxPreutter(&lt;0)</c> 成立，旧代码
        /// <c>ratio = maxPreutter / 0</c> 得到 ±∞，<c>autoOverlap *= ratio</c> 把 0 变成 NaN。
        /// </summary>
        [Fact]
        public void PreutterZeroOto_WithVeryShortPreviousNote_StaysFinite() {
            if (!NativeWorldline.EnsureLoaded(out string reason)) {
                Assert.Skip($"原生 worldline 不可用：{reason}");
            }
            string dir = VoicebankWithOto(preutter: 0, overlap: 0);
            var singer = DummyVoicebank.Load(dir);
            var (project, part) = ClassicRenderSetup.BuildProject(singer, ("a", 60, 10), ("i", 60, 10));
            project.tempos[0].bpm = 600;         // 10 tick ≈ 2.1 ms < 5 ms 触发路径
            project.ValidateFull();

            using var session = ClassicRenderSetup.StartPhonemizer(project);
            ClassicRenderSetup.PhonemizeAndWait(project, part, output.WriteLine);

            double overlap = part.phonemes.Count > 1 ? part.phonemes[1].overlap : double.NaN;
            double preutter = part.phonemes.Count > 1 ? part.phonemes[1].preutter : double.NaN;
            var rendered = AudioFixtures.RenderMixdown(project, Samples(0.3));
            int nan = rendered.Count(v => !float.IsFinite(v));
            output.WriteLine($"phonemes={part.phonemes.Count}：第 2 音素 preutter={preutter:F4} overlap={overlap:F4}；" +
                             $"渲染样本 {rendered.Length} 个，非有限值 {nan} 个，峰值={AudioMeasure.Peak(rendered):F6}");

            Assert.True(part.phonemes.Count > 1, "音素化未产出两个音素");
            Assert.True(double.IsFinite(overlap), $"overlap 非有限：{overlap}");
            Assert.True(double.IsFinite(preutter), $"preutter 非有限：{preutter}");
            Assert.Equal(0, nan);   // 旧代码：NaN 会经包络传播到音频
        }

        // ── 3) 缓存 per-path 锁 ─────────────────────────────────────────

        [Fact]
        public void CacheLock_IsSharedPerPath_AndSerializesWriterReader() {
            string path = Path.Combine(Path.GetTempPath(), $"w14-cache-lock-{Guid.NewGuid():N}.wav");
            try {
                // 同一路径 → 同一把锁（共享锁表；SharpWavtool 读 / resampler 写都用它）
                object a = Renderers.GetCacheLock(path);
                object b = Renderers.GetCacheLock(path);
                object c = Renderers.GetCacheLock(path + ".other");
                Assert.Same(a, b);
                Assert.NotSame(a, c);

                int sharedErrors = HammerCacheFile(path, sharedLock: true, out int sharedMismatch);
                int splitErrors = HammerCacheFile(path, sharedLock: false, out int splitMismatch);
                output.WriteLine($"共享锁（本树实现）：异常 {sharedErrors} 次，内容不一致 {sharedMismatch} 次");
                output.WriteLine($"拆分锁（此前 SharpWavtool 裸读 / 各用一把锁）：异常 {splitErrors} 次，内容不一致 {splitMismatch} 次");
                // 产品断言：共享锁必须 0 异常、0 内容不一致。
                Assert.Equal(0, sharedErrors);
                Assert.Equal(0, sharedMismatch);
                // "拆分锁是否复现竞争"只是**诊断量**：竞争窗口是时序相关的，抢不到就没有区分度，
                // 但那是测量装置的问题、不是产品缺陷 —— 不能让它把用例判红
                // （W14 flake 修复：此处原为硬断言，满载并行时会随机失败）。
                bool splitReproduced = splitErrors > 0 || splitMismatch > 0;
                output.WriteLine($"A/B 区分度：拆分锁复现竞争={splitReproduced}" +
                                 (splitReproduced ? "" : "（本轮未抢到竞争窗口，仅诊断，不判失败）"));
            } finally {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }

        /// <summary>写入者用产品路径（WaveFileWriter 独占句柄），读取者用 Wave.OpenFile。</summary>
        static int HammerCacheFile(string path, bool sharedLock, out int mismatch) {
            object writerLock = sharedLock ? Renderers.GetCacheLock(path) : new object();
            object readerLock = sharedLock ? Renderers.GetCacheLock(path) : new object();
            var errorBox = new int[1];
            var mismatchBox = new int[1];
            var samples = new float[4096];
            for (int i = 0; i < samples.Length; i++) {
                samples[i] = (float)Math.Sin(2 * Math.PI * 440 * i / 44100) * 0.5f;
            }
            const int rounds = 40;
            var writer = new Thread(() => {
                for (int r = 0; r < rounds; r++) {
                    try {
                        lock (writerLock) {
                            using var writer2 = new WaveFileWriter(path, new WaveFormat(44100, 16, 1));
                            writer2.WriteSamples(samples, 0, samples.Length);
                        }
                    } catch (IOException) {
                        Interlocked.Increment(ref errorBox[0]);
                    } catch (UnauthorizedAccessException) {
                        Interlocked.Increment(ref errorBox[0]);
                    }
                }
            });
            var reader = new Thread(() => {
                for (int r = 0; r < rounds; r++) {
                    try {
                        lock (readerLock) {
                            if (!File.Exists(path)) {
                                continue;
                            }
                            using var stream = Wave.OpenFile(path);
                            var data = Wave.GetSamples(stream.ToSampleProvider().ToMono(1, 0));
                            if (data.Length != samples.Length) {
                                Interlocked.Increment(ref mismatchBox[0]);
                            }
                        }
                    } catch (IOException) {
                        Interlocked.Increment(ref errorBox[0]);
                    } catch (UnauthorizedAccessException) {
                        Interlocked.Increment(ref errorBox[0]);
                    }
                }
            });
            writer.Start();
            reader.Start();
            writer.Join();
            reader.Join();
            mismatch = mismatchBox[0];
            return errorBox[0];
        }
    }
}

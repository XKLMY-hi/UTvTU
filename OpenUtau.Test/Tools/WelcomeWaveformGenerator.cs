using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using OpenUtau.App.Views;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Test.Audio;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.Tools {
    /// <summary>
    /// W38 **资产生成器**：欢迎页左栏"一小节真实演唱"的波形包络。
    ///
    /// 默认**不跑**（环境变量未设 ⇒ 立刻返回，保持全量测试与基线不被污染）。
    /// 手动生成（在本工作树根目录）：
    /// <code>
    /// $env:OPENUTAU_GEN_WELCOME_WAVE='1'
    /// $env:OPENUTAU_GEN_VOICEBANK='C:\Users\&lt;你&gt;\Documents\OpenUtau\Singers\&lt;某个 UTAU 声库&gt;'
    /// $env:OPENUTAU_GEN_WELCOME_OUT='.opencode/design/welcome-waveform'
    /// dotnet test OpenUtau.Test\OpenUtau.Test.csproj --filter FullyQualifiedName~WelcomeWaveformGenerator
    /// </code>
    ///
    /// 两个候选：
    /// - **a：真声库渲染**（走产品渲染链 RenderEngine.RenderMixdown，经典 UTAU 声库 + worldline），
    ///   0.75 s 的一个「a」（自然 ADSR + 声库自带的轻微起伏）；
    /// - **b：自己合成**（共振峰 /a/ 的 F1/F2/F3 + 谐波源 + 5.5 Hz 轻微颤音 + ADSR），
    ///   同一时长，**零版权顾虑** ⇒ 作为随包资产。
    ///
    /// 归一化（两个候选**同一口径**，便于真实比较）：
    ///   ① 取单声道；② 定位有声段；③ 等分 112 个窗口，每窗取 max|x|（绝对值包络）与
    ///   最大 |x| 处的**带符号值**（双极性包络）；④ 两个候选都除以**同一个**全局峰值
    ///   （即 a 的峰值），因此图上"谁更响"是真实差异，不做各自自动增益。
    /// </summary>
    [Collection("AudioFixture")]
    public class WelcomeWaveformGenerator {
        const int Points = 112;          // 96–128 区间内取 112
        const double DurationSec = 0.75;  // 落在 0.6–1.0 s
        readonly ITestOutputHelper output;

        public WelcomeWaveformGenerator(ITestOutputHelper output) {
            this.output = output;
        }

        public sealed class Envelope {
            public string Name { get; set; } = "";
            public string Source { get; set; } = "";
            public double Rate { get; set; }
            public double DurationSec { get; set; }
            public double Peak { get; set; }
            public double RmsDb { get; set; }
            public List<double> Abs { get; set; } = new();
            public List<double> Up { get; set; } = new();
            public List<double> Down { get; set; } = new();
        }

        [Fact]
        public void GenerateWelcomeWaveformAssets() {
            if (Environment.GetEnvironmentVariable("OPENUTAU_GEN_WELCOME_WAVE") != "1") {
                return;   // 默认 no-op：不写文件、不影响基线
            }
            string outDir = Environment.GetEnvironmentVariable("OPENUTAU_GEN_WELCOME_OUT")
                ?? Path.Combine(".opencode", "design", "welcome-waveform");
            outDir = Path.GetFullPath(outDir);
            Directory.CreateDirectory(outDir);

            // ── 候选 b：合成 /a/（先算，因为它不需要原生库）──
            var (bSamples, bPitch) = SynthesizeA(DurationSec);

            // ── 候选 a：真声库 + 产品渲染链 ──
            float[]? aSamples = null;
            string aSource = "不可用（原生 worldline 未加载或未找到声库）";
            string? bank = FindVoicebank();
            string why = "";
            if (bank != null && NativeWorldline.EnsureLoaded(out why)) {
                try {
                    aSamples = RenderA(bank);
                    aSource = $"产品渲染链 + 声库「{Path.GetFileName(bank)}」";
                } catch (Exception ex) {
                    output.WriteLine($"真声库渲染失败：{ex.Message}");
                    try {
                        string fallback = Path.Combine(Path.GetTempPath(), "w38-fallback");
                        Directory.CreateDirectory(fallback);
                        string dir = Path.Combine(fallback, DummyVoicebank.FolderName);
                        if (!File.Exists(Path.Combine(dir, "character.txt"))) {
                            dir = DummyVoicebank.Create(fallback);
                        }
                        aSamples = RenderA(dir);
                        aSource = $"产品渲染链 + **伪声库**（真声库「{Path.GetFileName(bank)}」失败：{ex.GetType().Name}）";
                    } catch (Exception ex2) {
                        aSource = $"渲染失败（真声库 {ex.GetType().Name}／伪声库 {ex2.GetType().Name}）";
                    }
                }
            } else if (bank == null) {
                aSource = "未找到真声库（设 OPENUTAU_GEN_VOICEBANK 指定）";
            } else {
                aSource = $"原生 worldline 不可用：{why}";
            }

            // 统一归一化分母（两个候选共用 ⇒ 真实电平差异可见）
            // 分母统一取**单声道**峰值：AudioMeasure.Peak 会把各声道**相加**（双声道同相 ⇒ 2×），
            // 直接拿它当分母会让整幅画幅缩到一半。两个候选共用同一分母 ⇒ 电平差异仍真实可见。
            double sharedPeak = Math.Max(
                AudioMeasure.Peak(AudioMeasure.Mono(aSamples ?? Array.Empty<float>(), AudioFixtures.Channels)),
                AudioMeasure.Peak(AudioMeasure.Mono(bSamples, AudioFixtures.Channels)));
            if (sharedPeak <= 0) {
                sharedPeak = 1.0;
            }

            var envA = aSamples == null ? null : EnvelopeOf("a · 真声库渲染", aSource, aSamples, sharedPeak);
            var envB = EnvelopeOf("b · 自合成 /a/", "共振峰 F1/F2/F3 + 谐波 + 5.5Hz 颤音 + ADSR", bSamples, sharedPeak);

            var json = new Dictionary<string, object> {
                ["generatedBy"] = "OpenUtau.Test/Tools/WelcomeWaveformGenerator.cs",
                ["points"] = Points,
                ["normalization"] = "共用峰值归一（分母 = 两候选的全局峰值）；Abs=max|x|，Up/Down=最大|x|处的带符号值",
                ["candidates"] = new Dictionary<string, Envelope?> { ["a"] = envA, ["b"] = envB },
                ["pitch"] = bPitch,
            };
            string jsonPath = Path.Combine(outDir, "waveform.json");
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(json,
                new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));

            // ── 出图（2x）──
            string pngB = Path.Combine(outDir, "waveform-b.png");
            DrawEnvelope(envB, pngB, "b  自合成 /a/  (F1/F2/F3 + harmonics + vibrato)");
            string? pngA = null;
            if (envA != null) {
                pngA = Path.Combine(outDir, "waveform-a.png");
                DrawEnvelope(envA, pngA, "a  rendered from voicebank (product chain)");
            }
            string cmp = Path.Combine(outDir, "waveform-compare.png");
            DrawCompare(envA, envB, cmp);

            // ── XAML 几何（候选 b，可直接贴进视图）──
            string xamlPath = Path.Combine(outDir, "waveform-b.xaml");
            File.WriteAllText(xamlPath, BuildXaml(envB, bPitch), new UTF8Encoding(false));

            string logPath = Path.Combine(outDir, "waveform.log");
            File.WriteAllText(logPath, string.Join(Environment.NewLine, new[] {
                $"outDir = {outDir}",
                $"voicebank = {bank ?? "(未找到)"}",
                $"native = {(bank != null && aSamples != null ? "worldline OK" : why)}",
                $"a: {aSource}",
                $"sharedPeak = {sharedPeak:F4}",
                $"a envelope = {(envA == null ? "(未生成)" : $"dur {envA.DurationSec:F3}s peak {envA.Peak:F4} RMS {envA.RmsDb:F1} dBFS")}",
                $"b envelope = dur {envB.DurationSec:F3}s peak {envB.Peak:F4} RMS {envB.RmsDb:F1} dBFS",
            }), new UTF8Encoding(false));
            output.WriteLine($"out = {outDir}");
            foreach (string f in new[] { jsonPath, pngB, pngA, cmp, xamlPath }.Where(p => p != null)) {
                output.WriteLine($"  {Path.GetFileName(f)}  {new FileInfo(f).Length} bytes");
            }
            output.WriteLine($"a: {aSource}");
            Assert.True(File.Exists(jsonPath));
        }

        // ══════════════ 候选 a：真声库 + 产品渲染链 ══════════════

        static string? FindVoicebank() {
            string? explicitPath = Environment.GetEnvironmentVariable("OPENUTAU_GEN_VOICEBANK");
            if (!string.IsNullOrEmpty(explicitPath) && File.Exists(Path.Combine(explicitPath, "character.txt"))) {
                return explicitPath;
            }
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Documents", "OpenUtau", "Singers");
            if (!Directory.Exists(root)) {
                return null;
            }
            // 找一个有 oto.ini 且含 "a" 别名的 UTAU 声库
            foreach (string dir in Directory.GetDirectories(root)) {
                if (!File.Exists(Path.Combine(dir, "character.txt"))) {
                    continue;
                }
                string? oto = Directory.GetFiles(dir, "oto.ini", SearchOption.AllDirectories).FirstOrDefault();
                if (oto == null) {
                    continue;
                }
                string text;
                try {
                    text = File.ReadAllText(oto, Encoding.GetEncoding(932));
                } catch {
                    continue;
                }
                if (text.Split('\n').Any(line => line.Split('=')[0].Trim().Equals("a", StringComparison.OrdinalIgnoreCase))) {
                    return dir;
                }
            }
            return null;
        }

        static float[] RenderA(string voicebankDir) {
            var singer = DummyVoicebank.Load(voicebankDir);   // 通用 UTAU 声库加载器
            // 720 ticks ≈ 0.75 s（120 BPM，1 拍 480 ticks）
            var (project, part) = ClassicRenderSetup.BuildProject(singer, ("a", 60, 720));
            using var session = ClassicRenderSetup.StartPhonemizer(project);
            ClassicRenderSetup.PhonemizeAndWait(project, part, _ => { });
            int frames = (int)Math.Round(1.1 * AudioFixtures.Rate);
            return AudioFixtures.RenderMixdown(project, frames * AudioFixtures.Channels);
        }

        // ══════════════ 候选 b：自合成 /a/ ══════════════

        /// <summary>共振峰 + 谐波 + 颤音 + ADSR 合成 /a/；返回样本与 f0 曲线（用于音高线）。</summary>
        static (float[] Samples, List<double> Pitch) SynthesizeA(double seconds) {
            double rate = AudioFixtures.Rate;   // 跟随产品采样率（硬度 44100 会让时长口径错）
            const double f0Base = 220.0;      // A3
            const double vibHz = 5.5;         // 轻微颤音
            const double vibDepth = 0.35;     // 半音
            // /a/ 的共振峰（中性偏低音色）
            (double f, double bw, double gain)[] formants = {
                (800, 80, 1.00), (1200, 90, 0.55), (2800, 120, 0.22),
            };
            int n = (int)(seconds * rate);
            var samples = new float[n];
            var pitch = new List<double>();
            int pitchPoints = 112;
            // 二阶谐振器状态
            var y1 = new double[formants.Length];
            var y2 = new double[formants.Length];
            var coeff = formants.Select(f => {
                double r = Math.Exp(-Math.PI * f.bw / rate);
                double theta = 2 * Math.PI * f.f / rate;
                return (a1: 2 * r * Math.Cos(theta), a2: -r * r);
            }).ToArray();
            for (int i = 0; i < n; i++) {
                double t = i / rate;
                // ADSR
                double env = t < 0.045 ? t / 0.045
                    : t < 0.135 ? 1.0 - 0.28 * (t - 0.045) / 0.09
                    : t < seconds - 0.20 ? 0.72 + 0.02 * Math.Sin(2 * Math.PI * 3.1 * t)
                    : Math.Max(0.0, (0.92 - 0.28 * 0.5) * (seconds - t) / 0.20) * 0.8;
                // 颤音：延迟 150ms 渐入
                double vibRamp = t < 0.15 ? 0 : Math.Min(1.0, (t - 0.15) / 0.35);
                double f0 = f0Base * Math.Pow(2, vibDepth * vibRamp * Math.Sin(2 * Math.PI * vibHz * t) / 12.0);
                // 声门源：谐波叠加（1/n^1.2，上限 40 次或 Nyquist）
                double src = 0;
                int maxH = Math.Min(40, (int)(rate / 2 / f0));
                double phase = 0;
                for (int h = 1; h <= maxH; h++) {
                    src += Math.Sin(2 * Math.PI * f0 * h * t) / Math.Pow(h, 1.2);
                }
                src *= 0.5;
                // 三个共振峰并联
                double voiced = 0;
                for (int k = 0; k < formants.Length; k++) {
                    double y = src + coeff[k].a1 * y1[k] + coeff[k].a2 * y2[k];
                    y2[k] = y1[k];
                    y1[k] = y;
                    voiced += formants[k].gain * y;
                }
                samples[i] = (float)(voiced * env * 0.25);
                if (i % Math.Max(1, n / pitchPoints) == 0 && pitch.Count < pitchPoints) {
                    pitch.Add(f0);
                }
            }
            // 归一到 0.7 峰：纯合成信号的共振峰增益不可控，归一后 a/b 的电平比较才有意义
            double pk = 0;
            foreach (float v in samples) {
                pk = Math.Max(pk, Math.Abs(v));
            }
            if (pk > 0) {
                float gain = (float)(0.7 / pk);
                for (int i = 0; i < samples.Length; i++) {
                    samples[i] *= gain;
                }
            }
            // 交叠成产品链期望的声道数：单声道数组会被当成立体声读 ⇒ 时长减半、峰值翻倍
            int ch = AudioFixtures.Channels;
            if (ch > 1) {
                var interleaved = new float[samples.Length * ch];
                for (int i = 0; i < samples.Length; i++) {
                    for (int c = 0; c < ch; c++) {
                        interleaved[i * ch + c] = samples[i];
                    }
                }
                samples = interleaved;
            }
            return (samples, pitch);
        }

        // ══════════════ 包络 / 出图 / XAML ══════════════

        static Envelope EnvelopeOf(string name, string source, float[] interleaved, double sharedPeak) {
            float[] mono = AudioMeasure.Mono(interleaved, AudioFixtures.Channels);
            // 定位有声段（阈值 = 峰值 5%）
            double peak = AudioMeasure.Peak(mono);
            int first = 0, last = mono.Length - 1;
            double thr = peak * 0.05;
            while (first < mono.Length && Math.Abs(mono[first]) < thr) {
                first++;
            }

            int len = Math.Max(1, last - first + 1);
            var abs = new List<double>();
            var up = new List<double>();
            var dn = new List<double>();
            for (int p = 0; p < Points; p++) {
                int s = first + (int)((long)len * p / Points);
                int e = first + (int)((long)len * (p + 1) / Points);
                e = Math.Min(e, last + 1);
                double m = 0, signed = 0;
                for (int i = s; i < e; i++) {
                    double v = mono[i];
                    if (Math.Abs(v) > m) {
                        m = Math.Abs(v);
                        signed = v;
                    }
                }
                abs.Add(m / sharedPeak);
                up.Add(Math.Max(0, signed) / sharedPeak);
                dn.Add(Math.Min(0, signed) / sharedPeak);
            }
            return new Envelope {
                Name = name,
                Source = source,
                Rate = AudioFixtures.Rate,
                DurationSec = (double)len / AudioFixtures.Rate,
                Peak = peak,
                RmsDb = AudioMeasure.RmsDb(mono),
                Abs = abs,
                Up = up,
                Down = dn,
            };
        }

        /// <summary>2x PNG：填充的绝对值包络 + 双极性轮廓 + 零线 + 标题。</summary>
        static void DrawEnvelope(Envelope env, string path, string label) {
            const int w = 1200, h = 240, scale = 2;
            using var bmp = new System.Drawing.Bitmap(w * scale, h * scale);
            using var g = System.Drawing.Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.FromArgb(255, 20, 18, 24));
            using var axisPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(255, 90, 84, 100), 1 * scale);
            g.DrawLine(axisPen, 0, h / 2 * scale, w * scale, h / 2 * scale);
            // 绝对值包络（填充）：把 abs 上下对称画成"响度块"
            using var fill = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(90, 200, 190, 235));
            var pts = new List<System.Drawing.PointF>();
            for (int i = 0; i < env.Abs.Count; i++) {
                float x = (float)i / (env.Abs.Count - 1) * w;
                float y = (float)(h / 2 - env.Abs[i] * (h / 2 - 8) * 0.55);
                pts.Add(new System.Drawing.PointF(x * scale, y * scale));
            }
            for (int i = env.Abs.Count - 1; i >= 0; i--) {
                float x = (float)i / (env.Abs.Count - 1) * w;
                float y = (float)(h / 2 + env.Abs[i] * (h / 2 - 8) * 0.55);
                pts.Add(new System.Drawing.PointF(x * scale, y * scale));
            }
            g.FillPolygon(fill, pts.ToArray());
            // 双极性轮廓（实线）
            using var linePen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(255, 205, 198, 235), 1.6f * scale);
            var top = env.Up.Select((v, i) => new System.Drawing.PointF(
                (float)i / (env.Up.Count - 1) * w * scale,
                (float)(h / 2 - v * (h / 2 - 8)) * scale)).ToArray();
            var bottom = env.Down.Select((v, i) => new System.Drawing.PointF(
                (float)i / (env.Down.Count - 1) * w * scale,
                (float)(h / 2 - v * (h / 2 - 8)) * scale)).ToArray();
            g.DrawLines(linePen, top);
            g.DrawLines(linePen, bottom);
            using var font = new System.Drawing.Font("Consolas", 9 * scale);
            using var textBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 200, 195, 215));
            g.DrawString(label, font, textBrush, 6 * scale, 4 * scale);
            g.DrawString($"{env.Abs.Count} pts · dur {env.DurationSec:F2}s · peak {env.Peak:F3} · RMS {env.RmsDb:F1} dBFS",
                font, textBrush, 6 * scale, 18 * scale);
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }

        static void DrawCompare(Envelope? a, Envelope b, string path) {
            const int w = 1200, rowH = 200, scale = 2;
            int rows = a == null ? 1 : 2;
            using var bmp = new System.Drawing.Bitmap(w * scale, rowH * rows * scale);
            using var g = System.Drawing.Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.FromArgb(255, 16, 15, 20));
            using var font = new System.Drawing.Font("Consolas", 9 * scale);
            using var textBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 205, 200, 220));
            void Row(Envelope env, int index, string title) {
                int oy = index * rowH;
                g.DrawLine(new System.Drawing.Pen(System.Drawing.Color.FromArgb(255, 80, 76, 92), scale),
                    0, (oy + rowH / 2) * scale, w * scale, (oy + rowH / 2) * scale);
                using var fill = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(80, 190, 182, 228));
                var pts = new List<System.Drawing.PointF>();
                for (int i = 0; i < env.Abs.Count; i++) {
                    pts.Add(new System.Drawing.PointF(
                        (float)i / (env.Abs.Count - 1) * w * scale,
                        (oy + rowH / 2 - (float)(env.Abs[i] * (rowH / 2 - 14))) * scale));
                }
                for (int i = env.Abs.Count - 1; i >= 0; i--) {
                    pts.Add(new System.Drawing.PointF(
                        (float)i / (env.Abs.Count - 1) * w * scale,
                        (oy + rowH / 2 + (float)(env.Abs[i] * (rowH / 2 - 14))) * scale));
                }
                g.FillPolygon(fill, pts.ToArray());
                using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(255, 210, 204, 240), 1.4f * scale);
                g.DrawLines(pen, env.Up.Select((v, i) => new System.Drawing.PointF(
                    (float)i / (env.Up.Count - 1) * w * scale,
                    (oy + rowH / 2 - (float)(v * (rowH / 2 - 14))) * scale)).ToArray());
                g.DrawLines(pen, env.Down.Select((v, i) => new System.Drawing.PointF(
                    (float)i / (env.Down.Count - 1) * w * scale,
                    (oy + rowH / 2 - (float)(v * (rowH / 2 - 14))) * scale)).ToArray());
                g.DrawString(title, font, textBrush, 6 * scale, (oy + 4) * scale);
                g.DrawString($"peak {env.Peak:F3} · RMS {env.RmsDb:F1} dBFS · {env.Abs.Count} pts",
                    font, textBrush, 6 * scale, (oy + 18) * scale);
            }
            int r = 0;
            if (a != null) {
                Row(a, r++, $"a  {a.Source}");
            }
            Row(b, r, $"b  {b.Source}");
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }

        /// <summary>把候选 b 的包络与 f0 曲线转成可直接贴进 XAML 的几何（1000×240 视图盒）。</summary>
        static string BuildXaml(Envelope env, List<double> pitch) {
            var sb = new StringBuilder();
            sb.AppendLine("<!-- 由 OpenUtau.Test/Tools/WelcomeWaveformGenerator.cs 生成，勿手改 -->");
            sb.AppendLine("<!-- viewBox 1000x240，中线 y=120；填充轮廓 = 双极性包络 -->");
            sb.Append("waveform: ");
            sb.Append(WaveGeometry(env));
            sb.AppendLine();
            sb.Append("pitch:    ");
            sb.Append(PitchGeometry(pitch));
            sb.AppendLine();
            return sb.ToString();
        }

        internal static string WaveGeometry(Envelope env) {
            var sb = new StringBuilder("M ");
            for (int i = 0; i < env.Up.Count; i++) {
                double x = 1000.0 * i / (env.Up.Count - 1);
                double y = 120 - env.Up[i] * 100;
                sb.Append(F(x)).Append(',').Append(F(y)).Append(' ');
                if (i < env.Up.Count - 1) {
                    sb.Append("L ");
                }
            }
            for (int i = env.Down.Count - 1; i >= 0; i--) {
                double x = 1000.0 * i / (env.Down.Count - 1);
                double y = 120 - env.Down[i] * 100;
                sb.Append("L ").Append(F(x)).Append(',').Append(F(y)).Append(' ');
            }
            sb.Append('Z');
            return sb.ToString();
        }

        internal static string PitchGeometry(List<double> pitch) {
            if (pitch.Count < 2) {
                return "";
            }
            double lo = pitch.Min(), hi = pitch.Max();
            double span = Math.Max(1e-9, hi - lo);
            var sb = new StringBuilder("M ");
            for (int i = 0; i < pitch.Count; i++) {
                double x = 1000.0 * i / (pitch.Count - 1);
                double y = 190 - (pitch[i] - lo) / span * 150;   // 落在下方 40…190 带内
                sb.Append(F(x)).Append(',').Append(F(y)).Append(' ');
                if (i < pitch.Count - 1) {
                    sb.Append("L ");
                }
            }
            return sb.ToString();
        }

        static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    }
}

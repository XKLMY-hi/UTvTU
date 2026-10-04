using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenUtau.Test.TestSupport {
    /// <summary>
    /// W7 音频测量工具（无外部依赖：自带 FFT/Goertzel/biquad，不联网、不读用户机器上的任何东西）。
    ///
    /// **口径说明（务必先读，断言阈值依赖它）**
    /// <list type="bullet">
    /// <item>输入一律为**交织 float 立体声**（<c>AudioSettings.Channels</c> 声道），
    /// 采样率由调用方给出（测试夹具固定 44100）。</item>
    /// <item><see cref="ToDb"/> = 20·log10；<see cref="PowerToDb"/> = 10·log10；
    /// 小于 1e-12 的线性值报 <see cref="SilentDb"/>（-240 dB），不返回 -∞。</item>
    /// <item><see cref="BandEnergyDb"/>：Hann 窗 + 2048 点 FFT（50% 重叠）累加功率，
    /// 单 bin 按 <c>2|X|²/S1²</c> 归一（S1 = 窗和）→ 返回该频带的**等效 RMS（dBFS）**
    /// （相干正弦下与真实 RMS 一致；宽带噪声略高估）。只用于同一夹具下的相对比较。</item>
    /// <item><see cref="GoertzelPowerDb"/>：单频点窄带幅度（dBFS 峰值），分辨率与窗长无关。</item>
    /// <item><see cref="LufsApprox"/>：**近似**，不是 BS.1770 实现。① K-weighting 用 RBJ 公式在
    /// 目标采样率重算两段 biquad（+4 dB 高架 @1681.97 Hz Q=0.7071，再 38.14 Hz 高通 Q=0.5）；
    /// ② **不做门限（gating）**，直接对整段求均方；③ 加 -0.691 dB 偏移。
    /// 仅适合同一夹具的 A/B 相对比较，**不可当绝对 LUFS 标称值**。</item>
    /// <item><see cref="Rt60Approx"/>：对（交织→单声道求和）信号做 Schroeder 反向积分能量曲线，
    /// 在 -5 dB 到 min(-15 dB, 末端) 区间最小二乘拟合 dB/s 斜率，外推 60 dB 得秒数；
    /// 可达动态 &lt; 6 dB 时返回 <see cref="double.NaN"/>。</item>
    /// <item><see cref="Correlation"/>：左右声道 Pearson 相关系数（1 = 完全同相，0 = 无关）。</item>
    /// </list>
    /// </summary>
    internal static class AudioMeasure {
        public const double SilentDb = -240.0;
        public const int FftSize = 2048;

        // ── 基础 ─────────────────────────────────────────────────────────

        public static double ToDb(double linear) => linear <= 1e-12 ? SilentDb : 20.0 * Math.Log10(linear);

        public static double PowerToDb(double power) => power <= 1e-24 ? SilentDb : 10.0 * Math.Log10(power);

        /// <summary>交织 → 单声道（逐声道**求和**）。求和口径与混响/SharpWavtool 的 mono sum 一致；
        /// 需要"每声道电平"的频带分析请用 <see cref="MonoAverage"/>。</summary>
        public static float[] Mono(float[] interleaved, int channels) {
            if (channels <= 1) {
                return interleaved;
            }
            var mono = new float[interleaved.Length / channels];
            for (int i = 0; i < mono.Length; i++) {
                float sum = 0;
                for (int c = 0; c < channels; c++) {
                    sum += interleaved[i * channels + c];
                }
                mono[i] = sum;
            }
            return mono;
        }

        /// <summary>交织 → 单声道（逐声道**平均**）。双声道同相内容下这是"每声道电平"口径
        /// （求和会高出 20log10(channels) = 6.02 dB）。</summary>
        public static float[] MonoAverage(float[] interleaved, int channels) {
            if (channels <= 1) {
                return interleaved;
            }
            var mono = Mono(interleaved, channels);
            for (int i = 0; i < mono.Length; i++) {
                mono[i] /= channels;
            }
            return mono;
        }

        /// <summary>按时间窗切出交织片段（毫秒）。越界自动截断；空窗返回空数组。</summary>
        public static float[] SliceMs(float[] interleaved, int channels, int sampleRate, double fromMs, double toMs) {
            int from = Math.Clamp((int)(fromMs / 1000.0 * sampleRate) * channels, 0, interleaved.Length);
            int to = Math.Clamp((int)(toMs / 1000.0 * sampleRate) * channels, from, interleaved.Length);
            var slice = new float[to - from];
            Array.Copy(interleaved, from, slice, 0, slice.Length);
            return slice;
        }

        public static double Peak(float[] x, int from = 0, int count = -1) {
            if (count < 0) {
                count = x.Length - from;
            }
            double peak = 0;
            for (int i = from; i < from + count && i < x.Length; i++) {
                double v = Math.Abs(x[i]);
                if (v > peak) {
                    peak = v;
                }
            }
            return peak;
        }

        public static double PeakDb(float[] x, int from = 0, int count = -1) => ToDb(Peak(x, from, count));

        public static double Rms(float[] x, int from = 0, int count = -1) {
            if (count < 0) {
                count = x.Length - from;
            }
            if (count <= 0) {
                return 0;
            }
            double sum = 0;
            for (int i = from; i < from + count && i < x.Length; i++) {
                sum += (double)x[i] * x[i];
            }
            return Math.Sqrt(sum / count);
        }

        public static double RmsDb(float[] x, int from = 0, int count = -1) => ToDb(Rms(x, from, count));

        /// <summary>相邻样本最大差（爆音/瞬变判据）。</summary>
        public static double MaxAdjacentDelta(float[] x) {
            double max = 0;
            for (int i = 1; i < x.Length; i++) {
                double d = Math.Abs(x[i] - x[i - 1]);
                if (d > max) {
                    max = d;
                }
            }
            return max;
        }

        /// <summary>逐样本完全一致（导出确定性判据）。返回首个差异索引，-1 = 完全相同。</summary>
        public static int FirstDifference(float[] a, float[] b) {
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++) {
                if (a[i] != b[i]) {
                    return i;
                }
            }
            return a.Length == b.Length ? -1 : n;
        }

        // ── 频谱 ─────────────────────────────────────────────────────────

        static double[] HannWindow(int n) {
            var w = new double[n];
            for (int i = 0; i < n; i++) {
                w[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (n - 1)));
            }
            return w;
        }

        static readonly Dictionary<int, double[]> windowCache = new();

        static double[] Window(int n) {
            lock (windowCache) {
                if (!windowCache.TryGetValue(n, out var w)) {
                    w = HannWindow(n);
                    windowCache[n] = w;
                }
                return w;
            }
        }

        /// <summary>原地 radix-2 FFT（长度必须是 2 的幂）。</summary>
        public static void Fft(double[] re, double[] im) {
            int n = re.Length;
            if (n <= 1) {
                return;
            }
            if ((n & (n - 1)) != 0) {
                throw new ArgumentException($"FFT 长度必须是 2 的幂：{n}");
            }
            for (int i = 1, j = 0; i < n; i++) {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) {
                    j ^= bit;
                }
                j ^= bit;
                if (i < j) {
                    (re[i], re[j]) = (re[j], re[i]);
                    (im[i], im[j]) = (im[j], im[i]);
                }
            }
            for (int len = 2; len <= n; len <<= 1) {
                double ang = -2 * Math.PI / len;
                double wRe = Math.Cos(ang), wIm = Math.Sin(ang);
                for (int i = 0; i < n; i += len) {
                    double curRe = 1, curIm = 0;
                    for (int k = 0; k < len / 2; k++) {
                        int a = i + k, b = i + k + len / 2;
                        double xRe = re[b] * curRe - im[b] * curIm;
                        double xIm = re[b] * curIm + im[b] * curRe;
                        re[b] = re[a] - xRe;
                        im[b] = im[a] - xIm;
                        re[a] += xRe;
                        im[a] += xIm;
                        double nextRe = curRe * wRe - curIm * wIm;
                        curIm = curRe * wIm + curIm * wRe;
                        curRe = nextRe;
                    }
                }
            }
        }

        /// <summary>
        /// 频带能量（dBFS）：Hann 窗 + 2048 点 FFT（50% 重叠）累加功率后，取频带内**峰 bin**
        /// 并按 <c>2|X|²/S1²</c> 归一（相干音口径：单频正弦下等于真实 RMS，仪器自检
        /// <c>MeasurementToolSelfCheckTests</c> 用解析正弦标定）。
        /// 取峰 bin 而非频带内各 bin 求和，是因为求和会把 Hann 主瓣的多个 bin 重复计入
        /// （实测高估 ~8 dB）。宽带信号请改用 <see cref="RmsDb"/>。
        /// </summary>
        public static double BandEnergyDb(float[] interleaved, int channels, double lowHz, double highHz, int sampleRate) {
            // 用**每声道平均**（不是求和）：双声道同相时求和会高 6.02 dB，绝对标定就偏了。
            var mono = MonoAverage(interleaved, channels);
            if (mono.Length < FftSize) {
                return SilentDb;
            }
            var window = Window(FftSize);
            double s1 = window.Sum();
            int half = FftSize / 2;
            var acc = new double[half + 1];
            int frames = 0;
            for (int start = 0; start + FftSize <= mono.Length; start += FftSize / 2) {
                var re = new double[FftSize];
                var im = new double[FftSize];
                for (int i = 0; i < FftSize; i++) {
                    re[i] = mono[start + i] * window[i];
                }
                Fft(re, im);
                for (int k = 0; k <= half; k++) {
                    acc[k] += re[k] * re[k] + im[k] * im[k];
                }
                frames++;
            }
            if (frames == 0) {
                return SilentDb;
            }
            double binHz = (double)sampleRate / FftSize;
            int k0 = Math.Max(1, (int)Math.Ceiling(lowHz / binHz));
            int k1 = Math.Min(half, (int)Math.Floor(highHz / binHz));
            double power = 0;
            for (int k = k0; k <= k1; k++) {
                double bin = 2.0 * (acc[k] / frames) / (s1 * s1);
                if (bin > power) {
                    power = bin;
                }
            }
            return PowerToDb(power);
        }

        /// <summary>单频点幅度（dBFS 峰值）——Goertzel，窄带、与 FFT 分辨率无关。</summary>
        public static double GoertzelPowerDb(float[] mono, double freqHz, int sampleRate) {
            int n = mono.Length;
            if (n == 0) {
                return SilentDb;
            }
            double w = 2 * Math.PI * freqHz / sampleRate;
            double c = 2 * Math.Cos(w);
            double s1 = 0, s2 = 0;
            for (int i = 0; i < n; i++) {
                double s0 = mono[i] + c * s1 - s2;
                s2 = s1;
                s1 = s0;
            }
            double power = s1 * s1 + s2 * s2 - c * s1 * s2;
            double amplitude = 2 * Math.Sqrt(Math.Max(0, power)) / n;
            return ToDb(amplitude);
        }

        /// <summary>FFT 峰值频率（抛物线插值，Hz）。用于"输出里确实是这个音高"的独立判据。</summary>
        public static double DominantFreq(float[] interleaved, int channels, int sampleRate, double minHz = 50, double maxHz = 4000) {
            var mono = MonoAverage(interleaved, channels);
            if (mono.Length < FftSize) {
                return double.NaN;
            }
            var window = Window(FftSize);
            int half = FftSize / 2;
            var acc = new double[half + 1];
            int frames = 0;
            for (int start = 0; start + FftSize <= mono.Length; start += FftSize / 2) {
                var re = new double[FftSize];
                var im = new double[FftSize];
                for (int i = 0; i < FftSize; i++) {
                    re[i] = mono[start + i] * window[i];
                }
                Fft(re, im);
                for (int k = 0; k <= half; k++) {
                    acc[k] += re[k] * re[k] + im[k] * im[k];
                }
                frames++;
            }
            double binHz = (double)sampleRate / FftSize;
            int k0 = Math.Max(1, (int)Math.Ceiling(minHz / binHz));
            int k1 = Math.Min(half - 1, (int)Math.Floor(maxHz / binHz));
            int best = k0;
            for (int k = k0; k <= k1; k++) {
                if (acc[k] > acc[best]) {
                    best = k;
                }
            }
            double a = Math.Log(Math.Max(acc[best - 1], 1e-30));
            double b = Math.Log(Math.Max(acc[best], 1e-30));
            double c2 = Math.Log(Math.Max(acc[best + 1], 1e-30));
            double delta = 0.5 * (a - c2) / (a - 2 * b + c2);
            if (double.IsNaN(delta) || Math.Abs(delta) > 1) {
                delta = 0;
            }
            return (best + delta) * binHz;
        }

        // ── 响度（近似） ─────────────────────────────────────────────────

        sealed class Biquad {
            double b0 = 1, b1, b2, a1, a2;
            double x1, x2, y1, y2;

            public void SetHighShelf(double fs, double f0, double q, double gainDb) {
                double a = Math.Pow(10, gainDb / 40);
                double w0 = 2 * Math.PI * f0 / fs;
                double cw = Math.Cos(w0), sw = Math.Sin(w0);
                double alpha = sw / (2 * q);
                double sq = 2 * Math.Sqrt(a) * alpha;
                double a0 = (a + 1) - (a - 1) * cw + sq;
                b0 = a * ((a + 1) + (a - 1) * cw + sq) / a0;
                b1 = -2 * a * ((a - 1) + (a + 1) * cw) / a0;
                b2 = a * ((a + 1) + (a - 1) * cw - sq) / a0;
                a1 = 2 * ((a - 1) - (a + 1) * cw) / a0;
                a2 = ((a + 1) - (a - 1) * cw - sq) / a0;
            }

            public void SetHighPass(double fs, double f0, double q) {
                double w0 = 2 * Math.PI * f0 / fs;
                double cw = Math.Cos(w0), sw = Math.Sin(w0);
                double alpha = sw / (2 * q);
                double a0 = 1 + alpha;
                b0 = (1 + cw) / 2 / a0;
                b1 = -(1 + cw) / a0;
                b2 = (1 + cw) / 2 / a0;
                a1 = -2 * cw / a0;
                a2 = (1 - alpha) / a0;
            }

            public double Process(double x) {
                double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x;
                y2 = y1; y1 = y;
                return y;
            }
        }

        /// <summary>近似 LUFS（K-weighting 重算 + 无门限 + -0.691 dB 偏移）。见类注释。</summary>
        public static double LufsApprox(float[] interleaved, int channels, int sampleRate) {
            var mono = Mono(interleaved, channels);
            if (mono.Length == 0) {
                return SilentDb;
            }
            var shelf = new Biquad();
            shelf.SetHighShelf(sampleRate, 1681.97, 0.7071, 4.0);
            var hp = new Biquad();
            hp.SetHighPass(sampleRate, 38.14, 0.5);
            double sum = 0;
            for (int i = 0; i < mono.Length; i++) {
                double y = hp.Process(shelf.Process(mono[i]));
                sum += y * y;
            }
            double meanSquare = sum / mono.Length;
            if (meanSquare <= 1e-24) {
                return SilentDb;
            }
            return -0.691 + 10 * Math.Log10(meanSquare);
        }

        // ── 衰减时间 ─────────────────────────────────────────────────────

        /// <summary>Schroeder 反向积分 → RT60 近似（秒）。见类注释。</summary>
        public static double Rt60Approx(float[] interleaved, int channels, int sampleRate, double startMs = 0) {
            var mono = Mono(interleaved, channels);
            int start = Math.Clamp((int)(startMs / 1000.0 * sampleRate), 0, Math.Max(0, mono.Length - 1));
            int n = mono.Length - start;
            if (n < sampleRate / 20) { // 至少 50 ms
                return double.NaN;
            }
            var edc = new double[n];
            double acc = 0;
            for (int i = n - 1; i >= 0; i--) {
                acc += (double)mono[start + i] * mono[start + i];
                edc[i] = acc;
            }
            double total = edc[0];
            if (total <= 1e-12) {
                return double.NaN;
            }
            // 收集 -5 .. min(-15, 末端) dB 区间的 (时间, 相对 dB) 采样
            double upperDb = -5.0, lowerDb = -15.0;
            var ts = new List<double>();
            var dbs = new List<double>();
            for (int i = 0; i < n; i++) {
                double db = 10 * Math.Log10(Math.Max(edc[i], 1e-30) / total);
                if (db <= upperDb && db >= lowerDb) {
                    ts.Add((double)i / sampleRate);
                    dbs.Add(db);
                }
            }
            if (ts.Count < 8) {
                return double.NaN;
            }
            double spanDb = dbs[0] - dbs[^1];
            if (spanDb < 6.0) {
                return double.NaN;
            }
            // 最小二乘拟合 db = a*t + b
            double meanT = ts.Average(), meanDb = dbs.Average();
            double num = 0, den = 0;
            for (int i = 0; i < ts.Count; i++) {
                num += (ts[i] - meanT) * (dbs[i] - meanDb);
                den += (ts[i] - meanT) * (ts[i] - meanT);
            }
            double slope = num / den; // dB/s（负）
            if (slope >= -1e-6) {
                return double.NaN;
            }
            return 60.0 / Math.Abs(slope);
        }

        // ── 立体声 ───────────────────────────────────────────────────────

        public static (double left, double right) ChannelRmsDb(float[] interleaved, int channels = 2) {
            double sl = 0, sr = 0;
            int frames = interleaved.Length / channels;
            for (int i = 0; i < frames; i++) {
                double l = interleaved[i * channels];
                double r = channels > 1 ? interleaved[i * channels + 1] : l;
                sl += l * l;
                sr += r * r;
            }
            if (frames == 0) {
                return (SilentDb, SilentDb);
            }
            return (ToDb(Math.Sqrt(sl / frames)), ToDb(Math.Sqrt(sr / frames)));
        }

        /// <summary>右/左 RMS 比（dB，正 = 偏右）。</summary>
        public static double BalanceDb(float[] interleaved, int channels = 2) {
            var (l, r) = ChannelRmsDb(interleaved, channels);
            if (l <= SilentDb && r <= SilentDb) {
                return 0;
            }
            return r - l;
        }

        /// <summary>
        /// b 相对 a 的最佳延迟（毫秒）：在 ±<paramref name="maxLagMs"/> 内找使
        /// Σ a[n−L]·b[n] 归一化相关性最大的 L。用于测量"同一信号被延迟了多少"
        /// （如混响预延迟、导出对齐）。
        /// </summary>
        public static double BestLagMs(float[] a, float[] b, int channels, int sampleRate,
                                       double fromMs, double toMs, double maxLagMs = 200) {
            var ma = Mono(SliceMs(a, channels, sampleRate, fromMs, toMs), channels);
            var mb = Mono(SliceMs(b, channels, sampleRate, fromMs, toMs), channels);
            if (ma.Length == 0 || mb.Length == 0) {
                return double.NaN;
            }
            int maxLag = (int)(maxLagMs / 1000.0 * sampleRate);
            double best = double.NegativeInfinity;
            int bestLag = 0;
            for (int lag = -maxLag; lag <= maxLag; lag++) {
                double num = 0, denA = 0, denB = 0;
                for (int n = 0; n < mb.Length; n++) {
                    int j = n - lag;
                    if (j < 0 || j >= ma.Length) {
                        continue;
                    }
                    num += ma[j] * mb[n];
                    denA += ma[j] * ma[j];
                    denB += mb[n] * mb[n];
                }
                double corr = (denA <= 0 || denB <= 0) ? 0 : num / Math.Sqrt(denA * denB);
                if (corr > best) {
                    best = corr;
                    bestLag = lag;
                }
            }
            return bestLag * 1000.0 / sampleRate;
        }

        /// <summary>左右声道 Pearson 相关系数。</summary>
        public static double Correlation(float[] interleaved, int channels = 2) {
            if (channels < 2) {
                return 1;
            }
            int frames = interleaved.Length / channels;
            double sl = 0, sr = 0, slr = 0;
            for (int i = 0; i < frames; i++) {
                double l = interleaved[i * channels];
                double r = interleaved[i * channels + 1];
                sl += l * l;
                sr += r * r;
                slr += l * r;
            }
            double den = Math.Sqrt(sl * sr);
            return den <= 1e-18 ? 0 : slr / den;
        }
    }
}

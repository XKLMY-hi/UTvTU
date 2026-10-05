using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpenUtau.App.ViewModels;
using ReactiveUI;

namespace OpenUtau.App.Controls {
    /// <summary>
    /// 打开片段的已渲染音频，作为 min/max 包络画在音符下方，**每个设备像素一列**。
    ///
    /// 本实现按我们的数据结构重写了上游 `ffcf2748`（Keep the piano roll waveform steady
    /// and in step while scrolling）与 `f05244c0`（Clip the piano roll waveform to its
    /// bounds）的算法，**不引入**上游的 Core 投影 seam（`RenderView` / `MixPlanner` /
    /// `SampleSlot` —— 那是 W14 的领域），仍走我们既有的 `part.Mix.Mix(...)` 取样。
    ///
    /// 三条关键设计（都是"滚动时不抖、不重建"的必要条件）：
    ///   1. **列固定在同一时间网格上**：第 k 列覆盖 ticks
    ///      `[TickOrigin + k / p, TickOrigin + (k+1) / p]`（p = 每 tick 的设备像素数）。
    ///      按时间网格分桶 ⇒ 滚动不足一像素时不会重新分桶，峰值不会闪。
    ///   2. **缓存范围比视口宽**（左右各半屏余量），滚动只平移不重建；只有缩放、
    ///      新渲染音频、片段/原点/高度变化、或视口越出缓存范围才重建。
    ///   3. **按整设备像素平移**（`Math.Round`），保持与位图同级的锐利度；绘制前
    ///      `PushClip(Bounds)`，缓存余量不会画到控件之外（f05244c0 的意图）。
    ///
    /// 相对上游的两处**刻意差异**（已记录在 W15 报告）：
    ///   · 上游只画"已渲染短语覆盖到的列"（靠 Core 投影判断），我们拿不到该信息 ⇒
    ///     除 `t &lt; 0`（片段起点之前）留白外其余列都画（静音段表现为中线，与原实现一致）；
    ///   · 上游用 `SlotMixSource` 逐短语取样，我们用 `part.Mix.Mix` 整段取样。
    /// </summary>
    class WaveformImage : Control {
        public static readonly DirectProperty<WaveformImage, double> TickWidthProperty =
            AvaloniaProperty.RegisterDirect<WaveformImage, double>(
                nameof(TickWidth),
                o => o.TickWidth,
                (o, v) => o.TickWidth = v);
        public static readonly DirectProperty<WaveformImage, double> TickOffsetProperty =
            AvaloniaProperty.RegisterDirect<WaveformImage, double>(
                nameof(TickOffset),
                o => o.TickOffset,
                (o, v) => o.TickOffset = v);
        public static readonly DirectProperty<WaveformImage, bool> ShowWaveformProperty =
            AvaloniaProperty.RegisterDirect<WaveformImage, bool>(
                nameof(ShowWaveform),
                o => o.ShowWaveform,
                (o, v) => o.ShowWaveform = v);

        public double TickWidth {
            get => tickWidth;
            set => SetAndRaise(TickWidthProperty, ref tickWidth, value);
        }
        public double TickOffset {
            get { return tickOffset; }
            set { SetAndRaise(TickOffsetProperty, ref tickOffset, value); }
        }
        public bool ShowWaveform {
            get { return showWaveform; }
            set { SetAndRaise(ShowWaveformProperty, ref showWaveform, value); }
        }

        internal const int SampleRate = 44100;
        internal const int Channels = 2;
        static readonly IBrush Fill = new SolidColorBrush(Color.FromArgb(0x7F, 0x7F, 0x7F, 0x7F));

        private double tickWidth;
        private double tickOffset;
        private bool showWaveform;

        // 缓存包络：覆盖列 [cacheStart, cacheEnd)，按这些输入构建；坐标为设备像素，
        // 局部 x = 0 对应列 cacheStart。
        private StreamGeometry? geometry;
        private int cacheStart;
        private int cacheEnd;
        private object? cachePart;
        private double cacheTickOrigin;
        private double cacheTickWidth;
        private double cacheHeight;
        private double cacheScale;
        private bool cacheValid;
        private float[] sampleData = new float[0];

        /// <summary>测试/诊断用：包络实际重建次数（性能证据靠计数，不靠像素）。</summary>
        internal int BuildCount { get; private set; }

        public WaveformImage() {
            MessageBus.Current.Listen<WaveformRefreshEvent>()
                .Subscribe(e => {
                    // 新渲染出的音频 ⇒ 缓存作废
                    cacheValid = false;
                    InvalidateVisual();
                });
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == DataContextProperty ||
                change.Property == TickWidthProperty ||
                change.Property == TickOffsetProperty ||
                change.Property == ShowWaveformProperty ||
                change.Property == BoundsProperty) {
                InvalidateVisual();
            }
        }

        public override void Render(DrawingContext context) {
            base.Render(context);
            if (DataContext is not NotesViewModel viewModel || double.IsNaN(viewModel.TickOffset) ||
                !ShowWaveform || viewModel.TickWidth <= ViewConstants.PianoRollTickWidthShowDetails) {
                return;
            }
            var project = viewModel.Project;
            var part = viewModel.Part;
            double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            // 以下全部以设备像素为单位
            int width = (int)Math.Ceiling(Bounds.Width * scale);
            double height = Math.Round(Bounds.Height * scale);
            if (project == null || part == null || width <= 0 || height <= 0) {
                return;
            }
            double offsetPx = viewModel.TickOffset * viewModel.TickWidth * scale;
            int firstColumn = (int)Math.Floor(offsetPx);
            if (viewModel.TickWidth != cacheTickWidth || scale != cacheScale) {
                // 缩放期间每帧都会失效：只建可见部分，之后的第一次滚动再补出余量
                Build(viewModel, project, part, scale, firstColumn, firstColumn + width + 1, height);
            } else if (!cacheValid || geometry == null || !ReferenceEquals(part, cachePart) ||
                viewModel.TickOrigin != cacheTickOrigin || height != cacheHeight ||
                firstColumn < cacheStart || firstColumn + width + 1 > cacheEnd) {
                // 左右各留半屏余量 ⇒ 常态滚动几乎不重建
                Build(viewModel, project, part, scale, firstColumn - width / 2, firstColumn + width + width / 2 + 1, height);
            }
            if (geometry != null) {
                // 先按整设备像素平移，再把设备像素换算回控件单位（DIP）
                var transform = Matrix.CreateTranslation(Math.Round(cacheStart - offsetPx), 0) *
                                Matrix.CreateScale(1 / scale, 1 / scale);
                using (context.PushClip(new Rect(Bounds.Size)))          // f05244c0：缓存余量不越界
                using (context.PushTransform(transform)) {
                    context.DrawGeometry(Fill, null, geometry);
                }
            }
        }

        private void Build(NotesViewModel viewModel, object project, object part, double scale,
            int start, int end, double height) {
            cachePart = part;
            cacheTickOrigin = viewModel.TickOrigin;
            cacheTickWidth = viewModel.TickWidth;
            cacheScale = scale;
            cacheHeight = height;
            cacheStart = start;
            cacheEnd = end;
            cacheValid = true;
            geometry = null;
            BuildCount++;

            int columns = end - start;
            if (columns <= 0) {
                return;
            }
            // 每列左边缘的歌曲时间；edges[columns] = 最后一列的右边缘
            double pixelsPerTick = viewModel.TickWidth * scale;
            var edges = new double[columns + 1];
            for (int i = 0; i <= columns; ++i) {
                edges[i] = viewModel.Project.timeAxis.TickPosToMsPos(viewModel.TickOrigin + (start + i) / pixelsPerTick);
            }
            int firstSample = Math.Max(0, SampleIndex(edges[0]));
            int sampleCount = Math.Max(0, SampleIndex(edges[columns]) - firstSample);
            if (sampleCount == 0) {
                return;
            }
            if (sampleData.Length < sampleCount) {
                sampleData = new float[sampleCount];
            }
            Array.Clear(sampleData, 0, sampleCount);
            var mix = viewModel.Part?.Mix;
            if (mix == null) {
                return;
            }
            mix.Mix(firstSample, sampleData, 0, sampleCount);

            // 每列的上下沿（设备像素）；t < 0（片段起点之前）留白（NaN）
            var top = new double[columns];
            var bottom = new double[columns];
            float lastValue = 0;
            for (int i = 0; i < columns; ++i) {
                double fromMs = edges[i], toMs = edges[i + 1];
                int s0 = Math.Clamp(SampleIndex(fromMs) - firstSample, 0, sampleCount);
                int s1 = Math.Clamp(SampleIndex(toMs) - firstSample, 0, sampleCount);
                if (fromMs < 0) {
                    top[i] = bottom[i] = double.NaN;
                    if (s1 > 0) {
                        lastValue = sampleData[s1 - 1];
                    }
                    continue;
                }
                float min, max;
                if (s1 > s0) {
                    min = float.MaxValue;
                    max = float.MinValue;
                    for (int s = s0; s < s1; ++s) {
                        float v = sampleData[s];
                        if (v < min) min = v;
                        if (v > max) max = v;
                    }
                    lastValue = sampleData[s1 - 1];
                } else {
                    // 放大到一列不足一个采样：保持上一个采样值
                    min = max = lastValue;
                }
                // 取整到整像素行、至少一行 ⇒ 边缘锐利，且极安静处仍有一条线
                double yTop = Math.Clamp(Math.Round(NormalizePeak(max) * height), 0, height - 1);
                double yBottom = Math.Clamp(Math.Round(NormalizePeak(min) * height), yTop + 1, height);
                top[i] = yTop;
                bottom[i] = yBottom;
            }

            // 每段连续覆盖列合成一个闭合图形：先沿上沿去、再沿下沿回；每列占 [i, i+1)
            var g = new StreamGeometry();
            using (var ctx = g.Open()) {
                int i = 0;
                while (i < columns) {
                    if (double.IsNaN(top[i])) {
                        ++i;
                        continue;
                    }
                    int runStart = i;
                    while (i < columns && !double.IsNaN(top[i])) {
                        ++i;
                    }
                    ctx.BeginFigure(new Point(runStart, top[runStart]), true);
                    for (int k = runStart; k < i; ++k) {
                        ctx.LineTo(new Point(k, top[k]));
                        ctx.LineTo(new Point(k + 1, top[k]));
                    }
                    for (int k = i - 1; k >= runStart; --k) {
                        ctx.LineTo(new Point(k + 1, bottom[k]));
                        ctx.LineTo(new Point(k, bottom[k]));
                    }
                    ctx.EndFigure(true);
                }
            }
            geometry = g;
        }

        /// <summary>
        /// 峰值 → 归一化纵向位置（0 = 图顶，1 = 图底；调用方再乘绘制高）。
        ///
        /// 屏幕 y 轴向下、音频采样 + 向上 ⇒ 这里必须是**减号**：+1 的采样值画在上半部。
        /// 我们与上游 merge-base 同源，原先两边都写成了 <c>0.5f + s * 0.5f</c>，
        /// 表现为整条波形上下镜像（上游 `1437d5e2` 修的正是这个符号）。
        /// </summary>
        internal static float NormalizePeak(float sample) => 0.5f - sample * 0.5f;

        /// <summary>交错采样下标：与 mix 的排布一致（每毫秒 SampleRate 个采样 × Channels 交错）。</summary>
        internal static int SampleIndex(double ms) => (int)(ms * SampleRate / 1000) * Channels;
    }
}

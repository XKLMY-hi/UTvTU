using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.SignalChain.Effects;
using OpenUtau.Core.Theming;
using OpenUtau.Theming;

namespace OpenUtau.App.Controls {
    /// <summary>
    /// 机架曲线屏基类（MD3 重绘版，2026-10）。
    ///
    /// 曲线**由 DSP 自身的数学绘制**（<see cref="BiquadEQ.ResponseDb"/>、
    /// <see cref="SimpleCompressor.CurveGainDb"/>、<see cref="Freeverb.DecaySeconds"/>），
    /// 所以屏上看到的就是耳朵听到的。DSP 按 <see cref="MixFxSource.SampleRate"/> /
    /// <see cref="MixFxSource.Channels"/> 构造（跟随全局音频格式设置），采样率变化时重建。
    ///
    /// MD3 语言：实色底 + 网格 + 强调色曲线与浅填充 + 圆点标记；**无玻璃高光、无渐变蒙层**。
    /// 画刷属性为 null 时回退 MD3 颜色池，因此本文件不含任何字面量色值。
    /// 模块电源关闭（<see cref="Avalonia.Input.InputElement.IsEnabled"/> = false）时整屏降透明度。
    /// </summary>
    public abstract class FxDisplay : Control {
        public static readonly StyledProperty<IBrush?> BackgroundProperty =
            AvaloniaProperty.Register<FxDisplay, IBrush?>(nameof(Background));
        public static readonly StyledProperty<IBrush?> CurveBrushProperty =
            AvaloniaProperty.Register<FxDisplay, IBrush?>(nameof(CurveBrush));
        public static readonly StyledProperty<IBrush?> GridBrushProperty =
            AvaloniaProperty.Register<FxDisplay, IBrush?>(nameof(GridBrush));
        public static readonly StyledProperty<IBrush?> LabelBrushProperty =
            AvaloniaProperty.Register<FxDisplay, IBrush?>(nameof(LabelBrush));

        public IBrush? Background {
            get => GetValue(BackgroundProperty);
            set => SetValue(BackgroundProperty, value);
        }
        /// <summary>曲线 / 数值文字（模块强调色）。</summary>
        public IBrush? CurveBrush {
            get => GetValue(CurveBrushProperty);
            set => SetValue(CurveBrushProperty, value);
        }
        /// <summary>网格与刻度线。</summary>
        public IBrush? GridBrush {
            get => GetValue(GridBrushProperty);
            set => SetValue(GridBrushProperty, value);
        }
        /// <summary>刻度文字。</summary>
        public IBrush? LabelBrush {
            get => GetValue(LabelBrushProperty);
            set => SetValue(LabelBrushProperty, value);
        }

        const double LabelSize = 9;
        // 模块旁通时的降透明度。
        const double BypassedOpacity = 0.45;

        static FxDisplay() {
            AffectsRender<FxDisplay>(BackgroundProperty, CurveBrushProperty,
                GridBrushProperty, LabelBrushProperty, IsEnabledProperty);
        }

        protected FxDisplay() {
            ClipToBounds = true;
        }

        /// <summary>绘图区内边距（左,上,右,下）——曲线/网格/刻度都在这个矩形里。</summary>
        protected static readonly Thickness PlotPadding = new Thickness(8, 8, 8, 6);

        /// <summary>当前尺寸下的绘图区；<see cref="RenderPlot"/> 收到的就是它。</summary>
        public Rect PlotRect => new Rect(Bounds.Size).Deflate(PlotPadding);

        public sealed override void Render(DrawingContext context) {
            var rect = new Rect(Bounds.Size);
            context.DrawRectangle(Brush(Background, Md3Role.SurfaceContainerLow), null, rect);
            Rect plot = PlotRect;
            if (rect.Width < 16 || rect.Height < 16 || plot.Width < 4 || plot.Height < 4) {
                return;
            }
            if (!IsEnabled) {
                using (context.PushOpacity(BypassedOpacity)) {
                    RenderPlot(context, plot);
                }
                return;
            }
            RenderPlot(context, plot);
        }

        /// <summary>绘制曲线本体；<paramref name="rect"/> 已去掉内边距。</summary>
        protected abstract void RenderPlot(DrawingContext context, Rect rect);

        /// <summary>属性为 null 时回退颜色池（保证任何用法下都取到 MD3 角色色）。</summary>
        protected static IBrush Brush(IBrush? brush, Md3Role role) =>
            brush ?? ColorPool.Current.Brush(role);

        protected IBrush Grid() => Brush(GridBrush, Md3Role.OutlineVariant);
        protected IBrush Curve() => Brush(CurveBrush, Md3Role.Primary);

        protected Pen GridPen(double opacity = 0.5, double dash = 0) {
            var pen = new Pen(WithOpacity(Grid(), opacity), 1);
            if (dash > 0) {
                pen.DashStyle = new DashStyle(new[] { dash, dash }, 0);
            }
            return pen;
        }

        protected Pen CurvePen(double thickness = 2) => new Pen(Curve(), thickness) {
            LineJoin = PenLineJoin.Round,
            LineCap = PenLineCap.Round,
        };

        protected IBrush? CurveFill(double opacity = 0.22) => WithOpacity(Curve(), opacity);

        protected static IBrush? WithOpacity(IBrush? brush, double opacity) =>
            brush is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, solid.Opacity * opacity) : brush;

        /// <summary>曲线上的参数标记点：底色环 + 曲线色实心点。</summary>
        protected void DrawMarker(DrawingContext context, Point p) {
            context.DrawEllipse(Brush(Background, Md3Role.SurfaceContainerLow),
                new Pen(Curve(), 1.5), p, 3.5, 3.5);
        }

        protected static StreamGeometry Polyline(int count, Func<int, Point> point, double? closeToY = null) {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open()) {
                var first = point(0);
                if (closeToY is double y0) {
                    ctx.BeginFigure(new Point(first.X, y0), true);
                    ctx.LineTo(first);
                } else {
                    ctx.BeginFigure(first, false);
                }
                Point last = first;
                for (int i = 1; i < count; i++) {
                    last = point(i);
                    ctx.LineTo(last);
                }
                if (closeToY is double y1) {
                    ctx.LineTo(new Point(last.X, y1));
                }
                ctx.EndFigure(closeToY != null);
            }
            return geometry;
        }

        /// <summary>画一个比例尺文字；<paramref name="anchor"/> 是左上 / 上中 / 右上锚点。</summary>
        protected void DrawLabel(DrawingContext context, string text, Point anchor,
            TextAlignment align = TextAlignment.Left, IBrush? brush = null) {
            using var layout = new TextLayout(text, new Typeface(LabelFontFamily()), LabelSize,
                brush ?? Brush(LabelBrush, Md3Role.OnSurfaceVariant), TextAlignment.Left, TextWrapping.NoWrap);
            double x = align switch {
                TextAlignment.Center => anchor.X - layout.Width / 2,
                TextAlignment.Right => anchor.X - layout.Width,
                _ => anchor.X,
            };
            layout.Draw(context, new Point(x, anchor.Y));
        }

        // 跟随应用字体（全局样式设 PlusFontFamily，TextElement 附加属性可继承）。
        FontFamily LabelFontFamily() {
            var family = TextElement.GetFontFamily(this);
            return family ?? FontFamily.Default;
        }
    }

    /// <summary>EQ 幅频响应：20 Hz – 20 kHz 对数轴，±15 dB，三个频段各一个标记点。</summary>
    public class EqCurveDisplay : FxDisplay {
        public static readonly StyledProperty<double> LowDbProperty =
            AvaloniaProperty.Register<EqCurveDisplay, double>(nameof(LowDb));
        public static readonly StyledProperty<double> MidFreqProperty =
            AvaloniaProperty.Register<EqCurveDisplay, double>(nameof(MidFreq), 1000);
        public static readonly StyledProperty<double> MidDbProperty =
            AvaloniaProperty.Register<EqCurveDisplay, double>(nameof(MidDb));
        public static readonly StyledProperty<double> HighDbProperty =
            AvaloniaProperty.Register<EqCurveDisplay, double>(nameof(HighDb));

        public double LowDb { get => GetValue(LowDbProperty); set => SetValue(LowDbProperty, value); }
        public double MidFreq { get => GetValue(MidFreqProperty); set => SetValue(MidFreqProperty, value); }
        public double MidDb { get => GetValue(MidDbProperty); set => SetValue(MidDbProperty, value); }
        public double HighDb { get => GetValue(HighDbProperty); set => SetValue(HighDbProperty, value); }

        const double MinFreq = 20, MaxFreq = 20000, RangeDb = 15;
        // 搁架拐点，与 BiquadEQ 内部配置一致。
        const double LowShelfHz = 200, HighShelfHz = 8000;

        BiquadEQ eq = new BiquadEQ(MixFxSource.SampleRate, MixFxSource.Channels);
        int eqSampleRate = MixFxSource.SampleRate;
        int eqChannels = MixFxSource.Channels;

        static EqCurveDisplay() {
            AffectsRender<EqCurveDisplay>(LowDbProperty, MidFreqProperty, MidDbProperty, HighDbProperty);
        }

        /// <summary>音频格式变化时按新采样率重建 DSP（否则曲线与实听不符）。</summary>
        void SyncFormat() {
            if (eqSampleRate == MixFxSource.SampleRate && eqChannels == MixFxSource.Channels) {
                return;
            }
            eqSampleRate = MixFxSource.SampleRate;
            eqChannels = MixFxSource.Channels;
            eq = new BiquadEQ(eqSampleRate, eqChannels);
        }

        /// <summary>频率 → 绘图区横坐标（20 Hz–20 kHz 对数轴）。</summary>
        public static double FrequencyToX(double freq, Rect plot) =>
            plot.Left + Math.Log(freq / MinFreq) / Math.Log(MaxFreq / MinFreq) * plot.Width;

        /// <summary>增益 → 绘图区纵坐标（±15 dB 线性映射，0 dB 落在垂直中心）。</summary>
        public static double LevelToY(double db, Rect plot) =>
            plot.Center.Y - Math.Clamp(db, -RangeDb, RangeDb) / RangeDb * plot.Height / 2;

        /// <summary>
        /// 当前参数下的幅频响应折线（绘图区坐标，对数均匀采样）。
        /// <see cref="RenderPlot"/> 画的就是这条折线 —— 断言它等于断言屏上的曲线。
        /// </summary>
        public Point[] BuildCurvePoints(Rect plot, int count) {
            SyncFormat();
            eq.Configure(LowDb, MidFreq, MixFxSource.EqMidQ, MidDb, HighDb);
            int n = Math.Max(2, count);
            var points = new Point[n];
            for (int i = 0; i < n; i++) {
                double f = MinFreq * Math.Pow(MaxFreq / MinFreq, (double)i / (n - 1));
                points[i] = new Point(FrequencyToX(f, plot), LevelToY(eq.ResponseDb(f), plot));
            }
            return points;
        }

        protected override void RenderPlot(DrawingContext context, Rect rect) {
            var minor = GridPen(0.35);
            foreach (var f in new[] { 50.0, 200, 500, 2000, 5000 }) {
                context.DrawLine(minor, new Point(FrequencyToX(f, rect), rect.Top),
                    new Point(FrequencyToX(f, rect), rect.Bottom));
            }
            foreach (var db in new[] { -12.0, -6, 6, 12 }) {
                context.DrawLine(minor, new Point(rect.Left, LevelToY(db, rect)),
                    new Point(rect.Right, LevelToY(db, rect)));
            }
            var major = GridPen(0.7);
            foreach (var (f, label) in new[] { (100.0, "100"), (1000.0, "1k"), (10000.0, "10k") }) {
                double x = FrequencyToX(f, rect);
                context.DrawLine(major, new Point(x, rect.Top), new Point(x, rect.Bottom));
                DrawLabel(context, label, new Point(x + 2, rect.Bottom - 11));
            }
            double zeroY = LevelToY(0, rect);
            context.DrawLine(major, new Point(rect.Left, zeroY), new Point(rect.Right, zeroY));
            DrawLabel(context, "+12", new Point(rect.Left, LevelToY(12, rect) - 5));
            DrawLabel(context, "-12", new Point(rect.Left, LevelToY(-12, rect) - 5));

            var points = BuildCurvePoints(rect, Math.Max(2, (int)(rect.Width / 2)));
            Point At(int i) => points[i];
            context.DrawGeometry(CurveFill(), null, Polyline(points.Length, At, zeroY));
            context.DrawGeometry(null, CurvePen(), Polyline(points.Length, At));
            foreach (var f in new[] { LowShelfHz, MidFreq, HighShelfHz }) {
                DrawMarker(context, new Point(FrequencyToX(f, rect), LevelToY(ResponseAt(f), rect)));
            }
        }

        /// <summary>单个频点的响应（dB）——标记点用。</summary>
        double ResponseAt(double freq) {
            SyncFormat();
            eq.Configure(LowDb, MidFreq, MixFxSource.EqMidQ, MidDb, HighDb);
            return eq.ResponseDb(freq);
        }
    }

    /// <summary>压缩器传输曲线：输入 −60…0 dB → 输出 −60…+6 dB（含补偿增益），标出阈值。</summary>
    public class CompCurveDisplay : FxDisplay {
        public static readonly StyledProperty<double> ThresholdDbProperty =
            AvaloniaProperty.Register<CompCurveDisplay, double>(nameof(ThresholdDb));
        public static readonly StyledProperty<double> RatioProperty =
            AvaloniaProperty.Register<CompCurveDisplay, double>(nameof(Ratio), 1);
        public static readonly StyledProperty<double> MakeupDbProperty =
            AvaloniaProperty.Register<CompCurveDisplay, double>(nameof(MakeupDb));

        public double ThresholdDb { get => GetValue(ThresholdDbProperty); set => SetValue(ThresholdDbProperty, value); }
        public double Ratio { get => GetValue(RatioProperty); set => SetValue(RatioProperty, value); }
        public double MakeupDb { get => GetValue(MakeupDbProperty); set => SetValue(MakeupDbProperty, value); }

        const double InMin = -60, InMax = 0, OutMin = -60, OutMax = 6;

        static CompCurveDisplay() {
            AffectsRender<CompCurveDisplay>(ThresholdDbProperty, RatioProperty, MakeupDbProperty);
        }

        /// <summary>输入电平 → 绘图区横坐标（−60…0 dB 线性轴）。</summary>
        public static double InputToX(double db, Rect plot) =>
            plot.Left + (db - InMin) / (InMax - InMin) * plot.Width;

        /// <summary>输出电平 → 绘图区纵坐标（−60…+6 dB 线性轴）。</summary>
        public static double OutputToY(double db, Rect plot) =>
            plot.Bottom - (Math.Clamp(db, OutMin, OutMax) - OutMin) / (OutMax - OutMin) * plot.Height;

        /// <summary>静态传输特性：输出 dB = 输入 dB + 压缩增益 + 补偿增益。</summary>
        public static double TransferDb(double inputDb, double thresholdDb, double ratio, double makeupDb) =>
            inputDb + SimpleCompressor.CurveGainDb(inputDb, thresholdDb, ratio) + makeupDb;

        /// <summary>
        /// 当前参数下的传输曲线折线（绘图区坐标）。<see cref="RenderPlot"/> 画的就是它。
        /// </summary>
        public Point[] BuildCurvePoints(Rect plot, int count) {
            int n = Math.Max(2, count);
            var points = new Point[n];
            for (int i = 0; i < n; i++) {
                double input = InMin + (InMax - InMin) * i / (n - 1);
                points[i] = new Point(InputToX(input, plot),
                    OutputToY(TransferDb(input, ThresholdDb, Ratio, MakeupDb), plot));
            }
            return points;
        }

        protected override void RenderPlot(DrawingContext context, Rect rect) {
            var minor = GridPen(0.35);
            foreach (var db in new[] { -48.0, -36, -24, -12 }) {
                context.DrawLine(minor, new Point(InputToX(db, rect), rect.Top),
                    new Point(InputToX(db, rect), rect.Bottom));
                context.DrawLine(minor, new Point(rect.Left, OutputToY(db, rect)),
                    new Point(rect.Right, OutputToY(db, rect)));
                DrawLabel(context, db.ToString("0"), new Point(InputToX(db, rect), rect.Bottom - 11), TextAlignment.Center);
            }
            // 1:1 参考线 + 阈值线
            context.DrawLine(GridPen(0.7, 3), new Point(InputToX(InMin, rect), OutputToY(InMin, rect)),
                new Point(InputToX(InMax, rect), OutputToY(InMax, rect)));
            context.DrawLine(GridPen(1.0, 2), new Point(InputToX(ThresholdDb, rect), rect.Top),
                new Point(InputToX(ThresholdDb, rect), rect.Bottom));

            var points = BuildCurvePoints(rect, Math.Max(2, (int)(rect.Width / 2)));
            Point At(int i) => points[i];
            context.DrawGeometry(CurveFill(0.12), null, Polyline(points.Length, At, rect.Bottom));
            context.DrawGeometry(null, CurvePen(), Polyline(points.Length, At));
            DrawMarker(context, new Point(InputToX(ThresholdDb, rect),
                OutputToY(TransferDb(ThresholdDb, ThresholdDb, Ratio, MakeupDb), rect)));
            DrawLabel(context, $"{Ratio:0.0}:1", new Point(rect.Right, rect.Top), TextAlignment.Right, Curve());
        }
    }

    /// <summary>
    /// 混响衰减：0–4 s 的 dB 纵轴（这样衰减就是一条斜线）——全频段尾音，以及阻尼削掉的
    /// 高频尾音（更短），起点是预延迟之后的有效湿声电平。
    /// </summary>
    public class ReverbCurveDisplay : FxDisplay {
        public static readonly StyledProperty<double> RoomSizeProperty =
            AvaloniaProperty.Register<ReverbCurveDisplay, double>(nameof(RoomSize));
        public static readonly StyledProperty<double> DampProperty =
            AvaloniaProperty.Register<ReverbCurveDisplay, double>(nameof(Damp));
        public static readonly StyledProperty<double> WetProperty =
            AvaloniaProperty.Register<ReverbCurveDisplay, double>(nameof(Wet), 1);
        public static readonly StyledProperty<double> PreDelayMsProperty =
            AvaloniaProperty.Register<ReverbCurveDisplay, double>(nameof(PreDelayMs));
        public static readonly StyledProperty<string?> PresetProperty =
            AvaloniaProperty.Register<ReverbCurveDisplay, string?>(nameof(Preset));
        /// <summary>高频尾音线（阻尼后的剩余高频）。</summary>
        public static readonly StyledProperty<IBrush?> HighBrushProperty =
            AvaloniaProperty.Register<ReverbCurveDisplay, IBrush?>(nameof(HighBrush));
        /// <summary>无湿声时屏上显示的"干声"字样（走本地化键）。</summary>
        public static readonly StyledProperty<string> DryTextProperty =
            AvaloniaProperty.Register<ReverbCurveDisplay, string>(nameof(DryText), "DRY");

        public double RoomSize { get => GetValue(RoomSizeProperty); set => SetValue(RoomSizeProperty, value); }
        public double Damp { get => GetValue(DampProperty); set => SetValue(DampProperty, value); }
        public double Wet { get => GetValue(WetProperty); set => SetValue(WetProperty, value); }
        public double PreDelayMs { get => GetValue(PreDelayMsProperty); set => SetValue(PreDelayMsProperty, value); }
        public string? Preset { get => GetValue(PresetProperty); set => SetValue(PresetProperty, value); }
        public IBrush? HighBrush { get => GetValue(HighBrushProperty); set => SetValue(HighBrushProperty, value); }
        public string DryText { get => GetValue(DryTextProperty); set => SetValue(DryTextProperty, value); }

        const double Seconds = 4;
        const double FloorDb = -48;
        // 有效湿声增益 0.5 画在 0 dB（顶端）。
        const double FullScaleWet = 0.5;

        static ReverbCurveDisplay() {
            AffectsRender<ReverbCurveDisplay>(RoomSizeProperty, DampProperty, WetProperty,
                PreDelayMsProperty, PresetProperty, HighBrushProperty, DryTextProperty);
        }

        /// <summary>
        /// 有效湿声增益 = 预设 Wet × 用户 Wet（与 <c>MixFxSource</c> 里
        /// <c>reverb.Configure(..., rParams.Wet * userWet, ...)</c> 同源）。
        /// ≤1e-4 即"干声"：不画衰减包络，屏上只显示 DryText。
        /// </summary>
        public double EffectiveWet() {
            double presetWet = FxPresets.Reverb.TryGetValue(Preset ?? FxPresets.Off, out var rp) ? rp.Wet : 0;
            return presetWet * Math.Clamp(Wet, 0, 2) / FullScaleWet;
        }

        /// <summary>全频段 / 高频段 RT60（秒），由混响 DSP 的反馈数学给出。</summary>
        public (double Low, double High) Decay() => Freeverb.DecaySeconds(RoomSize, Damp);

        /// <summary>
        /// 衰减包络折线（绘图区坐标）：预延迟前贴在底噪线，之后按 RT60 每倍程掉 60 dB。
        /// <see cref="RenderPlot"/> 画的就是它。
        /// </summary>
        public Point[] BuildEnvelope(double rt60, Rect plot, int count) {
            double wet = EffectiveWet();
            double levelDb = wet > 1e-4 ? 20 * Math.Log10(wet) : FloorDb;
            double preDelay = PreDelayMs / 1000;
            double top = plot.Top + 12;
            int n = Math.Max(2, count);
            var points = new Point[n];
            for (int i = 0; i < n; i++) {
                double t = Seconds * i / (n - 1);
                double db = t < preDelay ? FloorDb : levelDb - 60 * (t - preDelay) / rt60;
                points[i] = new Point(plot.Left + t / Seconds * plot.Width,
                    top + Math.Clamp(db / FloorDb, 0, 1) * (plot.Bottom - top));
            }
            return points;
        }

        protected override void RenderPlot(DrawingContext context, Rect rect) {
            double wet = EffectiveWet();
            var (low, high) = Decay();
            double top = rect.Top + 12;

            double X(double t) => rect.Left + t / Seconds * rect.Width;
            double Y(double db) => top + Math.Clamp(db / FloorDb, 0, 1) * (rect.Bottom - top);
            var minor = GridPen(0.35);
            for (double t = 0.5; t < Seconds; t += 0.5) {
                context.DrawLine(minor, new Point(X(t), rect.Top), new Point(X(t), rect.Bottom));
                if (t % 1 == 0) {
                    DrawLabel(context, $"{t:0}s", new Point(X(t) + 2, rect.Bottom - 11));
                }
            }
            foreach (var db in new[] { -12.0, -24, -36 }) {
                context.DrawLine(minor, new Point(rect.Left, Y(db)), new Point(rect.Right, Y(db)));
            }

            if (wet > 1e-4) {
                var lowPoints = BuildEnvelope(low, rect, Math.Max(2, (int)(rect.Width / 2)));
                var highPoints = BuildEnvelope(high, rect, Math.Max(2, (int)(rect.Width / 2)));
                context.DrawGeometry(CurveFill(0.35), new Pen(Curve(), 1.5), Polyline(lowPoints.Length, i => lowPoints[i], rect.Bottom));
                context.DrawGeometry(WithOpacity(Brush(HighBrush, Md3Role.Tertiary), 0.55), null,
                    Polyline(highPoints.Length, i => highPoints[i], rect.Bottom));
                DrawMarker(context, new Point(X(PreDelayMs / 1000), Y(20 * Math.Log10(wet))));
            }
            DrawLabel(context, wet > 1e-4 ? $"RT60 {low:0.0} s" : DryText,
                new Point(rect.Right, rect.Top), TextAlignment.Right, Curve());
        }
    }
}

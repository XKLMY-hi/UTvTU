using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.SignalChain.Effects;
using OpenUtau.Core.Theming;
using OpenUtau.Theming;

namespace OpenUtau.App.Controls {
    /// <summary>
    /// 混音台规格常数（**唯一事实来源**）：数值逐条对应设计交付包
    /// `.opencode/design/spec/Mixer.txt`，注释里的行号就是设计稿行号。
    ///
    /// XAML 中的同名字面量（Width / Height / FontSize / Canvas.Left / Canvas.Top）由
    /// `OpenUtau.Test/App/MixerGeometryTests.cs` 逐条与这里比对——两边任何一处漂移都会
    /// 让契约测试变红，所以"规格抄两遍"是受约束的，不是自由发挥。
    /// </summary>
    public static class MixerMetrics {
        // ── Mixer Area（Mixer.txt:42）──
        /// <summary>区域四周内边距 16。</summary>
        public const double AreaPadding = 16;
        /// <summary>通道条之间 gap 8。</summary>
        public const double AreaGap = 8;
        /// <summary>
        /// 右侧效果链宿主宽度（规划 §1.1 冻结接口；设计稿里没有这个元素——13 条通道条
        /// 恰好填满 1440，链面板注定引入横向滚动）。
        /// </summary>
        public const double FxChainHostWidth = 280;
        /// <summary>链宿主左侧分隔线宽度 1px（outline-variant）。</summary>
        public const double FxChainDividerWidth = 1;

        // ── 通道条（Mixer.txt:43-96）──
        public const double StripWidth = 96;            // 43
        public const double StripPadding = 8;           // 43
        public const double StripCornerRadius = 12;     // 43
        public const double StripGap = 6;               // 43
        public const double AccentHeight = 3;           // 44
        public const double NameFontSize = 11;          // 45
        public const double NameLineHeight = 12;        // 45
        public const double EqWidth = 80;               // 47
        public const double EqHeight = 76;              // 47
        public const double EqCornerRadius = 8;         // 47
        /// <summary>EQ 零线纵坐标（= 76/2，Mixer.txt:48）。</summary>
        public const double EqZeroLineY = 38;           // 48
        public const double PanRowHeight = 14;          // 65
        public const double PanLabelFontSize = 8;       // 66
        public const double PanValueFontSize = 9;       // 68
        /// <summary>
        /// 声像行命中区高度。视觉高度仍是 <see cref="PanRowHeight"/>=14（稿的值），
        /// 但 14px 的行拖不动（规划 §11 第 8 条）——命中区用负 margin 外扩到 22，
        /// 不占布局（行内 slot 仍是 14，行间距仍是 6）。
        /// </summary>
        public const double PanHitHeight = 22;
        /// <summary>声像拖拽满程像素（-100 → +100）。</summary>
        public const double PanDragRange = 240;
        public const double ButtonHeight = 20;          // 71/74
        public const double ButtonCornerRadius = 4;     // 71/74
        public const double ButtonFontSize = 10;        // 72/75
        public const double ButtonGap = 4;              // 70
        public const double FaderWidth = 80;            // 81
        public const double FaderHeight = 500;          // 81
        public const double MeterWidth = 8;             // 82
        public const double MeterLeft = 4;              // 82
        public const double MeterCornerRadius = 999;    // 82
        public const double FaderTrackWidth = 4;        // 84
        public const double FaderTrackLeft = 34;        // 84
        public const double FaderHandleWidth = 24;      // 85
        public const double FaderHandleHeight = 14;     // 85
        public const double FaderHandleLeft = 24;       // 85
        public const double FaderHandleCornerRadius = 4; // 85
        public const double TickLeft = 52;              // 86-94
        /// <summary>9 条刻度宽度：8/5 交替（1/3/5/7/9 号是 8）。</summary>
        public static readonly double[] TickWidths = { 8, 5, 8, 5, 8, 5, 8, 5, 8 };
        /// <summary>9 条刻度 top：0…498，间距 ≈62.25（不是 500/9 等分）。</summary>
        public static readonly double[] TickTops = { 0, 62, 125, 187, 249, 311, 374, 436, 498 };
        public const double DbFontSize = 9;             // 95

        // ── 主输出条（Mixer.txt:691-736）──
        public const double MasterWidth = 160;          // 691
        public const double MasterNameFontSize = 12;    // 693
        public const double MasterSubtitleFontSize = 8; // 695
        public const double BusInfoPadding = 10;        // 697
        public const double BusInfoGap = 8;             // 697
        public const double BusInfoCornerRadius = 8;    // 697
        public const double BusRowHeight = 14;          // 698/703/708/713
        public const double BusLabelFontSize = 8;       // 699…
        public const double BusValueFontSize = 9;       // 701…
        public const double MasterFaderWidth = 144;     // 719
        public const double MasterMeterWidth = 10;      // 720/722
        public const double MasterMeterLeftL = 24;      // 720
        public const double MasterMeterLeftR = 38;      // 722
        public const double MasterFaderTrackLeft = 74;  // 724
        public const double MasterHandleWidth = 24;     // 725
        public const double MasterHandleHeight = 16;    // 725（比轨道条高 2px）
        public const double MasterHandleLeft = 64;      // 725
        public const double MasterTickLeft = 96;        // 726-734
        public const double MasterPeakFontSize = 13;    // 735

        // ── 推子映射（沿用既有 -24..+12，与刻度无关：刻度只是装饰）──
        public const double FaderMinDb = -24;
        public const double FaderMaxDb = 12;

        /// <summary>dB → 手柄 Canvas.Top（0 = 顶部 = 最大音量；下方留出手柄自身高度）。</summary>
        public static double FaderTop(double db, double trackHeight, double handleHeight) {
            double span = Math.Max(1, trackHeight - handleHeight);
            double ratio = 1.0 - Math.Clamp(
                (Math.Clamp(db, FaderMinDb, FaderMaxDb) - FaderMinDb) / (FaderMaxDb - FaderMinDb), 0, 1);
            return ratio * span;
        }

        /// <summary>手柄 Canvas.Top → dB（<see cref="FaderTop"/> 的逆变换）。</summary>
        public static double FaderTopToDb(double top, double trackHeight, double handleHeight) {
            double span = Math.Max(1, trackHeight - handleHeight);
            double ratio = 1.0 - Math.Clamp(top / span, 0, 1);
            return FaderMinDb + ratio * (FaderMaxDb - FaderMinDb);
        }
    }

    /// <summary>
    /// 连续条电平表状态机。数据源与上一版完全一致（每轨最终输出 / 主输出的峰值 dB，
    /// 由 `LevelTracker` / `MasterAdapter` 提供，33ms <see cref="Avalonia.Threading.DispatcherTimer"/> 轮询），
    /// 只是把 10 段 LED 换成设计稿的 8px 圆角连续条（Mixer.txt:82-83）。
    ///
    /// 攻击瞬时、释放按 **dB 速率**回落（每 tick 2dB ≈ 60dB/s），另维护峰值保持
    /// （保持 1.5s 后以 ≈20dB/s 缓降）。全部是纯计算，因此 headless 下可断言——
    /// 本项目 headless 走 `UseHeadlessDrawing` 桩绘制，像素断言无意义。
    /// </summary>
    public sealed class MixerMeter {
        /// <summary>表底（-60dB 及以下视为静音）。</summary>
        public const double MinDb = -60;
        public const double MaxDb = 0;
        /// <summary>表值回落速度（dB / tick，33ms）。</summary>
        public const double FallPerTickDb = 2.0;
        /// <summary>峰值保持 tick 数（45 × 33ms ≈ 1.5s）。</summary>
        public const int PeakHoldTicks = 45;
        /// <summary>峰值保持回落速度（dB / tick）——比主表慢，便于读峰值。</summary>
        public const double PeakFallPerTickDb = 0.67;

        double levelDb = MinDb;
        double peakDb = MinDb;
        int holdTicks;

        /// <summary>当前表值（dB，已做释放衰减）。</summary>
        public double LevelDb => levelDb;
        /// <summary>峰值保持值（dB）。</summary>
        public double PeakDb => peakDb;
        /// <summary>表值比例 0..1（1 = 0 dBFS）。</summary>
        public double Fraction => DbToFraction(levelDb);
        /// <summary>峰值保持比例 0..1。</summary>
        public double PeakFraction => DbToFraction(peakDb);

        /// <summary>峰值 dB → 比例 0..1（-60dB 以下为 0）。</summary>
        public static double DbToFraction(double db) =>
            Math.Clamp((Math.Clamp(db, MinDb, MaxDb) - MinDb) / (MaxDb - MinDb), 0, 1);

        /// <summary>喂入一个轮询周期的峰值 dB，推进一个 tick。</summary>
        public void Push(double peakDbSample) {
            double target = Math.Clamp(peakDbSample, MinDb, MaxDb);
            levelDb = target >= levelDb ? target : Math.Max(target, levelDb - FallPerTickDb);
            if (levelDb >= peakDb) {
                peakDb = levelDb;
                holdTicks = PeakHoldTicks;
            } else if (holdTicks > 0) {
                holdTicks--;
            } else {
                peakDb = Math.Max(levelDb, peakDb - PeakFallPerTickDb);
            }
        }

        /// <summary>把当前表值写进连续条（填充自底部起长，高度 = 比例 × 轨道高）。</summary>
        public void Apply(Border fill, double trackHeight) {
            if (fill == null) {
                return;
            }
            fill.Height = Math.Round(Fraction * trackHeight, 3);
        }

        public void Reset() {
            levelDb = MinDb;
            peakDb = MinDb;
            holdTicks = 0;
        }
    }

    /// <summary>
    /// 通道条 EQ 曲线屏（Mixer.txt:47-49）：80×76、圆角 8、零线在 38px。
    ///
    /// 曲线数学与 `MixFxDisplays.EqCurveDisplay` 同源（`BiquadEQ.ResponseDb`，采样率跟随
    /// `MixFxSource`），差别只在版面：这里**满幅**绘制（80×76 无内边距、无刻度文字、
    /// 无填充色——稿里 EQ Curve 的底层填充是 `transparent`，见 spec-digest §9.1）。
    /// 零线由 XAML 的 1px Border 画（可被几何契约测试直接断言），它的 y 与这里
    /// <see cref="ZeroY"/> 都等于 <see cref="MixerMetrics.EqZeroLineY"/>。
    /// </summary>
    public sealed class MixerEqCurve : Control {
        public static readonly StyledProperty<double> LowDbProperty =
            AvaloniaProperty.Register<MixerEqCurve, double>(nameof(LowDb));
        public static readonly StyledProperty<double> MidFreqProperty =
            AvaloniaProperty.Register<MixerEqCurve, double>(nameof(MidFreq), 1000);
        public static readonly StyledProperty<double> MidDbProperty =
            AvaloniaProperty.Register<MixerEqCurve, double>(nameof(MidDb));
        public static readonly StyledProperty<double> HighDbProperty =
            AvaloniaProperty.Register<MixerEqCurve, double>(nameof(HighDb));
        public static readonly StyledProperty<IBrush?> CurveBrushProperty =
            AvaloniaProperty.Register<MixerEqCurve, IBrush?>(nameof(CurveBrush));

        public double LowDb { get => GetValue(LowDbProperty); set => SetValue(LowDbProperty, value); }
        public double MidFreq { get => GetValue(MidFreqProperty); set => SetValue(MidFreqProperty, value); }
        public double MidDb { get => GetValue(MidDbProperty); set => SetValue(MidDbProperty, value); }
        public double HighDb { get => GetValue(HighDbProperty); set => SetValue(HighDbProperty, value); }
        /// <summary>曲线色；为空回退颜色池 primary（稿里 EQ Curve 用 primary）。</summary>
        public IBrush? CurveBrush { get => GetValue(CurveBrushProperty); set => SetValue(CurveBrushProperty, value); }

        const double MinFreq = 20, MaxFreq = 20000, RangeDb = 15;
        /// <summary>旁通时的降透明度（与 FxDisplay 家族同口径）。</summary>
        const double BypassedOpacity = 0.45;

        BiquadEQ eq = new BiquadEQ(MixFxSource.SampleRate, MixFxSource.Channels);
        int eqSampleRate = MixFxSource.SampleRate;
        int eqChannels = MixFxSource.Channels;

        static MixerEqCurve() {
            AffectsRender<MixerEqCurve>(LowDbProperty, MidFreqProperty, MidDbProperty,
                HighDbProperty, CurveBrushProperty, IsEnabledProperty);
        }

        /// <summary>绘图区 = 整屏（无内边距）。</summary>
        public Rect PlotRect => new Rect(Bounds.Size);

        /// <summary>零线纵坐标（80×76 时 = 38）。</summary>
        public double ZeroY => Bounds.Height / 2;

        void SyncFormat() {
            if (eqSampleRate == MixFxSource.SampleRate && eqChannels == MixFxSource.Channels) {
                return;
            }
            eqSampleRate = MixFxSource.SampleRate;
            eqChannels = MixFxSource.Channels;
            eq = new BiquadEQ(eqSampleRate, eqChannels);
        }

        /// <summary>频率 → 横坐标（20Hz–20kHz 对数轴，满幅）。</summary>
        public static double FrequencyToX(double freq, Rect plot) =>
            plot.Left + Math.Log(freq / MinFreq) / Math.Log(MaxFreq / MinFreq) * plot.Width;

        /// <summary>增益 → 纵坐标（±15dB 线性，0dB 落在垂直中心）。</summary>
        public static double LevelToY(double db, Rect plot) =>
            plot.Center.Y - Math.Clamp(db, -RangeDb, RangeDb) / RangeDb * plot.Height / 2;

        /// <summary>
        /// 当前参数下的幅频响应折线（<see cref="Render"/> 画的就是它——断言它等于断言屏上的曲线）。
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

        public override void Render(DrawingContext context) {
            var rect = new Rect(Bounds.Size);
            if (rect.Width < 8 || rect.Height < 8) {
                return;
            }
            var pen = new Pen(CurveBrush ?? ColorPool.Current.Brush(Md3Role.Primary), 1.5) {
                LineJoin = PenLineJoin.Round,
                LineCap = PenLineCap.Round,
            };
            var points = BuildCurvePoints(rect, Math.Max(2, (int)rect.Width));
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open()) {
                ctx.BeginFigure(points[0], false);
                for (int i = 1; i < points.Length; i++) {
                    ctx.LineTo(points[i]);
                }
                ctx.EndFigure(false);
            }
            if (!IsEnabled) {
                using (context.PushOpacity(BypassedOpacity)) {
                    context.DrawGeometry(null, pen, geometry);
                }
                return;
            }
            context.DrawGeometry(null, pen, geometry);
        }
    }
}

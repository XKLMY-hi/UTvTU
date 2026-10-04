using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using OpenUtau.App.Controls;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.SignalChain.Effects;
using OpenUtau.Core.Theming;
using OpenUtau.Theming;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// 实时效果机架自绘控件（<see cref="Knob"/> 与三块曲线屏）的行为测试。
    ///
    /// 断言的是**真实行为**而不是"能构造"：
    /// · 键盘 / 指针 / 滚轮通过真实路由事件驱动，检查 Value 的确定性变化；
    /// · 曲线几何直接断言"RenderPlot 画的那条折线"（<c>BuildCurvePoints</c>/<c>BuildEnvelope</c>
    ///   就是 RenderPlot 的绘制来源，不是另写一份数学），因此"0 dB 贴 0 线""低架抬左端"
    ///   "1:1 走对角线""DRY 不画包络"都是对真实绘制数据的断言；
    /// · <see cref="ProbeDisplay"/> 反过来证明 <c>Render</c> 确实被布局/渲染管线调到
    ///   （不是"没画所以没抛"），并锁定绘图区内边距。
    ///
    /// 注：本仓 headless 走 <c>UseHeadlessDrawing</c> 桩绘制，<see cref="Avalonia.Media.Imaging.RenderTargetBitmap"/>
    /// 拿到的是空白位面，因此这里不做逐像素断言——几何一律走上面的折线接口。
    /// </summary>
    [Collection("Theme")]
    public class FxRackControlTests {
        const int PlotW = 260;
        const int PlotH = 130;
        // 绘图区内边距（左,上,右,下），与 FxDisplay.PlotPadding 约定一致
        static readonly Rect Plot = new Rect(8, 8, PlotW - 16, PlotH - 14);

        static readonly Pointer TestPointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);

        /// <summary>探针控件：记录 RenderPlot 被调用的次数与收到的绘图区。</summary>
        sealed class ProbeDisplay : FxDisplay {
            public int Renders;
            public Rect LastPlot;
            protected override void RenderPlot(DrawingContext context, Rect rect) {
                Renders++;
                LastPlot = rect;
                context.DrawRectangle(Brushes.Magenta, null, rect);   // 真的画点东西
            }
        }

        // ── 输入辅助（走真实路由事件，与平台输入同一条路径） ──

        static void PressKey(Control target, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
            target.RaiseEvent(new KeyEventArgs {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = key,
                KeyModifiers = modifiers,
            });

        static void LeftPress(Control target, Point p, int clickCount = 1) =>
            target.RaiseEvent(new PointerPressedEventArgs(target, TestPointer, target, p, 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
                KeyModifiers.None, clickCount));

        static void Move(Control target, Point p) =>
            target.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, target, TestPointer, target, p, 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other),
                KeyModifiers.None));

        static void Release(Control target, Point p) =>
            target.RaiseEvent(new PointerReleasedEventArgs(target, TestPointer, target, p, 0,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
                KeyModifiers.None, MouseButton.Left));

        static void Wheel(Control target, Point p, double deltaY) =>
            target.RaiseEvent(new PointerWheelEventArgs(target, TestPointer, target, p, 0,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
                KeyModifiers.None, new Vector(0, deltaY)));

        /// <summary>把控件放进一个真正显示的窗口（焦点/指针捕获可用）。</summary>
        static void InWindow(Control content, double width, double height, Action<Window> body) {
            content.HorizontalAlignment = HorizontalAlignment.Left;
            content.VerticalAlignment = VerticalAlignment.Top;
            var window = new Window { Width = width, Height = height, Content = content };
            try {
                window.Show();
                body(window);
            } finally {
                window.Close();
            }
        }

        /// <summary>按显式尺寸完成布局并跑一次绘制管线。</summary>
        static void RenderOnce(Control content, double width, double height) {
            content.Measure(new Size(width, height));
            content.Arrange(new Rect(0, 0, width, height));
            using var target = new Avalonia.Media.Imaging.RenderTargetBitmap(
                new PixelSize((int)width, (int)height));
            target.Render(content);
        }

        static void UsePool() =>
            ColorPool.Initialize(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, true);

        /// <summary>包络在 <paramref name="from"/> 之后第一次落到指定底线的采样下标（找不到则返回长度）。</summary>
        static int FirstFloorIndex(Point[] points, double floorY, int from) {
            for (int i = from; i < points.Length; i++) {
                if (points[i].Y >= floorY - 1e-6) {
                    return i;
                }
            }
            return points.Length;
        }

        // ══════════════════════ 绘制管线确实被调用 ══════════════════════

        [AvaloniaFact]
        public void FxDisplay_RenderIsActuallyInvoked_AndUsesDocumentedPlotPadding() {
            UsePool();
            var probe = new ProbeDisplay { Width = PlotW, Height = PlotH };
            RenderOnce(probe, PlotW, PlotH);
            Assert.Equal(1, probe.Renders);
            Assert.Equal(Plot, probe.LastPlot);
            Assert.Equal(Plot, probe.PlotRect);

            // 旁通（IsEnabled=false）仍然照画（只是整体降透明度），不是跳过绘制
            probe.IsEnabled = false;
            RenderOnce(probe, PlotW, PlotH);
            Assert.Equal(2, probe.Renders);

            // 尺寸太小 → 不画（避免除零/负矩形）
            var tiny = new ProbeDisplay { Width = 12, Height = 12 };
            RenderOnce(tiny, 12, 12);
            Assert.Equal(0, tiny.Renders);
        }

        [AvaloniaFact]
        public void Displays_RenderWithoutThrowing_AcrossParameterSweeps() {
            UsePool();
            var eq = new EqCurveDisplay { Width = PlotW, Height = PlotH };
            var comp = new CompCurveDisplay { Width = PlotW, Height = PlotH };
            var rev = new ReverbCurveDisplay { Width = PlotW, Height = PlotH };

            RenderOnce(eq, PlotW, PlotH);
            RenderOnce(comp, PlotW, PlotH);
            RenderOnce(rev, PlotW, PlotH);

            eq.LowDb = 12; eq.MidDb = -6; eq.HighDb = 12; eq.MidFreq = 600;
            comp.ThresholdDb = -30; comp.Ratio = 8; comp.MakeupDb = 6;
            rev.RoomSize = 0.9; rev.Damp = 0.2; rev.Wet = 2; rev.PreDelayMs = 60; rev.Preset = "hall";
            RenderOnce(eq, PlotW, PlotH);
            RenderOnce(comp, PlotW, PlotH);
            RenderOnce(rev, PlotW, PlotH);

            // 模块旁通：整屏降透明度分支
            eq.IsEnabled = false; comp.IsEnabled = false; rev.IsEnabled = false;
            RenderOnce(eq, PlotW, PlotH);
            RenderOnce(comp, PlotW, PlotH);
            RenderOnce(rev, PlotW, PlotH);
            eq.IsEnabled = true; comp.IsEnabled = true; rev.IsEnabled = true;

            // 混响 DRY 分支（预设 off → 有效湿声 0）
            rev.Preset = "off";
            RenderOnce(rev, PlotW, PlotH);

            // 极端值不产生 NaN / 非法几何
            eq.LowDb = -15; eq.MidDb = 15; eq.HighDb = -15; eq.MidFreq = 20000;
            comp.ThresholdDb = -60; comp.Ratio = 20; comp.MakeupDb = 12;
            rev.RoomSize = 0; rev.Damp = 1; rev.Wet = 0; rev.PreDelayMs = 200;
            RenderOnce(eq, PlotW, PlotH);
            RenderOnce(comp, PlotW, PlotH);
            RenderOnce(rev, PlotW, PlotH);
        }

        // ══════════════════════ Knob ══════════════════════

        [AvaloniaFact]
        public void Knob_ValueIsClampedToRange() {
            var knob = new Knob { Minimum = -12, Maximum = 12, DefaultValue = 0 };
            knob.Value = 999;
            Assert.Equal(12, knob.Value);
            knob.Value = -999;
            Assert.Equal(-12, knob.Value);
        }

        [AvaloniaFact]
        public void Knob_Keyboard_StepsEndpointsAndFineModifier() {
            var knob = new Knob { Minimum = -12, Maximum = 12, DefaultValue = 0, Width = 64, Height = 64 };
            InWindow(knob, 80, 80, _ => {
                // 单步 = 量程/100 = 0.24；Shift 为 1/10 微调 = 0.024
                PressKey(knob, Key.Up);
                Assert.Equal(0.24, knob.Value, 6);
                PressKey(knob, Key.Right);
                Assert.Equal(0.48, knob.Value, 6);
                PressKey(knob, Key.Down);
                Assert.Equal(0.24, knob.Value, 6);
                PressKey(knob, Key.Left);
                Assert.Equal(0.0, knob.Value, 6);
                PressKey(knob, Key.Up, KeyModifiers.Shift);
                Assert.Equal(0.024, knob.Value, 6);
                PressKey(knob, Key.PageUp);   // 10 步
                Assert.Equal(2.424, knob.Value, 6);
                PressKey(knob, Key.PageDown);
                Assert.Equal(0.024, knob.Value, 6);
                PressKey(knob, Key.End);
                Assert.Equal(12, knob.Value);
                PressKey(knob, Key.Home);
                Assert.Equal(-12, knob.Value);
                // 到端点后再按不越界
                PressKey(knob, Key.Down);
                Assert.Equal(-12, knob.Value);
            });
        }

        [AvaloniaFact]
        public void Knob_DoubleClick_RestoresDefaultValue() {
            var knob = new Knob { Minimum = -12, Maximum = 12, DefaultValue = 3.5, Width = 64, Height = 64 };
            InWindow(knob, 80, 80, _ => {
                knob.Value = -7;
                LeftPress(knob, new Point(32, 32), clickCount: 2);
                Assert.Equal(3.5, knob.Value);
            });
        }

        [AvaloniaFact]
        public void Knob_VerticalDrag_ScalesWithRange_AndStopsAfterRelease() {
            var knob = new Knob { Minimum = -12, Maximum = 12, DefaultValue = 0, Width = 64, Height = 64 };
            InWindow(knob, 80, 120, _ => {
                // 200 px 走完全量程(24) → 每像素 0.12；上移 20 px 即 +2.4
                LeftPress(knob, new Point(32, 60));
                Move(knob, new Point(32, 40));
                Assert.Equal(2.4, knob.Value, 6);
                Move(knob, new Point(32, 20));
                Assert.Equal(4.8, knob.Value, 6);
                // 下移是减
                Move(knob, new Point(32, 40));
                Assert.Equal(2.4, knob.Value, 6);
                Release(knob, new Point(32, 40));
                // 松开后再移动不再改值
                Move(knob, new Point(32, 0));
                Assert.Equal(2.4, knob.Value, 6);
                // 双击把值拉回 DefaultValue
                LeftPress(knob, new Point(32, 40), clickCount: 2);
                Assert.Equal(0, knob.Value, 6);
            });
        }

        [AvaloniaFact]
        public void Knob_Wheel_StepsByHundredthOfRange() {
            var knob = new Knob { Minimum = 0, Maximum = 2, DefaultValue = 1, Width = 64, Height = 64 };
            InWindow(knob, 80, 80, _ => {
                knob.Value = 1;                      // DefaultValue 只是双击复位值，不初始化 Value
                Wheel(knob, new Point(32, 32), 1);
                Assert.True(Math.Abs(knob.Value - 1.02) < 1e-9, $"滚轮 +1 后 Value={knob.Value}，期望 1.02");
                Wheel(knob, new Point(32, 32), -1);
                Assert.True(Math.Abs(knob.Value - 1.0) < 1e-9, $"滚轮 -1 后 Value={knob.Value}，期望 1.0");
                // 双击复位到 DefaultValue
                LeftPress(knob, new Point(32, 32), clickCount: 2);
                Assert.Equal(1, knob.Value);
            });
        }

        [AvaloniaFact]
        public void Knob_RendersWithoutThrowing_WhileDisabledAndFocused() {
            UsePool();
            var knob = new Knob { Minimum = -12, Maximum = 12, DefaultValue = 0, Width = 64, Height = 64 };
            InWindow(knob, 80, 80, _ => {
                knob.Value = 5;
                RenderOnce(knob, 64, 64);
                knob.IsEnabled = false;               // 旁通降透明度路径
                RenderOnce(knob, 64, 64);
                knob.IsEnabled = true;
                knob.Focus();                          // 焦点环路径
                Assert.True(knob.IsFocused);
                RenderOnce(knob, 64, 64);
            });
        }

        // ══════════════════════ 曲线几何（= RenderPlot 绘制来源） ══════════════════════

        [AvaloniaFact]
        public void EqCurveDisplay_FlatResponse_HugsZeroLine_AndShelvesLiftTheEnds() {
            UsePool();
            var eq = new EqCurveDisplay { LowDb = 0, MidDb = 0, HighDb = 0, MidFreq = 1000 };

            // 全 0 dB → 整条曲线贴 0 线（绘图区垂直中心）
            var flat = eq.BuildCurvePoints(Plot, 128);
            Assert.Equal(128, flat.Length);
            foreach (var p in flat) {
                Assert.True(Math.Abs(p.Y - Plot.Center.Y) < 0.5,
                    $"0 dB 时曲线应贴 0 线(y={Plot.Center.Y:0.##})，实际 y={p.Y:0.##}");
            }
            // 频率轴：对数、左 20 Hz 右 20 kHz；几何中频(632 Hz)正好落在绘图区横向中心
            Assert.Equal(Plot.Left, flat[0].X, 6);
            Assert.Equal(Plot.Right, flat[^1].X, 6);
            Assert.Equal(Plot.Center.X, EqCurveDisplay.FrequencyToX(Math.Sqrt(20.0 * 20000.0), Plot), 6);
            Assert.Equal(Plot.Center.Y, EqCurveDisplay.LevelToY(0, Plot), 6);

            // 低/高搁架各 +12 dB → 两端抬到上方 12/15 半高处
            eq.LowDb = 12;
            eq.HighDb = 12;
            var boosted = eq.BuildCurvePoints(Plot, 128);
            double expectedY = Plot.Center.Y - 12.0 / 15 * Plot.Height / 2;
            Assert.True(Math.Abs(boosted[0].Y - expectedY) < 1.5,
                $"低架 +12 dB 后左端应到 y≈{expectedY:0.##}，实际 {boosted[0].Y:0.##}");
            Assert.True(Math.Abs(boosted[^1].Y - expectedY) < 1.5,
                $"高架 +12 dB 后右端应到 y≈{expectedY:0.##}，实际 {boosted[^1].Y:0.##}");
            // 中频未被中段增益影响（MidDb=0），中点仍贴 0 线
            Assert.True(Math.Abs(boosted[64].Y - Plot.Center.Y) < 1.5);

            // 切削：-12 dB 低架 → 左端落到下方对称位置
            eq.LowDb = -12;
            eq.HighDb = 0;
            var cut = eq.BuildCurvePoints(Plot, 128);
            Assert.True(cut[0].Y > Plot.Center.Y, "低架 -12 dB 后左端必须下移");
            Assert.True(Math.Abs(cut[0].Y - (Plot.Center.Y + 12.0 / 15 * Plot.Height / 2)) < 1.5);
        }

        [AvaloniaFact]
        public void CompCurveDisplay_RatioOneIsUnityDiagonal_AndCompressionBendsItDown() {
            UsePool();
            var comp = new CompCurveDisplay { ThresholdDb = 0, Ratio = 1, MakeupDb = 0 };

            // 1:1、无补偿 → 传输曲线就是 1:1 对角线
            var diagonal = comp.BuildCurvePoints(Plot, 121);
            foreach (var p in diagonal) {
                // 输入 -60..0 → 输出 -60..0；注意纵轴范围是 -60..+6
                double input = -60 + 60.0 * (p.X - Plot.Left) / Plot.Width;
                double expected = Plot.Bottom - (input - (-60)) / 66 * Plot.Height;
                Assert.True(Math.Abs(p.Y - expected) < 0.01,
                    $"1:1 时输入 {input:0.#} dB 的输出 y 应为 {expected:0.##}，实际 {p.Y:0.##}");
            }

            // 阈值 -30 dB、8:1 → 阈值以上的输出被压住：同一输入点 y 更大（电平更低）
            comp.ThresholdDb = -30;
            comp.Ratio = 8;
            var compressed = comp.BuildCurvePoints(Plot, 121);
            int at0Db = 120;                       // 最后一个点 = 输入 0 dB
            Assert.Equal(0, -60 + 60.0 * (compressed[at0Db].X - Plot.Left) / Plot.Width, 6);
            Assert.True(compressed[at0Db].Y > diagonal[at0Db].Y,
                "8:1 压缩后 0 dB 输入的输出必须低于 1:1 对角线");
            // 阈值处是折点：阈值以下仍与对角线重合
            int atMinus48 = 24;                    // 输入 -48 dB
            Assert.True(Math.Abs(compressed[atMinus48].Y - diagonal[atMinus48].Y) < 0.01,
                "阈值以下的输入不应被压缩");
            // 补偿增益整体抬升（y 变小）
            comp.MakeupDb = 6;
            var withMakeup = comp.BuildCurvePoints(Plot, 121);
            Assert.True(withMakeup[at0Db].Y < compressed[at0Db].Y, "补偿 +6 dB 应整体抬升输出");
        }

        [AvaloniaFact]
        public void ReverbCurveDisplay_OffPresetIsDry_AndEnvelopeDropsOverRt60() {
            UsePool();
            var rev = new ReverbCurveDisplay {
                // 预延迟取 500 ms，让"预延迟平台段"在 200 个采样点里占足够宽度（20 ms 不足一个步长）
                RoomSize = 0.6, Damp = 0.4, Wet = 1, PreDelayMs = 500, Preset = "hall",
            };
            // 有效湿声 = 预设 Wet × 用户 Wet
            double hallWet = rev.EffectiveWet();
            Assert.True(hallWet > 0, "hall 预设应有湿声");
            Assert.Equal(FxPresets.Reverb["hall"].Wet / 0.5, hallWet, 6);

            // 预设 off → 干声（≤1e-4），RenderPlot 走 DRY 分支不画包络
            rev.Preset = "off";
            Assert.True(rev.EffectiveWet() <= 1e-4, $"off 预设应为干声，实际 {rev.EffectiveWet()}");
            rev.Preset = "hall";

            // 包络：预延迟内贴在底噪线（不上升），之后按 RT60 单调下滑
            var (low, high) = rev.Decay();
            Assert.True(low > 0 && high > 0);
            Assert.True(high < low, "阻尼后的高频尾音必须比全频段尾音短");
            var envelope = rev.BuildEnvelope(low, Plot, 200);
            Assert.Equal(Plot.Left, envelope[0].X, 6);
            Assert.Equal(Plot.Right, envelope[^1].X, 6);
            Assert.Equal(Plot.Bottom, envelope[0].Y, 3);          // 预延迟内 = 底噪线
            for (int i = 0; i < 20; i++) {                        // 0–0.4 s 仍在预延迟内
                Assert.Equal(Plot.Bottom, envelope[i].Y, 3);
            }
            for (int i = 40; i < envelope.Length; i++) {          // 衰减段必须单调下降
                Assert.True(envelope[i].Y >= envelope[i - 1].Y - 1e-6,
                    $"衰减段第 {i} 点应不比前一点更高（Y {envelope[i - 1].Y:0.##} → {envelope[i].Y:0.##}）");
            }
            // 更短 RT60 → 同一时刻剩余更少（y 更大），且更早衰减到底噪线
            var shorter = rev.BuildEnvelope(low * 0.5, Plot, 200);
            Assert.True(shorter[40].Y > envelope[40].Y, "RT60 减半后同一时刻应衰减更多");
            Assert.True(FirstFloorIndex(shorter, Plot.Bottom, 40) < FirstFloorIndex(envelope, Plot.Bottom, 40),
                "RT60 减半后应更早触底");
        }

        [AvaloniaFact]
        public void EqCurveDisplay_PicksUpSampleRateChange_AndStillRendersFlatResponse() {
            UsePool();
            int originalRate = AudioSettings.SampleRate;
            int originalChannels = AudioSettings.Channels;
            try {
                var eq = new EqCurveDisplay { LowDb = 0, MidDb = 0, HighDb = 0, MidFreq = 1000 };
                var at44k = eq.BuildCurvePoints(Plot, 64);
                Assert.True(Math.Abs(at44k[32].Y - Plot.Center.Y) < 0.5);

                // 换音频格式 → 控件按新采样率重建 DSP 后仍画出正确的平坦响应
                // （重建若只做了一半，例如新 DSP 没 Configure，这里就会落错位置）
                AudioSettings.Configure(48000, 2);
                Assert.Equal(48000, MixFxSource.SampleRate);

                var at48k = eq.BuildCurvePoints(Plot, 64);
                Assert.Equal(at44k.Length, at48k.Length);
                for (int i = 0; i < at44k.Length; i++) {
                    Assert.Equal(at44k[i].X, at48k[i].X, 6);
                    Assert.Equal(at44k[i].Y, at48k[i].Y, 1);   // 平坦响应与采样率无关
                }
            } finally {
                AudioSettings.Configure(originalRate, originalChannels);
            }
        }
    }
}

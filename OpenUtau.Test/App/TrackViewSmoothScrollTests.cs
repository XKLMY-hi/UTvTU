using System;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenUtau.App.Controls;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// W21-A/C（task-32）：**轨道视图**那半的平滑滚动 + 运输条图标对齐契约。
    ///
    /// 与 W15 的 `SmoothViewportTests`（纯运动学 + 降级路径）互补：这里驱动**真实窗口的动画帧**，
    /// 断言"整步进不跳、逐帧滑过去、ReduceMotion 立即到位"，这正是任务判据里的"平滑滚动手感"与对照。
    /// </summary>
    [Collection("Theme")]
    public class TrackViewSmoothScrollTests {
        private static void Frame() {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            Dispatcher.UIThread.RunJobs();
        }

        [AvaloniaFact]
        public void WheelStep_GlidesOverFrames_ThenLandsOnTarget() {
            bool oldReduceMotion = Preferences.Default.ReduceMotion;
            var bar = new ScrollBar { Minimum = 0, Maximum = 1000, SmallChange = 50, ViewportSize = 100, Value = 500 };
            var win = new Window { Width = 400, Height = 200, Content = bar };
            try {
                win.Show();
                Dispatcher.UIThread.RunJobs();
                win.UpdateLayout();
                var glide = new SmoothViewport(bar).Scroll(bar);

                Preferences.Default.ReduceMotion = false;
                glide.By(-200, animate: SmoothViewport.IsWheelStep(-4));
                Assert.Equal(500, bar.Value, 1);            // 不跳：还没走过一帧

                Frame();
                Assert.True(bar.Value < 500, $"一帧后应已开始滑动，实际 {bar.Value}");
                Assert.True(bar.Value > 300, $"一帧后不应直接到位（要滑 0.18s），实际 {bar.Value}");
                double afterFirstFrame = bar.Value;

                Thread.Sleep(220);                          // 曲线按真实时钟计时
                Frame();
                Assert.Equal(300, bar.Value, 1);            // 到位
                Assert.True(300 < afterFirstFrame, "顺序：目标 < 首帧位置");

                // ReduceMotion 对照：同一动作立即到位（不滑动、不等帧）
                Preferences.Default.ReduceMotion = true;
                glide.By(-100, animate: true);
                Assert.Equal(200, bar.Value, 1);
            } finally {
                Preferences.Default.ReduceMotion = oldReduceMotion;
                win.Close();
            }
        }

        [AvaloniaFact]
        public void PrecisionTouchpadDelta_AppliesAtOnce() {
            var bar = new ScrollBar { Minimum = 0, Maximum = 1000, SmallChange = 50, ViewportSize = 100, Value = 500 };
            var win = new Window { Width = 400, Height = 200, Content = bar };
            try {
                win.Show();
                Dispatcher.UIThread.RunJobs();
                win.UpdateLayout();
                var glide = new SmoothViewport(bar).Scroll(bar);

                // 触控板送小数 delta：animate=false ⇒ 立即生效（否则会把顺滑手势再"加工"一遍）
                glide.By(-12.5, animate: SmoothViewport.IsWheelStep(-0.25));
                Assert.Equal(487.5, bar.Value, 1);
            } finally {
                win.Close();
            }
        }

        [AvaloniaFact]
        public void MainWindow_WiresTrackViewGlide_AndUsesOurReduceMotion() {
            string code = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Views", "MainWindow.axaml.cs"));
            // 四个 glide：横滚 / 纵滚 / 横向缩放 / 轨道高
            Assert.Contains("new SmoothViewport(this)", code);
            Assert.Contains("smoothViewport.Scroll(HScrollBar)", code);
            Assert.Contains("smoothViewport.Scroll(VScrollBar)", code);
            Assert.Contains("smoothViewport.Zoom(", code);
            Assert.Contains("smoothViewport.Value(", code);
            // 滚轮处理改走 glide，并用 IsWheelStep 区分整步进/触控板
            Assert.Contains("hScroll.By(-HScrollBar.SmallChange", code);
            Assert.Contains("vScroll.By(-VScrollBar.SmallChange", code);
            Assert.Contains("hScroll.By(-HScrollBar.SmallChange * delta.X, SmoothViewport.IsWheelStep(delta.X))", code);
            Assert.Contains("xZoom.By(position, 0.1 * args.Delta.Y, SmoothViewport.IsWheelStep(args.Delta.Y))", code);
            Assert.Contains("trackHeight.By(Math.Sign(args.Delta.Y) * ViewConstants.TrackHeightDelta", code);
            // 旧写法（逐格跳）已退役
            Assert.DoesNotContain("HScrollBar.Value = Math.Max(HScrollBar.Minimum", code);
            Assert.DoesNotContain("VScrollBar.Value = Math.Max(VScrollBar.Minimum", code);
            // 复用我们的 ReduceMotion，不引入上游偏好项（注释里提到上游名字是"移植说明"，不算依赖 ⇒ 先剥注释）
            Assert.DoesNotContain("ReduceAnimations", code);
            string smooth = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Controls", "SmoothViewport.cs"));
            string smoothCode = string.Join("\n", smooth.Split('\n')
                .Where(l => !l.TrimStart().StartsWith("///") && !l.TrimStart().StartsWith("//")));
            Assert.Contains("Preferences.Default.ReduceMotion", smoothCode);
            Assert.DoesNotContain("ReduceAnimations", smoothCode);
        }

        [AvaloniaFact]
        public void TracksViewModel_SingleTrackHeightImplementation() {
            string code = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ViewModels", "TracksViewModel.cs"));
            Assert.Contains("public void SetTrackHeight(double trackHeight)", code);
            // 缩放路径委托同一实现（避免两套夹紧逻辑漂移）
            Assert.Contains("SetTrackHeight(TrackHeight + Math.Sign(delta) * ViewConstants.TrackHeightDelta)", code);
            Assert.Contains("Math.Clamp(trackHeight, ViewConstants.TrackHeightMin, ViewConstants.TrackHeightMax)", code);
        }

        /// <summary>
        /// W21-C（`5f14dd89` 运输条图标对齐）：上游的缺陷形态是"图标 Path 不设尺寸、容器又写死总宽"，
        /// 我们重构后的运输条**每个图标都显式定尺 + Stretch=Uniform、按钮定尺、胶囊不写死总宽** ⇒
        /// 该缺陷类在我们结构里不存在。这里把这条结构契约钉住，防止以后回退成"不设尺寸"。
        /// </summary>
        [AvaloniaFact]
        public void TransportIcons_AreExplicitlySizedAndCentered() {
            string xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Views", "MainWindow.axaml"));
            // 运输条五个图标各自显式定尺（16×16）并等比缩放居中
            foreach (string icon in new[] { "icon-skip-back", "icon-play", "icon-pause", "icon-skip-forward", "icon-metronome" }) {
                string line = FindIconLine(xaml, icon);
                Assert.Contains("Width=\"16\"", line);
                Assert.Contains("Height=\"16\"", line);
                Assert.Contains("Stretch=\"Uniform\"", line);
            }
            // 按钮定尺（tpBtn/tpPlay 在样式里给 36×36）——图标居中由"等尺寸按钮 + 等尺寸图标盒"保证
            Assert.Contains("<Style Selector=\"Button.tpBtn\">", xaml);
            Assert.Contains("<Style Selector=\"Button.tpPlay\">", xaml);
            // 运输条胶囊不写死总宽（上游缺陷的另一半：Width=127 固定宽 + 不等尺寸图标 = 视觉错位）
            int chipStart = xaml.IndexOf("<!-- 运输条", StringComparison.Ordinal);
            Assert.True(chipStart >= 0, "找不到运输条注释块");
            int chipEnd = xaml.IndexOf("<!-- 播放位置", chipStart, StringComparison.Ordinal);
            Assert.True(chipEnd > chipStart, "找不到运输条结束位置");
            string chip = xaml.Substring(chipStart, chipEnd - chipStart);
            // 胶囊自己不写死总宽（上游缺陷：Width=127 固定宽 + 不等尺寸图标 = 视觉错位）
            string chipBorder = chip.Split('\n').First(l => l.Contains("Classes=\"tpChip\"") && l.Contains("Height=\"40\""));
            Assert.DoesNotContain("Width=", chipBorder);
        }

        private static string FindIconLine(string xaml, string iconKey) {
            foreach (string line in xaml.Split('\n')) {
                if (line.Contains($"{{StaticResource {iconKey}}}") && line.Contains("<Path")) {
                    return line;
                }
            }
            throw new Xunit.Sdk.XunitException($"MainWindow.axaml 里找不到 {iconKey} 的 Path 行");
        }
    }
}

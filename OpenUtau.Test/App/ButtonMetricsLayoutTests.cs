using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Styling;
using OpenUtau.App;
using OpenUtau.App.Controls;
using OpenUtau.Core.Theming;
using OpenUtau.Theming;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// 按钮尺寸的**布局层**契约（W10 口径：断言 <see cref="Layoutable.Bounds"/>，不是声明值）。
    ///
    /// 立这一条是为了把本仓两处"继承来的几何"钉成可执行规则：
    ///   · 全局 <c>Md3ButtonTheme</c> 设 <c>MinHeight=32</c>（<c>Styles/Md3ControlThemes.axaml:28</c>），
    ///     布局取 <c>Max(MinHeight, Height)</c> ⇒ **所有声明 &lt;32 高的 Button 类都会被顶到 32**；
    ///   · 应用级 <c>Button { Margin: 0,4 }</c>（<c>Styles/Styles.axaml:159</c>）给每个 Button
    ///     外加 8px 竖向 margin（中和办法：自己样式里写 <c>Margin=0</c>）；
    ///   · 容器有硬约束时并不安全：Measure 阶段 <c>DesiredSize</c> 会被父夹到可用高度，
    ///     但 **Arrange 仍尊重 MinHeight** ⇒ 实测"胶囊内区 30 / 按钮 Bounds 32 / 胶囊 36"，
    ///     按钮在胶囊里上下各溢出 1px（Border 默认不裁剪，肉眼看不出被切）。
    ///   · 另一半规则（反向）：Avalonia 的**类型选择器精确匹配**，`Button` 选择器**不匹配**
    ///     ToggleButton ⇒ 自定义 ToggleButton 既不吃该 MinHeight，也不吃应用级 Margin。
    ///
    /// 说明：headless **起不了 MainWindow**（构造即拉 Updater/定时器，见 ViewSwitcherTests 头注），
    /// 所以真窗口层用"源码契约 + 复刻宿主实量"两段拼起来：
    ///   ① 从**随构建产出的** <c>Views/MainWindow.axaml</c> 里读出两个样式块，断言中和 Setter 在位；
    ///   ② 用同样的一组 Setter + 同样的父链复刻宿主，按 Bounds 量 30 / 28 / 36。
    ///   ① 保证"文件里真有这一行"，② 保证"这一行确实把高度压到声明值"。
    /// </summary>
    [Collection("Theme")]
    public class ButtonMetricsLayoutTests {
        static void SyncPool() =>
            ColorPool.Initialize(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, ThemeManager.IsDarkMode);

        static string MainWindowXaml() =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Views", "MainWindow.axaml"));

        /// <summary>取出某个样式选择器的第一个样式块（用于把复刻宿主与真实文件对齐）。</summary>
        static string StyleBlock(string xaml, string selector) {
            int start = xaml.IndexOf($"<Style Selector=\"{selector}\">", StringComparison.Ordinal);
            Assert.True(start >= 0, $"MainWindow.axaml 里找不到样式：{selector}");
            int end = xaml.IndexOf("</Style>", start, StringComparison.Ordinal);
            Assert.True(end > start, $"样式块未闭合：{selector}");
            return xaml.Substring(start, end - start);
        }

        /// <summary>
        /// 一轮真实布局：Measure → Arrange → 排空 dispatcher → <see cref="Layoutable.UpdateLayout"/>。
        /// 断言点必须是控件自身 <c>Bounds</c>：Measure 阶段子控件的 <c>DesiredSize</c> 会被父容器
        /// 夹到可用高度（胶囊内区 30），**只有 Bounds 会暴露 MinHeight 造成的 32**。
        /// </summary>
        static void Layout(Window win, double width, double height) {
            win.Measure(new Size(width, height));
            win.Arrange(new Rect(0, 0, width, height));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            win.UpdateLayout();
        }

        // ══════════════ 通用规则：MinHeight=32 顶小按钮 / 类型选择器精确匹配 ══════════════

        [AvaloniaFact]
        public void GeneralRule_SmallButtonsArePushedToThemeMinHeight_ButToggleButtonIsNot() {
            SyncPool();
            var bare = new Button { Height = 20, Width = 40 };                  // 无本地样式
            var toggle = new ToggleButton { Height = 20, Width = 40 };          // 派生自 Button
            var win = new WindowEx {
                Width = 200, Height = 200,
                Content = new StackPanel { Spacing = 8, Children = { bare, toggle } },
            };
            win.Show();
            Layout(win, 200, 200);
            try {
                // 小 Button 被主题 MinHeight=32 顶高（这正是 W10 的结论）
                Assert.Equal(32, bare.Bounds.Height);
                // ToggleButton 既不吃 Button 主题的 MinHeight，也不吃应用级 Margin
                // （Avalonia 类型选择器精确匹配）⇒ 保持 20、竖向 margin 为 0
                Assert.Equal(20, toggle.Bounds.Height);
                Assert.Equal(0, toggle.Margin.Top);
                // 应用级 `Button { Margin: 0,4 }` 给普通 Button 外加 8px 竖向 margin
                Assert.Equal(4, bare.Margin.Top);
            } finally {
                win.Close();
            }
        }

        // ══════════════ ① 源码契约：两个样式块带中和 Setter ══════════════

        [AvaloniaFact]
        public void MainWindowMarkup_CarriesMinHeightNeutralizersForCapsuleAndLibraryTabs() {
            string xaml = MainWindowXaml();
            // 视图胶囊选项：Height=30 必须配 MinHeight=30（否则被顶到 32）
            string viewTab = StyleBlock(xaml, "Button.viewTab");
            Assert.Contains("<Setter Property=\"Height\" Value=\"30\"/>", viewTab);
            Assert.Contains("<Setter Property=\"MinHeight\" Value=\"30\"/>", viewTab);
            Assert.Contains("<Setter Property=\"Margin\" Value=\"0\"/>", viewTab);
            // 素材库页签：Height=28 必须配 MinHeight=28
            string libTab = StyleBlock(xaml, "Button.libTab");
            Assert.Contains("<Setter Property=\"Height\" Value=\"28\"/>", libTab);
            Assert.Contains("<Setter Property=\"MinHeight\" Value=\"28\"/>", libTab);
            Assert.Contains("<Setter Property=\"Margin\" Value=\"0\"/>", libTab);
            // 全局主题与应用级样式一律不许被本类改动（中和只允许写在各自样式里）：
            // 主题的 MinHeight=32 直接从随构建产出的 Styles/Md3ControlThemes.axaml 读；
            // 应用级 `Button { Margin: 0,4 }` 由上面的 GeneralRule 用例以 bare.Margin.Top==4
            // 断言（Styles/Styles.axaml 未复制到测试输出，故走可执行断言而非读文件）。
            string themes = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Styles", "Md3ControlThemes.axaml"));
            Assert.Contains("<Setter Property=\"MinHeight\" Value=\"32\"/>", themes);
        }

        // ══════════════ ② 复刻宿主实量：胶囊 30 / 36，页签 28 ══════════════

        [AvaloniaFact]
        public void Capsule_RendersViewTabAt30_AndContainerStaysAt36() {
            SyncPool();
            var win = new WindowEx { Width = 420, Height = 300 };
            win.Classes.Set("no-motion", true);
            // 复刻 MainWindow.axaml 的窗口级样式（Window.Styles 是窗口作用域、不能跨窗复用；
            // 这里只搬与几何相关的 Setter，颜色/内容与尺寸无关）
            win.Styles.Add(Rule("viewTab", (Button.HeightProperty, 30d), (Button.MinHeightProperty, 30d)));
            // 反向对照臂：只写 Height、不压 MinHeight（= 修前的 MainWindow.axaml）
            win.Styles.Add(Rule("viewTabNoMin", (Button.HeightProperty, 30d)));
            var tabs = BuildCapsuleTabs("viewTab", out var capsule);
            var trapTabs = BuildCapsuleTabs("viewTabNoMin", out var trapCapsule);
            win.Content = new StackPanel { Spacing = 10, Children = { capsule, trapCapsule } };
            win.Show();
            Layout(win, 420, 300);
            try {
                // 选项 30（修前是 32：MinHeight=32 顶掉 + Arrange 溢出内区）
                foreach (var child in tabs.Children) {
                    Assert.Equal(30, ((Button)child).Bounds.Height);
                }
                // 内区 30 = 容器 36 − 内边距 3×2，三处几何同时成立
                Assert.Equal(30, tabs.Bounds.Height);
                Assert.Equal(36, capsule.Bounds.Height);
                // 对照臂（不压 MinHeight）：按钮 Bounds 仍是 32、在 36 的胶囊里上下各溢 1px，
                // 而内区 30 与胶囊 36 都不变 —— 即"Measure 可被父夹紧、Arrange 仍尊重 MinHeight"
                Assert.Equal(32, ((Button)trapTabs.Children[0]).Bounds.Height);
                Assert.Equal(30, trapTabs.Bounds.Height);
                Assert.Equal(36, trapCapsule.Bounds.Height);
            } finally {
                win.Close();
            }
        }

        /// <summary>复刻真实父链：Border(36, padding 3, r999) &gt; StackPanel(spacing 3) &gt; 3× Button。</summary>
        static StackPanel BuildCapsuleTabs(string className, out Border capsule) {
            var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
            for (int i = 0; i < 3; i++) {
                tabs.Children.Add(new Button { Classes = { className }, Content = $"tab{i}" });
            }
            capsule = new Border {
                Height = 36, Padding = new Thickness(3), CornerRadius = new CornerRadius(999),
                VerticalAlignment = VerticalAlignment.Center, Child = tabs,
            };
            return tabs;
        }

        [AvaloniaFact]
        public void LibraryTabs_RenderAt28() {
            SyncPool();
            var win = new WindowEx { Width = 420, Height = 240 };
            win.Classes.Set("no-motion", true);
            win.Styles.Add(Rule("libTab", (Button.HeightProperty, 28d), (Button.MinHeightProperty, 28d)));
            var strip = new StackPanel {
                Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(12, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            for (int i = 0; i < 4; i++) {
                strip.Children.Add(new Button { Classes = { "libTab" }, Content = $"lib{i}" });
            }
            // 真实父链：素材库第 1 行 40 高（表头 44 / 页签 40 / 内容 *）
            var row = new Grid { RowDefinitions = new RowDefinitions("40"), Children = { strip } };
            win.Content = row;
            win.Show();
            Layout(win, 420, 240);
            try {
                foreach (var child in strip.Children) {
                    Assert.Equal(28, ((Button)child).Bounds.Height);
                }
                // 页签条本身也是 28（居中对齐在 40 高的行里 ⇒ 不被行高拉伸）
                Assert.Equal(28, strip.Bounds.Height);
            } finally {
                win.Close();
            }
        }

        /// <summary>按类名构造一条按钮样式（只含与几何相关的 Setter）。</summary>
        static Style Rule(string className, params (AvaloniaProperty, double)[] geometry) {
            var style = new Style(x => x.OfType<Button>().Class(className));
            // Margin=0：中和应用级 `Button { Margin: 0,4 }`（与 MainWindow.axaml 同款）
            style.Setters.Add(new Setter(Button.MarginProperty, new Thickness(0)));
            foreach (var (property, value) in geometry) {
                style.Setters.Add(new Setter(property, value));
            }
            return style;
        }
    }
}

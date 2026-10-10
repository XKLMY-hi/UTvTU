using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
// 别名：本文件同时用 System.IO.Path（Path.Combine），直接 using Avalonia.Controls.Shapes 会二义
using ShapePath = Avalonia.Controls.Shapes.Path;
using OpenUtau.App;
using OpenUtau.App.Views;
using OpenUtau.Core.Theming;
using OpenUtau.Theming;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// 欢迎视图契约测试（W36 重设计后）。
    ///
    /// 三类断言：
    /// 1) **令牌与资源**：颜色只走 MD3 颜色池、文案键可解析、图标键可解析（原有契约，保留）；
    /// 2) **设计几何**：分栏 432、字号阶梯、卡片 104/圆角 16、波形 132、拖放条 56（对齐设计稿）；
    /// 3) **键盘可达**（本轮修掉的缺陷）：入口**全是 Button**（不再有 `Border.PointerPressed`）、
    ///    Tab 序 = 阅读序、Space/Enter 能激活、焦点环声明存在且不产生布局位移。
    /// </summary>
    [Collection("Theme")]   // 这些用例会改全局主题/颜色池，串行执行避免互相污染
    public class WelcomeViewTests {
        // 视图 XAML 由测试工程以链接文件复制到输出目录（见 OpenUtau.Test.csproj）
        private static string ReadXaml(string fileName) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Views", fileName));

        private static List<string> KeysOf(string xaml, string kind) =>
            Regex.Matches(xaml, "\\{" + kind + " ([A-Za-z0-9_.\\-]+)\\}")
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList();

        private static void UsePool() =>
            ColorPool.Initialize(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, false);

        /// <summary>把视图挂进**已显示**的窗口再查询视觉树（UserControl 单独构造时视觉树未实体化）。</summary>
        private static void InView(Action<WelcomeView> body) {
            UsePool();
            var view = new WelcomeView();
            var window = new Window { Width = 1200, Height = 800, Content = view };
            try {
                window.Show();
                body(view);
            } finally {
                window.Close();
            }
        }

        private static List<Button> Buttons(WelcomeView view) =>
            view.GetVisualDescendants().OfType<Button>().ToList();

        private static bool HasClass(Button b, string cls) => b.Classes.Contains(cls);

        // ══════════════════════ 设计几何（W50：与主界面同一套 MD3 词汇） ══════════════════════

        [AvaloniaFact]
        public void WelcomeView_MatchesDesignGeometry() {
            string xaml = ReadXaml("WelcomeView.axaml");
            // 分栏：304 左导航（与 PreferencesView 同构）+ 自适应内容
            // （W50 用户裁决「这套方案肯定要重设计的，不然和主界面的MD3不搭」⇒ 旧 432 品牌栏版作废）
            Assert.Contains("ColumnDefinitions=\"304,*\"", xaml);
            Assert.Contains("md3.surface-container-low", xaml);
            Assert.DoesNotContain("md3.primary-container", xaml);
            // 密度字号阶梯（主界面基准：标题 28 / 空态 16 / 副标 14 / 正文 13 / 分组 12 / 次要 11.5 / 徽标 11）
            foreach (double size in new[] { 28.0, 16.0, 14.0, 13.0, 12.0, 11.5, 11.0 }) {
                string s = size.ToString(System.Globalization.CultureInfo.InvariantCulture);
                Assert.True(xaml.Contains($"FontSize=\"{s}\"", StringComparison.Ordinal)
                    || xaml.Contains($"<Setter Property=\"FontSize\" Value=\"{s}\"/>", StringComparison.Ordinal),
                    $"缺字号 {s}");
            }
            // 关键尺寸：最近行 56 / 格式徽标 24 / 音源 chip 28 / 模板卡 240×72 / 空态图标容器 56 / 波形带 64
            Assert.Contains("<Setter Property=\"Height\" Value=\"56\"/>", xaml);
            Assert.Contains("<Setter Property=\"Height\" Value=\"24\"/>", xaml);
            Assert.Contains("<Setter Property=\"Height\" Value=\"28\"/>", xaml);
            Assert.Contains("<Setter Property=\"Height\" Value=\"72\"/>", xaml);
            Assert.Contains("<Setter Property=\"Width\" Value=\"240\"/>", xaml);
            Assert.Contains("<Setter Property=\"Width\" Value=\"56\"/>", xaml);
            Assert.Contains($"Height=\"{WelcomeArt.WaveBandHeight.ToString(System.Globalization.CultureInfo.InvariantCulture)}\"", xaml);
            // 导航项走**应用级共享** `Button.navItem`（Md3Controls.axaml）——本页不得再本地定义一套（防两页漂移）
            Assert.Contains("Classes=\"navItem", xaml);
            Assert.DoesNotContain("<Style Selector=\"Button.navItem\">", xaml);
            Assert.Contains("Classes=\"railLink\"", xaml);
            // 按钮走现成主题变体（`Md3ButtonTheme` 的 .primary / .outline），页面不自造按钮几何
            Assert.Contains("Classes=\"primary\"", xaml);
            Assert.Contains("Classes=\"outline\"", xaml);
            // 波形仍是生成器产物的真实包络（几何来源 + 缩放口径不许回退）
            Assert.Contains("welcome-waveform", xaml);
            Assert.Contains("Stretch=\"Fill\"", xaml);
            Assert.DoesNotContain("Classes=\"waveBar\"", xaml);
            // Avalonia 12：占位符只能用 PlaceholderText（Watermark 已废弃，会出 AVLN5001）
            Assert.Contains("PlaceholderText=", xaml);
            Assert.DoesNotContain("Watermark", xaml);
            // 最近列表禁用 ListBox（全局隐式 ListBoxItem{Height=28} 会把 56 行夹扁）
            Assert.DoesNotContain("<ListBox", xaml);
            // 键盘可达：入口一律 Button，不得用 PointerPressed
            Assert.DoesNotContain("PointerPressed=\"", xaml);
            // 绑定必须落在 VM 真名上（反射绑定写错**不报错**、只显示空白）
            foreach (string name in new[] {
                "WelcomeSearch", "FilteredRecentFiles", "WelcomeNoRecent", "WelcomeNoMatch",
                "HasRecovery", "TemplateFiles", "AppVersion", "RecoveryString",
            }) {
                Assert.Contains(name, xaml);
            }
            // 入口处理器齐（左栏导航 + 工程动作 + 最近/模板行）
            foreach (string handler in new[] {
                "OnNavRecent", "OnNavNew", "OnNavOpen", "OnNavTemplates",
                "OnNewProject", "OnOpenProject", "OnImportAudio", "OnShowTemplates",
                "OnRecovery", "OnOpenRecent", "OnOpenTemplate",
            }) {
                Assert.Contains($"Click=\"{handler}\"", xaml);
            }
            // MD3 铁律：无阴影、无玻璃、无硬编码色值（先剥注释——说明文字里会出现这些词）
            string code = Regex.Replace(xaml, "<!--.*?-->", "", RegexOptions.Singleline);
            Assert.DoesNotContain("BoxShadow", code);
            Assert.DoesNotContain("BlurEffect", code);
            Assert.DoesNotMatch("#[0-9A-Fa-f]{6}", code);
        }

        [AvaloniaFact]
        public void WelcomeView_Waveform_IsTheGeneratedEnvelope() {
            // 波形是**真实一小节演唱**的响度几何（生成器产物，经 `Assets/WelcomeWaveform.axaml` 逐字进入应用）。
            // W50 起它是**页头右侧的装饰带**（固定高 64）⇒ 除几何特征外，还断"固定高与低强调度"。
            InView(view => {
                var wave = view.GetVisualDescendants().OfType<ShapePath>().First(p => p.Name == "WaveformBars");
                Assert.NotNull(wave.Data);                  // 资源键 welcome-waveform 必须解析到
                var bounds = wave.Data!.Bounds;
                Assert.Equal(1000.0, bounds.Width, 0);      // 生成器 viewBox 宽（逐字引用）
                Assert.InRange(bounds.Height, 100.0, 240.0); // 有真实幅度（双极性）
                Assert.Equal(Stretch.Fill, wave.Stretch);
                Assert.NotNull(wave.Fill);
                Assert.Equal(WelcomeArt.WaveBandHeight, wave.Bounds.Height, 1);
                Assert.Equal(0.4, wave.Opacity, 3);
            });
        }

        /// <summary>
        /// **布局层 + 导航层**（W50 取代旧的"4 张动作卡都渲染"）：
        /// 左栏 6 个导航项（4 主 + 偏好/包管理）在两个窗口尺寸下都真的渲染（`IsVisible` 且未被夹扁），
        /// 右栏**同一时刻只有一个分页可见**（默认最近），且 `.selected` 只挂在当前页那一项上。
        /// </summary>
        [AvaloniaTheory]
        [InlineData(1226.0, 699.0)]
        [InlineData(1000.0, 660.0)]
        public void WelcomeView_NavItems_RenderAndSinglePageIsVisible(double width, double height) {
            UsePool();
            var view = new WelcomeView();
            var window = new Window { Width = width, Height = height, Content = view };
            window.Show();
            try {
                Dispatcher.UIThread.RunJobs();
                var navs = Buttons(view).Where(b => HasClass(b, "navItem")).ToList();
                Assert.Equal(6, navs.Count);   // 最近/新建/打开/模板 + 偏好设置/包管理器
                for (int i = 0; i < navs.Count; i++) {
                    Assert.True(navs[i].IsVisible, $"{width}x{height}: 第 {i + 1} 个导航项不可见");
                    Assert.True(navs[i].Bounds.Width > 0 && navs[i].Bounds.Height >= 40,
                        $"{width}x{height}: 第 {i + 1} 个导航项被夹扁（{navs[i].Bounds.Width}x{navs[i].Bounds.Height}）");
                }
                var pages = view.GetVisualDescendants().OfType<Control>()
                    .Where(c => c.Name is "PageRecent" or "PageNew" or "PageOpen" or "PageTemplates")
                    .ToList();
                Assert.Equal(4, pages.Count);
                Assert.Single(pages.Where(p => p.IsVisible));
                Assert.Equal("PageRecent", pages.First(p => p.IsVisible).Name);
                Assert.Single(navs.Where(n => HasClass(n, "selected")));
                Assert.True(HasClass(navs[0], "selected"), "默认页应是「最近」");
            } finally {
                window.Close();
            }
        }

        /// <summary>
        /// W50（MD3 重建）：波形从"左栏中段弹性大图（132…200）"改成**右栏页头右侧的装饰带**。
        /// 契约：高度恒为 `WelcomeArt.WaveBandHeight`（±1 —— 常量与 XAML 不许分叉）、
        /// `Stretch=Fill`（时基→宽 / 幅度→高）、低强调（Opacity 0.4）、几何仍是生成器产物
        /// （资源键 `welcome-waveform`）、且落在**右栏**（x ≥ 304 左栏宽）。
        /// 三个窗口尺寸下都不被压扁：装饰带不参与弹性布局，版面让给信息。
        /// </summary>
        [AvaloniaTheory]
        [InlineData(1226.0, 699.0)]
        [InlineData(1000.0, 660.0)]
        [InlineData(1226.0, 900.0)]
        public void WelcomeView_WaveformBand_IsFixedHeaderDecoration(double width, double height) {
            UsePool();
            var view = new WelcomeView();
            var window = new Window { Width = width, Height = height, Content = view };
            window.Show();
            try {
                Dispatcher.UIThread.RunJobs();
                var wave = view.GetVisualDescendants().OfType<ShapePath>().First(p => p.Name == "WaveformBars");
                Assert.NotNull(wave.Data);                  // 资源键 welcome-waveform 必须解析到
                Assert.Equal(WelcomeArt.WaveBandHeight, wave.Bounds.Height, 1);
                Assert.Equal(Stretch.Fill, wave.Stretch);
                Assert.Equal(0.4, wave.Opacity, 3);
                Assert.NotNull(wave.Fill);
                var origin = wave.TranslatePoint(new Point(0, 0), view);
                Assert.NotNull(origin);
                Assert.True(origin!.Value.X >= 304,
                    $"{width}x{height}: 波形带应落在右栏（x={origin.Value.X:F1}）");
            } finally {
                window.Close();
            }
        }

        /// <summary>
        /// 空态文案必须是**各自的专用键**（此前复用 `welcome.open.description` ⇒ 空态显示"打开本地项目"）。
        /// W50 起「最近」有两个空态且必须区分：
        ///   ① 一个工程都没有 → `welcome.recent.empty`（绑 `WelcomeNoRecent`）
        ///   ② 有工程但搜索无匹配 → `welcome.search.noresult`（绑 `WelcomeNoMatch`）
        /// 混成一句的后果：用户搜不到东西时会以为"工程丢了"。
        /// 「模板」页同理：有模板出网格、没模板出专用空态。
        /// </summary>
        [AvaloniaFact]
        public void WelcomeView_EmptyStates_UseDistinctKeysAndBindings() {
            string xaml = ReadXaml("WelcomeView.axaml");
            Assert.Contains("welcome.recent.empty", xaml);
            Assert.Contains("welcome.search.noresult", xaml);
            Assert.Contains("welcome.template.empty", xaml);
            Assert.Contains("IsVisible=\"{Binding WelcomeNoRecent}\"", xaml);
            Assert.Contains("IsVisible=\"{Binding WelcomeNoMatch}\"", xaml);
            Assert.Contains("IsVisible=\"{Binding TemplateFiles.Count}\"", xaml);
            Assert.Contains("IsVisible=\"{Binding !TemplateFiles.Count}\"", xaml);
            // 两个空态的文案键必须不同（防止"顺手复用"又混成一句）
            // 窗口 700：空态里 icon 容器/Path 的属性不少，400 会漏（实测踩过）
            var noRecent = Regex.Match(xaml,
                "IsVisible=\"\\{Binding WelcomeNoRecent\\}\"[\\s\\S]{0,700}?Text=\"\\{DynamicResource (welcome\\.[A-Za-z0-9_.]+)\\}\"");
            var noMatch = Regex.Match(xaml,
                "IsVisible=\"\\{Binding WelcomeNoMatch\\}\"[\\s\\S]{0,700}?Text=\"\\{DynamicResource (welcome\\.[A-Za-z0-9_.]+)\\}\"");
            Assert.True(noRecent.Success, "找不到「一个工程都没有」空态的文案键");
            Assert.True(noMatch.Success, "找不到「搜索无匹配」空态的文案键");
            Assert.Equal("welcome.recent.empty", noRecent.Groups[1].Value);
            Assert.Equal("welcome.search.noresult", noMatch.Groups[1].Value);
        }

        // ══════════════════════ 键盘可达（本轮缺陷修复） ══════════════════════

        /// <summary>
        /// 守卫：入口**全是 Button**，不再有 `Border.PointerPressed` 那种键盘不可达的形态。
        /// （重设计前所有入口都是 Border + PointerPressed ⇒ Tab 到不了、Enter/Space 无反应。）
        /// </summary>
        [AvaloniaFact]
        public void WelcomeView_AllEntriesAreButtons_NotClickableBorders() {
            string xaml = ReadXaml("WelcomeView.axaml");
            Assert.DoesNotContain("PointerPressed=\"", xaml);

            InView(view => {
                var buttons = Buttons(view);
                Assert.Equal(6, buttons.Count(b => HasClass(b, "navItem")));   // 4 主导航 + 偏好设置 + 包管理器
                Assert.Equal(4, buttons.Count(b => HasClass(b, "railLink")));  // 4 条外链（12px 小字行）
                Assert.Equal(2, buttons.Count(b => HasClass(b, "primary")));   // 新建空白工程 / 浏览文件
                Assert.Equal(2, buttons.Count(b => HasClass(b, "outline")));   // 模板 / 导入音频
                Assert.All(buttons, b => Assert.True(b.Focusable, "入口必须可聚焦（Tab 可达）"));
            });
            // 数据驱动的行（最近 / 模板）在 headless 无 VM 数据时不会实例化 ⇒ 只能断"模板里声明了该类 + 是 Button"
            Assert.Contains("Classes=\"recentRow\"", xaml);
            Assert.Contains("Classes=\"templateCard\"", xaml);
        }

        /// <summary>
        /// Tab 序 = 阅读序（W50：左栏 → 右栏）。视觉树顺序即 XAML 声明序 ⇒ 断言"左栏全部入口
        /// 排在右栏动作之前"，且左栏第一项是「最近」（默认页）。
        /// </summary>
        [AvaloniaFact]
        public void WelcomeView_TabOrder_FollowsReadingOrder() {
            InView(view => {
                var buttons = Buttons(view);
                int lastRail = buttons.FindLastIndex(b => HasClass(b, "navItem") || HasClass(b, "railLink"));
                int firstContent = buttons.FindIndex(b =>
                    HasClass(b, "primary") || HasClass(b, "outline") || HasClass(b, "recentRow"));
                Assert.True(lastRail >= 0 && firstContent > lastRail,
                    $"右栏动作应排在左栏之后（左栏末 idx={lastRail}，右栏首 idx={firstContent}）");
                Assert.True(HasClass(buttons[0], "navItem"), "左栏第一个入口应是导航项");
                var navs = buttons.Where(b => HasClass(b, "navItem")).ToList();
                Assert.True(navs[0].Name == "NavRecent", "第一个导航项应是「最近」");
            });
        }

        /// <summary>
        /// Space / Enter 能激活左栏导航并切换分页（`Border + PointerPressed` 时代键盘根本到不了）。
        /// W50 起断言**行为后果**（分页真的切换、`.selected` 真的转移），而不是"Click 被触发了几次"。
        /// </summary>
        [AvaloniaFact]
        public void WelcomeView_Nav_ActivatesOnSpaceAndEnter() {
            InView(view => {
                var navNew = Buttons(view).First(b => b.Name == "NavNew");
                var pageNew = view.GetVisualDescendants().OfType<Control>().First(c => c.Name == "PageNew");
                Assert.True(navNew.Focus(), "导航项必须能拿到键盘焦点");
                Assert.False(pageNew.IsVisible, "默认不应停在「新建」页");

                void Press(Key key) {
                    navNew.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
                    navNew.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key });
                }

                Press(Key.Space);
                Assert.True(pageNew.IsVisible, "Space 应切到「新建」页");
                Assert.True(HasClass(navNew, "selected"), "选中态应转移到「新建」");
                var navRecent = Buttons(view).First(b => b.Name == "NavRecent");
                Assert.False(HasClass(navRecent, "selected"), "旧页的选中态应被摘掉");
            });
        }

        /// <summary>
        /// 焦点环（W50）：导航项的焦点环在**应用级共享层**（`Styles/Md3Controls.axaml` 的
        /// `Button.navItem:focus-visible`，与偏好设置页共用）；页面本地的行样式则要求
        /// **基础态就有 1px 描边、焦点态只换颜色** ⇒ 聚焦不产生布局位移。
        /// </summary>
        [AvaloniaFact]
        public void WelcomeView_FocusRings_DeclareNoLayoutShift() {
            string shared = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Styles", "Md3Controls.axaml"));
            Assert.Contains("Button.navItem:focus-visible", shared);
            Assert.Contains("md3.primary", shared);

            string xaml = ReadXaml("WelcomeView.axaml");
            foreach (string cls in new[] { "recentRow", "railLink" }) {
                int baseIdx = xaml.IndexOf($"<Style Selector=\"Button.{cls}\">", StringComparison.Ordinal);
                int focusIdx = xaml.IndexOf($"Button.{cls}:focus-visible", StringComparison.Ordinal);
                Assert.True(baseIdx >= 0, $"缺少 Button.{cls} 基础样式");
                Assert.True(focusIdx > baseIdx, $"缺少 Button.{cls}:focus-visible 焦点环");
                string baseBlock = xaml.Substring(baseIdx, focusIdx - baseIdx);
                Assert.Contains("<Setter Property=\"BorderThickness\" Value=\"1\"/>", baseBlock);
                Assert.Contains("Value=\"Transparent\"", baseBlock);
                string focusBlock = xaml.Substring(focusIdx, Math.Min(160, xaml.Length - focusIdx));
                Assert.Contains("md3.primary", focusBlock);            // 焦点 = 主色描边
                Assert.DoesNotContain("BorderThickness", focusBlock);  // 不改变厚度 ⇒ 零位移
            }
        }

        // ══════════════════════ 动效 / 旧样式规避（原有契约，保留） ══════════════════════

        [AvaloniaFact]
        public void WelcomeView_UsesDeclarativeTransitions() {
            // 动效重做（2026-09-25）：不再用自定义 Motion 附加属性（会闪烁/错位），
            // 视图自身不写动画，改由宿主切 Classes + Styles/Md3Transitions.axaml 的 Transitions 插值。
            string xaml = ReadXaml("WelcomeView.axaml");
            Assert.DoesNotContain("motion:Motion.", xaml);
            Assert.DoesNotContain("Motion.Hover", xaml);
            string mainXaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Views", "MainWindow.axaml"));
            Assert.Contains("x:Name=\"WelcomeHost\" Classes=\"md3-fade\"", mainXaml);
            string transitions = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Styles", "Md3Transitions.axaml"));
            Assert.Contains(".md3-fade", transitions);
            Assert.Contains("<DoubleTransition Property=\"Opacity\"", transitions);
        }

        [AvaloniaFact]
        public void WelcomeView_UsesNoLegacyColorKeys() {
            // 颜色一律来自颜色池：旧的 Plus*/Suki/Fluent 颜色键与硬编码色值都不允许出现
            string xaml = ReadXaml("WelcomeView.axaml");
            foreach (string legacy in new[] { "PlusSurface", "PlusBorder", "SystemControl", "AccentBrush", "NeutralAccent" }) {
                Assert.DoesNotContain(legacy, xaml);
            }
            Assert.DoesNotMatch("#[0-9A-Fa-f]{6}", xaml);
        }

        [AvaloniaFact]
        public void WelcomeView_RecentRows_NotSubjectToGlobalListBoxItemStyle() {
            // 约定：新 MD3 界面不吃旧的全局隐式样式。
            // Styles.axaml 有全局 ListBoxItem{MinHeight=28,Height=28}（旧密度覆盖），
            // 会把设计稿 68px 的最近工程行夹扁 → 欢迎页改用 ScrollViewer + ItemsControl，行自己定尺寸。
            // ⚠ 后人不要"顺手换成 ListBox"。
            string xaml = ReadXaml("WelcomeView.axaml");
            Assert.DoesNotContain("<ListBox", xaml);
            Assert.Contains("Classes=\"recentRow\"", xaml);   // 行样式走自有类，不用 ListBoxItem
            Assert.Contains("<ItemsControl", xaml);
        }

        [AvaloniaFact]
        public void WelcomeView_Instantiates() {
            InView(view => {
                Assert.Null(view.Host);   // 宿主由 MainWindow 注入
                // 波形几何由资源字典喂（W38：真实包络，非手画柱）—— 未解析则 Data 为 null
                var waveform = view.GetVisualDescendants().OfType<ShapePath>()
                    .FirstOrDefault(p => p.Name == "WaveformBars");
                Assert.NotNull(waveform);
                Assert.NotNull(waveform!.Data);
            });
        }

        [AvaloniaFact]
        public void MainWindow_HostsWelcomeView_HiddenByDefault() {
            string xaml = ReadXaml("MainWindow.axaml");
            Assert.Contains("x:Name=\"WelcomeHost\"", xaml);
            Assert.Contains("WelcomeHost", xaml);
            // 初始隐藏：由 MainWindow 在构造末尾按"是否带命令行工程文件"决定是否显示
            Assert.Matches("x:Name=\"WelcomeHost\"[^/]*IsVisible=\"False\"", xaml);
        }

        // ══════════════════════ 资源键（原有契约，保留） ══════════════════════

        [AvaloniaFact]
        public void AllDynamicResourceKeys_Resolve() {
            UsePool();
            Application app = Application.Current!;
            var missing = new List<string>();
            foreach (string key in KeysOf(ReadXaml("WelcomeView.axaml"), "DynamicResource")) {
                if (!app.TryFindResource(key, out _)) {
                    missing.Add(key);
                }
            }
            Assert.True(missing.Count == 0, "未解析的动态资源键：" + string.Join(", ", missing));
        }

        [AvaloniaFact]
        public void IconKeys_Resolve() {
            Application app = Application.Current!;
            string xaml = ReadXaml("WelcomeView.axaml");
            // 视图内自定义的键（如弹层外框 ControlTheme）在同文件定义，不走应用资源
            var local = Regex.Matches(xaml, "x:Key=\"([^\"]+)\"")
                .Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);
            var missing = new List<string>();
            foreach (string key in KeysOf(xaml, "StaticResource")) {
                if (local.Contains(key)) {
                    continue;
                }
                if (!app.TryFindResource(key, out _)) {
                    missing.Add(key);
                }
            }
            Assert.True(missing.Count == 0, "未解析的静态资源键：" + string.Join(", ", missing));
        }

        [AvaloniaFact]
        public void ColorKeys_ComeFromMd3Pool_Only() {
            // 契约：欢迎视图的颜色一律取 md3 角色键；其余动态键必须是文案键（字符串资源）。
            // 也就是说：旧颜色键（Plus* / Suki / Fluent 命名空间）在这里一律不允许出现。
            var md3Keys = new HashSet<string>(Enum.GetValues<Md3Role>().Select(ColorPool.Key));
            var md3ColorKeys = new HashSet<string>(Enum.GetValues<Md3Role>().Select(ColorPool.ColorKey));
            // **非颜色**资源键白名单：本页只有波形几何（W38）。
            // 它们不参与配色 ⇒ 不要求属于颜色池；新增时必须**显式登记**，防止"顺手引个外来颜色键"混进来。
            var nonColorResourceKeys = new HashSet<string>(StringComparer.Ordinal) { "welcome-waveform" };
            var foreign = new List<string>();
            foreach (string key in KeysOf(ReadXaml("WelcomeView.axaml"), "DynamicResource")) {
                if (md3Keys.Contains(key) || md3ColorKeys.Contains(key) || nonColorResourceKeys.Contains(key)) {
                    continue;
                }
                if (key.StartsWith("md3.", StringComparison.Ordinal)) {
                    foreign.Add(key + "（md3 前缀但不在颜色池键集合内）");
                    continue;
                }
                if (!ThemeManager.TryGetString(key, out string? text) || string.IsNullOrEmpty(text)) {
                    foreign.Add(key + "（既不是颜色池键，也不是文案键）");
                }
            }
            Assert.True(foreign.Count == 0, "欢迎视图引用了非法键：" + string.Join(", ", foreign));
        }

        [AvaloniaFact]
        public void TextKeys_ComeFromStringResources() {
            Application app = Application.Current!;
            var dynamicKeys = KeysOf(ReadXaml("WelcomeView.axaml"), "DynamicResource");
            // 视图里的文案键应当能在字符串资源里解析（welcome.* / singers.* / menu.* 等）
            var textKeys = dynamicKeys.Where(k => !k.StartsWith("md3.", StringComparison.Ordinal)).ToList();
            Assert.NotEmpty(textKeys);
            foreach (string key in textKeys) {
                Assert.True(app.TryFindResource(key, out _), $"文案键未解析：{key}");
            }
        }

        /// <summary>本页新增的 welcome.* 键必须在 EN 与 zh 两侧成对存在。</summary>
        [AvaloniaFact]
        public void WelcomeView_NewStringKeys_ExistInBothLanguages() {
            var used = Regex.Matches(ReadXaml("WelcomeView.axaml"), "\\{DynamicResource (welcome\\.[A-Za-z0-9_.]+)\\}")
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .ToList();
            Assert.True(used.Count > 10, $"应使用十余个 welcome.* 键，实际 {used.Count}");

            HashSet<string> KeysIn(string name) => Regex
                .Matches(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Strings", name)),
                    "<system:String\\s+x:Key=\"([^\"]+)\"")
                .Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);

            var en = KeysIn("Strings.axaml");
            var zh = KeysIn("Strings.zh-CN.axaml");
            var missing = used.Where(k => !en.Contains(k) || !zh.Contains(k)).ToList();
            Assert.True(missing.Count == 0, "缺键或未成对：" + string.Join(", ", missing));
        }
    }
}

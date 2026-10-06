using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

        // ══════════════════════ 设计几何（对齐 1-Welcome 稿） ══════════════════════

        [AvaloniaFact]
        public void WelcomeView_MatchesDesignGeometry() {
            string xaml = ReadXaml("WelcomeView.axaml");
            // 分栏：432 品牌栏（稿 480，因左栏不再自带 48 内边距而收窄）+ 自适应启动器
            Assert.Contains("ColumnDefinitions=\"432,*\"", xaml);
            // 品牌栏实底 = surface-container（稿的 teal 品牌底是独立色板，动态色池无对应角色）
            Assert.Contains("md3.surface-container", xaml);
            Assert.DoesNotContain("md3.primary-container", xaml);
            // 字号阶梯（稿值抽查）
            // 34px 英雄字号已按用户裁决删除（营销文案整块移除）⇒ 阶梯最高 30（右栏标题）
            foreach (int size in new[] { 30, 26, 18, 15, 14, 13, 12, 11 }) {
                // 属性形态或样式 Setter 形态都算（两种在本页都有使用）
                Assert.True(xaml.Contains($"FontSize=\"{size}\"", StringComparison.Ordinal)
                    || xaml.Contains($"<Setter Property=\"FontSize\" Value=\"{size}\"/>", StringComparison.Ordinal),
                    $"缺字号 {size}");
            }
            // 关键尺寸：动作卡 104/圆角 16、波形区 132、柱宽 6、拖放条 56、缩略图 48/圆角 8
            Assert.Contains("<Setter Property=\"Height\" Value=\"104\"/>", xaml);
            Assert.Contains("<Setter Property=\"CornerRadius\" Value=\"16\"/>", xaml);
            Assert.Contains("Height=\"132\"", xaml);   // 波形区（属性形态）
            Assert.Contains("<Setter Property=\"Height\" Value=\"56\"/>", xaml);
            Assert.Contains("<Setter Property=\"Width\" Value=\"6\"/>", xaml);
            Assert.Contains("CornerRadius=\"999\"", xaml);
            // 四张动作卡：新建 / 打开 / 导入音频 / 模板（**全部走 Button.Click**）
            foreach (string handler in new[] { "OnNewProject", "OnOpenProject", "OnImportAudio", "OnShowTemplates" }) {
                Assert.Contains($"Click=\"{handler}\"", xaml);
            }
            // MD3 铁律：无阴影、无玻璃（先剥注释——说明文字里会出现这些词）
            string code = Regex.Replace(xaml, "<!--.*?-->", "", RegexOptions.Singleline);
            Assert.DoesNotContain("BoxShadow", code);
            Assert.DoesNotContain("BlurEffect", code);
        }

        [AvaloniaFact]
        public void WelcomeView_Waveform_MatchesTheDesignSpec() {
            var bars = WelcomeArt.Waveform;
            Assert.Equal(26, bars.Count);                                  // 稿：26 根柱
            Assert.Equal(18, bars[0].Height, 1);
            Assert.Equal(62, bars[^1].Height, 1);
            Assert.Equal(122, bars.Max(b => b.Height), 1);                  // 最高柱
            Assert.Equal(1.0, bars.Max(b => b.Opacity), 3);
            Assert.All(bars, b => Assert.InRange(b.Opacity, 0.4, 1.0));
            Assert.All(bars, b => Assert.InRange(b.Height, 18, 132));
        }

        /// <summary>
        /// **布局层**断言：4 张动作卡在两个窗口尺寸下都真的渲染（`IsVisible` 且 `Bounds` 非零）。
        /// 上一轮"第 4 张卡（模板）没渲染"就是因为只断言了 `Click=` 处理器在座、没断言可见性与尺寸
        /// —— 当时模板卡带 `IsVisible="{Binding TemplateFiles.Count}"`，本机无模板 ⇒ 2×2 网格留洞。
        /// </summary>
        [AvaloniaTheory]
        [InlineData(1226.0, 699.0)]
        [InlineData(1000.0, 660.0)]
        public void WelcomeView_FourActionCards_AreLaidOut(double width, double height) {
            UsePool();
            var view = new WelcomeView();
            var window = new Window { Width = width, Height = height, Content = view };
            window.Show();
            try {
                Dispatcher.UIThread.RunJobs();
                var cards = Buttons(view).Where(b => HasClass(b, "actionCard")).ToList();
                Assert.Equal(4, cards.Count);
                for (int i = 0; i < cards.Count; i++) {
                    Button card = cards[i];
                    Assert.True(card.IsVisible, $"{width}x{height}: 第 {i + 1} 张动作卡不可见");
                    Assert.True(card.Bounds.Width > 0 && card.Bounds.Height > 0,
                        $"{width}x{height}: 第 {i + 1} 张动作卡无尺寸（{card.Bounds.Width}x{card.Bounds.Height}）");
                }
            } finally {
                window.Close();
            }
        }

        /// <summary>空态文案必须是专用键（此前复用 `welcome.open.description` ⇒ 空态显示"打开本地项目"）。</summary>
        [AvaloniaFact]
        public void WelcomeView_EmptyState_UsesItsOwnKey() {
            string xaml = ReadXaml("WelcomeView.axaml");
            Assert.Contains("welcome.recent.empty", xaml);
            Assert.Contains("welcome.template.empty", xaml);
            // 只针对**空态元素**：其 Text= 必须是专用键（动作卡副标题用 welcome.open.description 是合理的）
            var emptyState = Regex.Match(xaml,
                "Text=\"\\{DynamicResource (welcome\\.[A-Za-z0-9_.]+)\\}\"[\\s\\S]{0,200}?IsVisible=\"\\{Binding !RecentFiles\\.Count\\}\"");
            Assert.True(emptyState.Success, "找不到最近工程的空态元素");
            Assert.Equal("welcome.recent.empty", emptyState.Groups[1].Value);
            // 模板卡不得再按"有无模板"隐藏（否则留洞）
            Assert.DoesNotContain("IsVisible=\"{Binding TemplateFiles.Count}\"", xaml);
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
                Assert.Equal(6, buttons.Count(b => HasClass(b, "linkRow")));        // 左栏快捷入口
                Assert.Equal(4, buttons.Count(b => HasClass(b, "actionCard")));     // 动作卡 2×2
                Assert.Equal(1, buttons.Count(b => HasClass(b, "linkButton")));     // 查看全部
                Assert.All(buttons, b => Assert.True(b.Focusable, "入口必须可聚焦（Tab 可达）"));
            });
        }

        /// <summary>Tab 序 = 阅读序：左栏快捷入口在前，右栏动作卡在后；首张卡片是主行动。</summary>
        [AvaloniaFact]
        public void WelcomeView_TabOrder_FollowsReadingOrder() {
            InView(view => {
                var buttons = Buttons(view);
                int lastLinkRow = buttons.FindLastIndex(b => HasClass(b, "linkRow"));
                int firstCard = buttons.FindIndex(b => HasClass(b, "actionCard"));
                Assert.True(lastLinkRow >= 0 && firstCard > lastLinkRow,
                    $"动作卡应排在左栏快捷入口之后（linkRow 末 idx={lastLinkRow}，card 首 idx={firstCard}）");

                var cards = buttons.Where(b => HasClass(b, "actionCard")).ToList();
                Assert.True(HasClass(cards[0], "cardMain"), "第一张卡片应是主行动（新建工程）");
                Assert.False(HasClass(cards[1], "cardMain"));
            });
        }

        /// <summary>Space / Enter 能激活主行动卡（Border + PointerPressed 时代做不到）。</summary>
        [AvaloniaFact]
        public void WelcomeView_ActionCard_ActivatesOnSpaceAndEnter() {
            InView(view => {
                var card = Buttons(view).First(b => HasClass(b, "cardMain"));
                int clicks = 0;
                card.Click += (_, _) => clicks++;
                Assert.True(card.Focus(), "主行动卡必须能拿到键盘焦点");

                void Press(Key key) {
                    card.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
                    card.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key });
                }

                Press(Key.Space);
                int afterSpace = clicks;
                Press(Key.Enter);
                Assert.True(afterSpace >= 1, "Space 应激活动作卡");
                Assert.True(clicks > afterSpace, "Enter 应激活动作卡");
            });
        }

        /// <summary>
        /// 焦点环：声明 `:focus-visible`，且**基础态就有 1px 透明描边**、焦点态只换颜色
        /// ⇒ 聚焦不产生布局位移（Button 没有 BoxShadow 属性，不能用浮层焦点环）。
        /// </summary>
        [AvaloniaFact]
        public void WelcomeView_FocusRing_IsDeclaredWithoutLayoutShift() {
            string xaml = ReadXaml("WelcomeView.axaml");
            int baseIdx = xaml.IndexOf("<Style Selector=\"Button.welcome\">", StringComparison.Ordinal);
            int focusIdx = xaml.IndexOf("Button.welcome:focus-visible", StringComparison.Ordinal);
            Assert.True(baseIdx >= 0, "缺少 Button.welcome 基础样式");
            Assert.True(focusIdx > baseIdx, "缺少 Button.welcome:focus-visible 焦点环");

            string baseBlock = xaml.Substring(baseIdx, focusIdx - baseIdx);
            Assert.Contains("<Setter Property=\"BorderThickness\" Value=\"1\"/>", baseBlock);
            Assert.Contains("Value=\"Transparent\"", baseBlock);

            string focusBlock = xaml.Substring(focusIdx, Math.Min(200, xaml.Length - focusIdx));
            Assert.Contains("md3.primary", focusBlock);          // 焦点 = 主色描边
            Assert.DoesNotContain("BorderThickness", focusBlock); // 不改变厚度 ⇒ 零位移
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
            Assert.Contains("Classes=\"welcome recentRow\"", xaml);   // 行样式走自有类，不用 ListBoxItem
            Assert.Contains("<ItemsControl", xaml);
        }

        [AvaloniaFact]
        public void WelcomeView_Instantiates() {
            InView(view => {
                Assert.Null(view.Host);   // 宿主由 MainWindow 注入
                // 几何波形与音源 chips 由视图自己喂数据（不依赖 VM 构造顺序）
                var waveform = view.GetVisualDescendants().OfType<ItemsControl>()
                    .FirstOrDefault(c => c.Name == "WaveformBars");
                Assert.NotNull(waveform);
                Assert.Equal(26, (waveform!.ItemsSource as System.Collections.IEnumerable)?.Cast<object>().Count() ?? 0);
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
            var foreign = new List<string>();
            foreach (string key in KeysOf(ReadXaml("WelcomeView.axaml"), "DynamicResource")) {
                if (md3Keys.Contains(key) || md3ColorKeys.Contains(key)) {
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

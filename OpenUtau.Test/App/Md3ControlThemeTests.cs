using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using OpenUtau.App;
using OpenUtau.App.Controls;
using OpenUtau.Core.Theming;
using OpenUtau.Theming;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// 控件规范与状态色契约（2026-09-25 用户反馈「悬浮背景闪一下 / 选中不变色 / 圆角边距颜色不统一」后重做）：
    ///
    /// 规则：控件外观由**自己的 ControlTheme** 提供（模板只做 TemplateBinding，状态写在主题里），
    /// 严禁在样式层用 `X /template/ Y` 去改模板部件 —— 那会与主题 setter 抢同一个属性，
    /// 表现为悬浮闪烁、选中态不生效。本文件把这条规则和状态色一起钉住。
    /// </summary>
    [Collection("Theme")]
    public class Md3ControlThemeTests {
        /// <summary>样式引用的池画刷（md3.* 资源键）——与控件属性同源比较，天然与主题变体无关。</summary>
        private static Color PoolBrush(string key) {
            Assert.True(Application.Current!.TryFindResource(key, out object? value), $"池画刷未解析：{key}");
            return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
        }

        /// <summary>颜色池当前变体下的角色色（跟随应用实际深浅，避免把变体写死）。</summary>
        private static Color Role(Md3Role role) => ColorPool.Current.Color(role);

        private static WindowEx Host(Control content, bool? isDark = null) {
            // 与应用当前变体对齐（否则颜色池是深色、而样式引用的是浅色资源，断言必然对不上）；
            // 传 isDark 时按指定变体建，供「两个变体各断言一次」的确定性用例使用（T8-B）。
            ColorPool.Initialize(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, isDark ?? ThemeManager.IsDarkMode);
            var win = new WindowEx { Width = 400, Height = 200, Content = content };
            win.Show();
            content.ApplyTemplate();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            return win;
        }

        private static Border? TemplateRoot(Control c) =>
            c.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "PART_Root");

        private static void SetPseudo(Control c, string pseudo, bool on) {
            ((IPseudoClasses)c.Classes).Set(pseudo, on);
            // 伪类改变后需要让样式系统跑一轮，否则读到的是旧值
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        /// <summary>取应用资源里的画刷颜色（TryFindResource 是已验证可用的取法；FindResource 会返回 UnsetValue）。</summary>
        private static Color ResourceColor(string key) {
            Assert.True(Application.Current!.TryFindResource(key, out object? value), $"资源键未解析：{key}");
            return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
        }

        private static Color? Bg(Control? c) => c switch {
            Border b => (b.Background as ISolidColorBrush)?.Color,
            Avalonia.Controls.Primitives.TemplatedControl t => (t.Background as ISolidColorBrush)?.Color,
            _ => null,
        };

        [AvaloniaFact]
        public void Button_UsesOurControlTheme() {
            var btn = new Button { Content = "ok" };
            var win = Host(btn);
            try {
                // 我们的 ControlTheme 已装上：模板根为 PART_Root，规格与池色都来自主题
                Assert.NotNull(btn.Theme);
                Assert.Equal(typeof(Button), btn.Theme!.TargetType);
                Assert.NotNull(TemplateRoot(btn));
                Assert.Equal(new CornerRadius(8), btn.CornerRadius);
                Assert.Equal(new Thickness(14, 6, 14, 6), btn.Padding);
                Assert.Equal(32, btn.MinHeight);
                Assert.Equal(13, btn.FontSize);
                // T9-B 契约更新：base 标签色从 md3.primary 改成 md3.on-surface。
                // 理由：那个 md3.primary 在转发修复前**从未真正渲染过**（一直被应用级
                // TextBlock 前景压住，用户实际看到的普通按钮文字就是 on-surface）；
                // 转发修好后若仍用 primary，全 App 普通按钮标签会一起变 indigo —— 属
                // 需要用户先过目的视觉决策（MD3 规范确实是 primary），故本轮保持既有观感。
                Assert.Equal(PoolBrush("md3.on-surface"), (btn.Foreground as ISolidColorBrush)?.Color);
                Assert.Equal(PoolBrush("md3.outline-variant"), (btn.BorderBrush as ISolidColorBrush)?.Color);
            } finally {
                win.Close();
            }
            // 状态写在主题内部（单一来源）：悬浮/按下由主题的嵌套样式提供
            string theme = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Styles", "Md3ControlThemes.axaml"));
            Assert.Contains("x:Key=\"Md3ButtonTheme\"", theme);
            Assert.Contains("Selector=\"^:pointerover\"", theme);
            Assert.Contains("Selector=\"^:pressed\"", theme);
            Assert.Contains("Selector=\"^.primary\"", theme);
        }

        /// <summary>
        /// 主按钮实心胶囊：**两个变体各断言一次**（T8-B）。
        ///
        /// 历史坑：断言曾隐含依赖「进程当前深浅色」——浅色池的 on-primary 恰好等于旧的应用级
        /// 覆盖色 #ffffff，于是浅色下过、深色下挂，表现成看执行顺序的 flaky。
        /// 现在改为显式遍历 Light/Dark 两个池，并额外钉住「深色池下前景绝不能再是白色」。
        /// </summary>
        [AvaloniaFact]
        public void Button_PrimaryVariant_IsFilledPill_InBothVariants() {
            bool original = ColorPool.Current.IsDark;
            try {
                foreach (bool isDark in new[] { false, true }) {
                    var btn = new Button { Content = "save", Classes = { "primary" } };
                    var win = Host(btn, isDark);
                    try {
                        Assert.Equal(isDark, ColorPool.Current.IsDark);
                        Assert.Equal(new CornerRadius(999), btn.CornerRadius);
                        Assert.Equal(PoolBrush("md3.primary"), Bg(btn));
                        Assert.Equal(PoolBrush("md3.on-primary"), (btn.Foreground as ISolidColorBrush)?.Color);
                        Assert.Equal(ColorPool.Current.Color(Md3Role.Primary), Bg(btn));
                        Assert.Equal(ColorPool.Current.Color(Md3Role.OnPrimary), (btn.Foreground as ISolidColorBrush)?.Color);
                        if (isDark) {
                            // 深色池 on-primary 是深色：若哪天又被固定白色压住，这里必须炸
                            Assert.NotEqual(Avalonia.Media.Colors.White, (btn.Foreground as ISolidColorBrush)?.Color);
                        }
                    } finally {
                        win.Close();
                    }
                }
            } finally {
                ColorPool.SetDark(original);   // 归还全局状态，避免污染后续用例
            }
        }

        [AvaloniaFact]
        public void ListBoxItem_UsesOurControlTheme() {
            var item = new ListBoxItem { Content = "row", IsSelected = true };
            var win = Host(item);
            try {
                // 主题归属用 Theme 属性判定（确定性）；模板与几何由 ControlThemeMountProbe 实证
                Assert.NotNull(item.Theme);
                Assert.Equal(typeof(ListBoxItem), item.Theme!.TargetType);
                Assert.Equal(PoolBrush("md3.secondary-container"), Bg(item));
            } finally {
                win.Close();
            }
            string theme = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Styles", "Md3ControlThemes.axaml"));
            Assert.Contains("x:Key=\"Md3ListBoxItemTheme\"", theme);
            Assert.Contains("Selector=\"^:selected\"", theme);
        }

        [AvaloniaFact]
        public void AppStyleLayers_NeverPatchForeignTemplateParts() {
            // 规则：应用级样式文件里禁止 `X /template/ Y` —— 那是从外面改**别的主题**的模板部件，
            // 会与该主题的 setter 抢同一个属性（悬浮闪烁、选中态不生效）。
            // 注意：在自己的 ControlTheme 内部用 `^:state /template/ 部件` 是正规写法，允许。
            foreach (string file in new[] { "Md3Controls.axaml", "Md3Transitions.axaml" }) {
                string xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Styles", file));
                var offenders = Regex.Matches(xaml, "Selector=\"([^\"]*/template/[^\"]*)\"")
                    .Select(m => m.Groups[1].Value).ToList();
                Assert.True(offenders.Count == 0, $"{file} 不应从样式层改模板部件：{string.Join(" | ", offenders)}");
            }
        }

        [AvaloniaFact]
        public void AppearanceLivesInAppStyles_AndFluentKeysPointAtPool() {
            // 实测结论（本轮）：应用级 Styles 是唯一稳定生效的位置 —— 隐式 ControlTheme 会被 FluentTheme 抢先命中，
            // 显式 Setter Theme 也不生效。因此外观写在 Styles/Md3Controls.axaml，
            // 模板部件的状态色则通过覆盖 Fluent 画刷键落到颜色池（本文件同时校验两者）。
            string xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Styles", "Md3Controls.axaml"));
            // 按钮/列表项外观在 ControlTheme；应用级样式负责输入类与容器类
            foreach (string sel in new[] { "TextBox", "ComboBox", "CheckBox, RadioButton", "ToggleSwitch", "ListBox", "Slider" }) {
                Assert.Contains($"Selector=\"{sel}\"", xaml);
            }
            // 2026-10 契约更新：选择类控件（CheckBox / RadioButton / ToggleSwitch / Slider / ProgressBar）
            // 不再靠"覆盖 Fluent 画刷键"取巧上色，而是接管**自有 ControlTheme**
            //（Styles/Md3SelectionThemes.axaml，模板 + 状态色全由我们定义，颜色只取 md3 池角色）。
            // Md3Controls 里保留的只剩几何规格（字号/最小高度/内边距），状态色不再由 Fluent 决定。
            string selection = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Styles", "Md3SelectionThemes.axaml"));
            foreach (string type in new[] { "CheckBox", "RadioButton", "ToggleSwitch", "Slider", "ProgressBar" }) {
                Assert.Contains($"TargetType=\"{type}\"", selection);            // 自有 ControlTheme
                Assert.Contains($"<Style Selector=\"{type}\">", selection);      // 同文件挂载
            }
            // 说明：Fluent 主题键（含画刷）与隐式 ControlTheme 一样无法从外部覆盖（先注册者优先），
            // 故**仍在用 Fluent 模板**的控件（TextBox/ComboBox/ListBox 等）的外观只能写在控件级属性上。
        }

        // ───────────────────────── T9：文字色转发（渲染层） ─────────────────────────

        /// <summary>渲染出来的内容文本元素：字符串内容由 ContentPresenter 生成（RecognizesAccessKey 下为 AccessText）。</summary>
        private static TextBlock RenderedContent(Control c) =>
            c.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()
            ?? throw new Xunit.Sdk.XunitException($"{c.GetType().Name} 没有生成内容文本元素");

        /// <summary>WCAG 对比度（相对亮度比），纯计算、不依赖像素。</summary>
        private static double Contrast(Color a, Color b) {
            static double Ch(byte v) {
                double s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            static double Lum(Color c) => 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
            double la = Lum(a), lb = Lum(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        /// <summary>
        /// T9：`Button.Foreground` 必须真的转发到**渲染出来的内容文本**（不是只到 ContentPresenter）。
        ///
        /// 缺陷背景（像素级实证）：模板的 ContentPresenter 没绑 Foreground，且应用级
        /// `TextBlock { Foreground = TextFillColorPrimaryBrush }` 会压过继承 ⇒ `^.primary` 的
        /// `md3.on-primary` 在渲染层从未生效（机架「确定」实测白字叠浅紫，约 1.28:1）。
        /// 修法两条腿：ContentPresenter 补 `Foreground="{TemplateBinding Foreground}"`，
        /// 并加 `RecognizesAccessKey="True"`（字符串内容改由 AccessText 承载 ⇒ 不再被 TextBlock
        /// 规则拦下；这也正是 Fluent 原生 Button 的写法）。
        /// </summary>
        [AvaloniaFact]
        public void Button_ForegroundIsForwardedToRenderedContent_InBothVariants() {
            bool original = ColorPool.Current.IsDark;
            try {
                foreach (bool isDark in new[] { false, true }) {
                    var cases = new (string Name, string[] Classes, Md3Role Expect)[] {
                        ("base", Array.Empty<string>(), Md3Role.OnSurface),
                        ("primary", new[] { "primary" }, Md3Role.OnPrimary),
                        ("outline", new[] { "outline" }, Md3Role.OnSurface),
                        ("danger", new[] { "danger" }, Md3Role.Error),
                    };
                    foreach (var (name, classes, expect) in cases) {
                        var btn = new Button { Content = "确定" };
                        foreach (string cls in classes) {
                            btn.Classes.Add(cls);
                        }
                        var win = Host(btn, isDark);
                        try {
                            TextBlock content = RenderedContent(btn);
                            Assert.Equal("AccessText", content.GetType().Name);          // RecognizesAccessKey 的产物
                            Assert.Equal((btn.Foreground as ISolidColorBrush)?.Color,
                                         (content.Foreground as ISolidColorBrush)?.Color); // 转发生效
                            Assert.Equal(ColorPool.Current.Color(expect),
                                         (content.Foreground as ISolidColorBrush)?.Color); // 且是该变体的池角色
                        } finally {
                            win.Close();
                        }
                    }

                    // 缺陷回归守卫：filled primary 的文字绝不能还是 on-surface（原 bug 的现象）
                    var pill = new Button { Content = "确定", Classes = { "primary" } };
                    var pillWin = Host(pill, isDark);
                    try {
                        Color text = Assert.IsAssignableFrom<ISolidColorBrush>(RenderedContent(pill).Foreground).Color;
                        Assert.Equal(ColorPool.Current.Color(Md3Role.OnPrimary), text);
                        Assert.NotEqual(ColorPool.Current.Color(Md3Role.OnSurface), text);
                    } finally {
                        pillWin.Close();
                    }

                    // linkButton：强调色由应用级规则给（不是转发值），TextBlock/AccessText 两种内容元素都要覆盖到
                    var link = new Button { Content = "打开日志文件夹", Classes = { "linkButton" } };
                    var linkWin = Host(link, isDark);
                    try {
                        TextBlock content = RenderedContent(link);
                        Assert.Equal(ResourceColor("AccentBrush1"), (content.Foreground as ISolidColorBrush)?.Color);
                        Assert.NotNull(content.TextDecorations);
                        Assert.NotEmpty(content.TextDecorations!);
                    } finally {
                        linkWin.Close();
                    }
                }
            } finally {
                ColorPool.SetDark(original);
            }
        }

        /// <summary>
        /// T9-B：标签对比度下限（WCAG 4.5:1），两个池各算一次。数值即报告里的「两池实测对比度」。
        /// 也是 `Button.danger` 等当前无 XAML 使用者的变体的对比度守卫（headless 覆盖）。
        /// </summary>
        [AvaloniaFact]
        public void Button_LabelContrast_MeetsMinimum_InBothPools() {
            bool original = ColorPool.Current.IsDark;
            try {
                foreach (bool isDark in new[] { false, true }) {
                    ColorPool.Initialize(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, isDark);
                    string pool = isDark ? "Dark" : "Light";
                    Color surface = ColorPool.Current.Color(Md3Role.Surface);
                    double baseLabel = Contrast(ColorPool.Current.Color(Md3Role.OnSurface), surface);
                    double filledLabel = Contrast(ColorPool.Current.Color(Md3Role.OnPrimary), ColorPool.Current.Color(Md3Role.Primary));
                    double dangerLabel = Contrast(ColorPool.Current.Color(Md3Role.Error), surface);
                    double dangerHoverLabel = Contrast(ColorPool.Current.Color(Md3Role.OnError), ColorPool.Current.Color(Md3Role.Error));
                    Assert.True(baseLabel >= 4.5, $"{pool} 普通按钮标签对比度不足：{baseLabel:0.00}");
                    Assert.True(filledLabel >= 4.5, $"{pool} filled primary 标签对比度不足：{filledLabel:0.00}");
                    Assert.True(dangerLabel >= 4.5, $"{pool} danger 标签对比度不足：{dangerLabel:0.00}");
                    Assert.True(dangerHoverLabel >= 4.5, $"{pool} danger 悬浮标签对比度不足：{dangerHoverLabel:0.00}");
                }
            } finally {
                ColorPool.SetDark(original);
            }
        }
    }
}

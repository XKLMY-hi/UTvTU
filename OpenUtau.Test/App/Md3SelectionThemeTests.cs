using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App;
using OpenUtau.App.Controls;
using OpenUtau.Core.Theming;
using OpenUtau.Theming;
using Xunit;
using IOPath = System.IO.Path;
using PathShape = Avalonia.Controls.Shapes.Path;

namespace OpenUtau.Test.App {
    /// <summary>
    /// 选择类控件自有 ControlTheme 契约（2026-10 新控件语言）：
    /// CheckBox / RadioButton（普通形态）/ ToggleSwitch / Slider / ProgressBar。
    ///
    /// 规则：外观由**我们自己的 ControlTheme** 提供（模板 + 状态色），
    /// 颜色只取颜色池角色（md3.* 画刷 / md3.color.* 颜色），禁止硬编码色值。
    /// 本文件既验证"挂上了"（Theme 归属 + 模板部件 + 真实几何），
    /// 也验证"状态真的变了"（勾选/半选/禁用/不定态 + 开关位移）。
    /// </summary>
    [Collection("Theme")]
    public class Md3SelectionThemeTests {
        private const string ThemeFile = "Md3SelectionThemes.axaml";

        private static string ThemeXaml() =>
            System.IO.File.ReadAllText(IOPath.Combine(AppContext.BaseDirectory, "Styles", ThemeFile));

        private static string AppXaml() =>
            System.IO.File.ReadAllText(IOPath.Combine(AppContext.BaseDirectory, "App.axaml"));

        /// <summary>池画刷颜色（与控件属性同源比较，天然与深浅色变体无关）。</summary>
        private static Color PoolBrush(string key) {
            Assert.True(Application.Current!.TryFindResource(key, out object? value), $"池画刷未解析：{key}");
            return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
        }

        private static Color BgColor(Control? c) => c switch {
            Border b => (b.Background as ISolidColorBrush)?.Color ?? default,
            Ellipse e => (e.Fill as ISolidColorBrush)?.Color ?? default,
            TemplatedControl t => (t.Background as ISolidColorBrush)?.Color ?? default,
            Panel p => (p.Background as ISolidColorBrush)?.Color ?? default,
            _ => default,
        };

        /// <summary>把控件放进应用真实样式环境（App.axaml 已加载）并跑完样式/模板/布局。</summary>
        private static WindowEx Host(Control content) {
            ColorPool.Initialize(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, ThemeManager.IsDarkMode);
            var win = new WindowEx { Width = 320, Height = 160, Content = content };
            win.Show();
            Dispatcher.UIThread.RunJobs();
            content.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();
            return win;
        }

        private static T Part<T>(Control c, string name) where T : Control =>
            c.GetVisualDescendants().OfType<T>().FirstOrDefault(x => x.Name == name)
            ?? throw new Xunit.Sdk.XunitException($"模板部件缺失：{typeof(T).Name}#{name}（{c.GetType().Name}）");

        private static Control? TryPart(Control c, string name) =>
            c.GetVisualDescendants().OfType<Control>().FirstOrDefault(x => x.Name == name);

        // ───────────────────────── 挂载 ─────────────────────────

        [AvaloniaFact]
        public void SelectionControls_AllMountOurOwnTheme() {
            var controls = new Control[] {
                new CheckBox { Content = "勾" },
                new RadioButton { Content = "选" },
                new ToggleSwitch { Content = "开关" },
                new Slider { Width = 200, Minimum = 0, Maximum = 10, Value = 4 },
                new ProgressBar { Width = 200, Minimum = 0, Maximum = 100, Value = 40 },
            };
            var win = new WindowEx { Width = 320, Height = 320, Content = new StackPanel { Children = { controls[0], controls[1], controls[2], controls[3], controls[4] } } };
            win.Show();
            Dispatcher.UIThread.RunJobs();
            try {
                // 每类控件的特征部件：证明跑的是我们的模板，而不是 Fluent 的
                var markers = new (string Name, Type PartType)[] {
                    ("NormalRectangle", typeof(Border)),
                    ("OuterEllipse", typeof(Ellipse)),
                    ("OuterBorder", typeof(Border)),
                    ("PART_Track", typeof(Track)),
                    ("PART_Indicator", typeof(Border)),
                };
                for (int i = 0; i < controls.Length; i++) {
                    Control c = controls[i];
                    c.ApplyTemplate();
                    Dispatcher.UIThread.RunJobs();
                    Assert.NotNull(c.Theme);                                   // 真的挂上了（不是只写不挂）
                    Assert.Equal(c.GetType(), c.Theme!.TargetType);
                    Assert.Equal(markers[i].PartType, Part<Control>(c, markers[i].Name).GetType());
                    Assert.True(c.Bounds.Width > 0 && c.Bounds.Height > 0, $"{c.GetType().Name} 未参与布局");
                }
            } finally {
                win.Close();
            }
        }

        [AvaloniaFact]
        public void ThemeKeys_AreDeclaredAndMountedInSameFile() {
            string xaml = ThemeXaml();
            foreach (string key in new[] {
                "Md3CheckBoxTheme", "Md3RadioButtonTheme", "Md3ToggleSwitchTheme",
                "Md3SliderTheme", "Md3ProgressBarTheme",
            }) {
                Assert.Contains($"x:Key=\"{key}\"", xaml);
                // 同文件挂载范式：跨字典 StaticResource 解析不到，必须同文件
                Assert.Contains($"Value=\"{{StaticResource {key}}}\"", xaml);
            }
            // 应用级注册，且排在其它样式层之后（应用级样式后注册者优先级更高）
            string app = AppXaml();
            int mine = app.IndexOf("Styles/Md3SelectionThemes.axaml", StringComparison.Ordinal);
            Assert.True(mine > 0, "App.axaml 未注册 Md3SelectionThemes.axaml");
            foreach (string earlier in new[] {
                "Styles/Md3InputThemes.axaml", "Styles/Md3Menus.axaml",
                "Styles/Md3Controls.axaml", "Styles/Md3ControlThemes.axaml",
            }) {
                int other = app.IndexOf(earlier, StringComparison.Ordinal);
                Assert.True(other >= 0 && other < mine, $"{earlier} 必须排在选择类控件主题之前");
            }
        }

        [AvaloniaFact]
        public void ThemeFile_UsesOnlyPoolColors() {
            string xaml = ThemeXaml();
            Assert.DoesNotMatch("#[0-9A-Fa-f]{6}", xaml);          // 禁止硬编码色值
            Assert.DoesNotContain("Plus", xaml.Replace("PlusInfo", ""));
            // 每个 DynamicResource 都必须是颜色池键（md3.<角色> / md3.color.<角色>）
            var keys = Regex.Matches(xaml, @"\{DynamicResource ([^}]+)\}")
                .Select(m => m.Groups[1].Value.Trim()).Distinct().ToList();
            Assert.NotEmpty(keys);
            var offenders = keys.Where(k => !k.StartsWith("md3.", StringComparison.Ordinal)).ToList();
            Assert.True(offenders.Count == 0, "非颜色池资源键：" + string.Join(", ", offenders));
            // 画刷键必须真的在色池里（防拼错角色名导致静默无色）
            foreach (string key in keys) {
                Assert.True(Application.Current!.TryFindResource(key, out _), $"池资源未解析：{key}");
            }
        }

        [AvaloniaFact]
        public void OtherToggleTypes_AreNotHijacked() {
            // 应用级 Style 的类型选择器不得串到派生类型（否则 ToggleButton/Button 会被套上勾选框外观）
            var toggle = new ToggleButton { Content = "t" };
            var button = new Button { Content = "b" };
            var win = new WindowEx { Width = 300, Height = 120, Content = new StackPanel { Children = { toggle, button } } };
            win.Show();
            Dispatcher.UIThread.RunJobs();
            try {
                toggle.ApplyTemplate();
                button.ApplyTemplate();
                Assert.NotEqual(typeof(CheckBox), toggle.Theme?.TargetType);
                Assert.Null(TryPart(toggle, "NormalRectangle"));
                Assert.Equal(typeof(Button), button.Theme!.TargetType);      // 既有 Button 主题不受影响
            } finally {
                win.Close();
            }
        }

        // ───────────────────────── CheckBox ─────────────────────────

        [AvaloniaFact]
        public void CheckBox_StatesUsePoolColors() {
            var box = new CheckBox { Content = "选项" };
            var win = Host(box);
            try {
                var rect = Part<Border>(box, "NormalRectangle");
                var glyph = Part<PathShape>(box, "CheckGlyph");
                var dash = Part<Border>(box, "IndeterminateDash");
                // headless 没有渲染时钟，过渡会停在中间值 → 取即时值再断言状态色
                rect.Transitions = null;

                // 未选：2px outline 描边、透明底、勾隐藏
                Assert.Equal(new CornerRadius(4), rect.CornerRadius);
                Assert.Equal(new Thickness(2), rect.BorderThickness);
                Assert.Equal(PoolBrush("md3.outline"), (rect.BorderBrush as ISolidColorBrush)?.Color);
                Assert.Equal(0d, glyph.Opacity);
                Assert.Equal(new Size(18, 18), rect.Bounds.Size);

                // 选中：primary 实底、无描边、on-primary 勾
                box.IsChecked = true;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(PoolBrush("md3.primary"), BgColor(rect));
                Assert.Equal(new Thickness(0), rect.BorderThickness);
                Assert.Equal(1d, glyph.Opacity);
                Assert.Equal(PoolBrush("md3.on-primary"), (glyph.Fill as ISolidColorBrush)?.Color);
                Assert.Equal(0d, dash.Opacity);

                // 半选：primary 实底 + 横杠
                box.IsChecked = null;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(PoolBrush("md3.primary"), BgColor(rect));
                Assert.Equal(1d, dash.Opacity);
                Assert.Equal(0d, glyph.Opacity);
                Assert.Equal(PoolBrush("md3.on-primary"), (dash.Background as ISolidColorBrush)?.Color);

                // 禁用：整体降不透明度
                box.IsEnabled = false;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(0.38, box.Opacity, 3);
            } finally {
                win.Close();
            }
        }

        [AvaloniaFact]
        public void CheckBox_MenuVariant_KeepsGlyphOnlyLook() {
            // Styles.axaml 的 `CheckBox.menu` 与钢琴卷帘菜单按 /template/ Border#NormalRectangle、
            // Path#CheckGlyph 打补丁（菜单里只显示勾、不显示框）。自有模板必须保住这条既有契约。
            var box = new CheckBox { Content = "菜单项", Classes = { "menu" }, IsChecked = true };
            var win = Host(box);
            try {
                var rect = Part<Border>(box, "NormalRectangle");
                Assert.Equal(new Thickness(0), rect.BorderThickness);          // 框不可见
                Assert.Equal(Avalonia.Media.Colors.Transparent, BgColor(rect));
                Assert.Equal(1d, Part<PathShape>(box, "CheckGlyph").Opacity);        // 勾仍可见
            } finally {
                win.Close();
            }
        }

        // ───────────────────────── RadioButton ─────────────────────────

        [AvaloniaFact]
        public void RadioButton_StatesUsePoolColors() {
            var radio = new RadioButton { Content = "选项" };
            var win = Host(radio);
            try {
                var ring = Part<Ellipse>(radio, "OuterEllipse");
                var dot = Part<Ellipse>(radio, "CheckGlyph");
                ring.Transitions = null;   // 同上：headless 无渲染时钟
                Assert.Equal(new Size(18, 18), ring.Bounds.Size);
                Assert.Equal(PoolBrush("md3.outline"), (ring.Stroke as ISolidColorBrush)?.Color);
                Assert.Equal(0d, dot.Opacity);

                radio.IsChecked = true;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(PoolBrush("md3.primary"), (ring.Stroke as ISolidColorBrush)?.Color);
                Assert.Equal(1d, dot.Opacity);
                Assert.Equal(new Size(10, 10), dot.Bounds.Size);
                Assert.Equal(PoolBrush("md3.primary"), BgColor(dot));

                radio.IsEnabled = false;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(0.38, radio.Opacity, 3);
            } finally {
                win.Close();
            }
        }

        // ───────────────────────── ToggleSwitch ─────────────────────────

        [AvaloniaFact]
        public void ToggleSwitch_StatesAndKnobTravel() {
            var toggle = new ToggleSwitch { Content = "标题", OnContent = "开", OffContent = "关" };
            var win = Host(toggle);
            try {
                var trackOff = Part<Border>(toggle, "OuterBorder");
                var trackOn = Part<Border>(toggle, "SwitchKnobBounds");
                var knob = Part<Canvas>(toggle, "PART_SwitchKnob");
                var moving = Part<Grid>(toggle, "PART_MovingKnobs");
                var knobOn = Part<Ellipse>(toggle, "SwitchKnobOn");
                var knobOff = Part<Ellipse>(toggle, "SwitchKnobOff");
                // headless 无渲染时钟：过渡会停在中间值，先关掉再断言
                trackOff.Transitions = null;
                trackOn.Transitions = null;
                knobOn.Transitions = null;
                knobOff.Transitions = null;
                moving.Transitions = null;

                // 几何：轨道 40×20、圆钮画布 20×20 ⇒ 位移协议 40-20 = 20（改尺寸会让开关"跑出去"）
                Assert.Equal(new Size(40, 20), trackOff.Bounds.Size);
                Assert.Equal(new Size(20, 20), knob.Bounds.Size);

                // 关：关态轨道可见、开态底与开钮隐藏、文案显示 Off
                Assert.Equal(1d, trackOff.Opacity);
                Assert.Equal(PoolBrush("md3.surface-container-highest"), BgColor(trackOff));
                Assert.Equal(PoolBrush("md3.outline"), (trackOff.BorderBrush as ISolidColorBrush)?.Color);
                Assert.Equal(0d, trackOn.Opacity);
                Assert.Equal(1d, knobOff.Opacity);
                Assert.Equal(0d, knobOn.Opacity);
                Assert.Equal(0d, Canvas.GetLeft(moving));
                Assert.Equal(0d, Part<ContentPresenter>(toggle, "PART_OnContentPresenter").Opacity);
                Assert.Equal(1d, Part<ContentPresenter>(toggle, "PART_OffContentPresenter").Opacity);

                // 开：primary 轨道 + on-primary 圆钮，圆钮位移 20
                toggle.IsChecked = true;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(0d, trackOff.Opacity);
                Assert.Equal(1d, trackOn.Opacity);
                Assert.Equal(PoolBrush("md3.primary"), BgColor(trackOn));
                Assert.Equal(1d, knobOn.Opacity);
                Assert.Equal(PoolBrush("md3.on-primary"), BgColor(knobOn));
                Assert.Equal(0d, knobOff.Opacity);
                Assert.Equal(20d, Canvas.GetLeft(moving));
                Assert.Equal(1d, Part<ContentPresenter>(toggle, "PART_OnContentPresenter").Opacity);

                // 来回切（状态机可反复）
                toggle.IsChecked = false;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(0d, Canvas.GetLeft(moving));
                Assert.Equal(PoolBrush("md3.outline"), BgColor(knobOff));
            } finally {
                win.Close();
            }
        }

        // ───────────────────────── Slider ─────────────────────────

        [AvaloniaFact]
        public void Slider_KeepsCodeBehindPartsAndPoolColors() {
            var slider = new Slider { Width = 200, Minimum = 0, Maximum = 10, Value = 4 };
            var win = Host(slider);
            try {
                // 代码后置依赖的部件：点击定位 / 拖动 / 值同步
                var track = Part<Track>(slider, "PART_Track");
                var decrease = Part<RepeatButton>(slider, "PART_DecreaseButton");
                var increase = Part<RepeatButton>(slider, "PART_IncreaseButton");
                var thumb = Part<Thumb>(slider, "thumb");
                Assert.Equal(4, track.Value, 3);

                // 颜色全部来自颜色池：已选 = primary、未选 = surface-container-highest、圆钮 = primary
                Assert.Equal(PoolBrush("md3.primary"), BgColor(decrease));
                Assert.Equal(PoolBrush("md3.surface-container-highest"), BgColor(increase));
                Assert.Equal(PoolBrush("md3.primary"), BgColor(thumb));
                Assert.Equal(new Size(16, 16), thumb.Bounds.Size);

                // 值仍双向同步到 Track（模板没截断绑定）
                slider.Value = 7;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(7, track.Value, 3);
            } finally {
                win.Close();
            }
        }

        // ───────────────────────── ProgressBar ─────────────────────────

        [AvaloniaFact]
        public void ProgressBar_DeterminateAndIndeterminate() {
            var bar = new ProgressBar { Width = 200, Minimum = 0, Maximum = 100, Value = 40 };
            var win = Host(bar);
            try {
                var indicator = Part<Border>(bar, "PART_Indicator");
                Assert.Equal(80, indicator.Width, 1);                       // 40% × 200
                Assert.Equal(PoolBrush("md3.primary"), BgColor(indicator));
                Assert.Equal(PoolBrush("md3.surface-container-highest"), BgColor(Part<Border>(bar, "ProgressBarRoot")));
                Assert.Equal(1d, Part<Panel>(bar, "DeterminateRoot").Opacity);
                Assert.Equal(0d, Part<Panel>(bar, "IndeterminateRoot").Opacity);

                // 不定态：换成两条滚动指示条（宽度/位移由控件代码驱动既有部件）
                bar.IsIndeterminate = true;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(0d, Part<Panel>(bar, "DeterminateRoot").Opacity);
                Assert.Equal(1d, Part<Panel>(bar, "IndeterminateRoot").Opacity);
                var first = Part<Border>(bar, "IndeterminateProgressBarIndicator");
                var second = Part<Border>(bar, "IndeterminateProgressBarIndicator2");
                Assert.True(first.Width > 0 && second.Width > 0);
                Assert.Equal(PoolBrush("md3.primary"), BgColor(first));

                // 滚动动画仍在跑（控件代码用 RenderTransform 驱动我们模板里的同名部件）
                double Offset(Visual v) => v.RenderTransform?.Value.M31 ?? 0;
                double before = Offset(first);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(30);
                Dispatcher.UIThread.RunJobs();
                double after = Offset(first);
                Assert.True(before != after,
                    $"不定态指示条未滚动：before={before} after={after} rt={first.RenderTransform}");
            } finally {
                win.Close();
            }
        }

        // ───────────────────────── 无障碍：键盘焦点环 ─────────────────────────

        [AvaloniaFact]
        public void CheckBox_KeyboardFocus_ShowsPoolFocusRing() {
            var box = new CheckBox { Content = "选项" };
            var win = Host(box);
            try {
                var ring = Part<Border>(box, "PART_FocusRing");
                Assert.Equal(PoolBrush("md3.primary"), (ring.BorderBrush as ISolidColorBrush)?.Color);
                Assert.Equal(0d, ring.Opacity);              // 鼠标点击不该出现焦点环
                box.Focus(NavigationMethod.Tab);             // 键盘导航焦点
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1d, ring.Opacity);
            } finally {
                win.Close();
            }
        }

        // ───────────────────────── 既有用法不回归 ─────────────────────────

        [AvaloniaFact]
        public void FaderSlider_KeepsItsOwnTemplate() {
            // 混合台/音轨推子用 Styles.axaml 的 `Slider.fader` 模板（16×6 胶囊把手）。
            // 应用级 Template setter 优先于 ControlTheme 的 Template → fader 必须原样保留。
            var slider = new Slider { Classes = { "fader" }, Width = 200, Minimum = 0, Maximum = 10, Value = 4 };
            var win = Host(slider);
            try {
                Assert.Equal(typeof(Slider), slider.Theme?.TargetType);       // 主题仍挂着
                var thumb = Part<Thumb>(slider, "thumb");
                var pill = thumb.GetVisualDescendants().OfType<Border>().First();
                Assert.Equal(6, pill.Height);                                 // fader 把手，不是我们的 16×16 圆钮
                Assert.Equal(16, pill.Width);
            } finally {
                win.Close();
            }
        }

        [AvaloniaFact]
        public void ToggleSwitch_OnOffContent_BehavesLikeFluent() {
            // 未显式设 OnContent/OffContent 的开关（如 ThemeEditorWindow 的深浅色开关）：
            // 我们的模板必须与 Fluent 模板表现一致（有文案就都有，没有就都没有）。
            Assert.True(Application.Current!.TryFindResource(typeof(ToggleSwitch), out object? res));
            var fluentTheme = Assert.IsType<Avalonia.Styling.ControlTheme>(res);
            var ours = new ToggleSwitch();
            var theirs = new ToggleSwitch { Theme = fluentTheme };
            var win = new WindowEx { Width = 320, Height = 160, Content = new StackPanel { Children = { ours, theirs } } };
            win.Show();
            Dispatcher.UIThread.RunJobs();
            try {
                bool OursText = HasLabel(Part<ContentPresenter>(ours, "PART_OffContentPresenter"));
                bool TheirText = HasLabel(Part<ContentPresenter>(theirs, "PART_OffContentPresenter"));
                Assert.True(OursText == TheirText,
                    $"未设 OffContent 时与 Fluent 不一致：ours={OursText} fluent={TheirText}");
            } finally {
                win.Close();
            }
        }

        private static bool HasLabel(ContentPresenter cp) =>
            cp.Content is string s && !string.IsNullOrEmpty(s)
            || cp.GetVisualDescendants().OfType<TextBlock>().Any(tb => !string.IsNullOrEmpty(tb.Text));

        // ───────────────────────── 动效/无障碍残留契约 ─────────────────────────

        [AvaloniaFact]
        public void ThemeFile_DeclaresFocusAndDisabledStates() {
            string xaml = ThemeXaml();
            foreach (string state in new[] { ":focus-visible", ":disabled", ":pointerover", ":pressed" }) {
                Assert.Contains(state, xaml);
            }
            Assert.Contains("PART_FocusRing", xaml);
        }
    }
}

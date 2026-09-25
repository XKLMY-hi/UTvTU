using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using OpenUtau.App;
using OpenUtau.Core.Theming;
using OpenUtau.Theming;
using OpenUtau.Colors;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// 主题契约回归门（v4.0 Phase 1）。
    /// 删除 FluentTheme 前后都必须保持：所有 Plus* 令牌 / Fluent 兼容键 / ThemeManager 绑定键可解析。
    /// 缺键即 CI 红，杜绝"新画笔不同步"与"删除 Fluent 后断链"。
    /// </summary>
    [Collection("Theme")]   // 这些用例会改全局主题/颜色池，串行执行避免互相污染
    public class ThemeContractTests {
        private static readonly string[] PlusColorKeys = {
            // Surface：2026-09-25 起这 9 个旧颜色键已退役，改为直接由画刷键接 MD3 颜色池
            // （PlusBrushSurface* / PlusBrushDialogCard 见下方画刷清单）
            // Border 6
            "PlusBorderSubtle", "PlusBorderDefault", "PlusBorderHover", "PlusBorderFocus",
            "PlusBorderGlass", "PlusBorderGlassStrong",
            // Accent 6
            "PlusAccent", "PlusAccentHover", "PlusAccentPressed",
            "PlusAccentMuted", "PlusAccentSoft", "PlusAccentGlow",
            // Text 4
            "PlusTextPrimary", "PlusTextSecondary", "PlusTextDisabled", "PlusTextOnAccent",
            // Semantic 5
            "PlusSemanticSuccess", "PlusSemanticWarning", "PlusSemanticWarningBg",
            "PlusSemanticDanger", "PlusSemanticInfo",
            // Glass 5
            "PlusGlassCard", "PlusGlassPopover", "PlusGlassBackdrop",
            "PlusGlassHeader", "PlusGlassInsetBottom",
        };

        private static readonly string[] PlusBrushKeys = {
            "PlusBrushSurfaceDeep", "PlusBrushSurfaceBase", "PlusBrushSurfaceRaised",
            "PlusBrushSurfaceControl", "PlusBrushSurfaceHover", "PlusBrushSurfacePressed",
            "PlusBrushSurfaceDisabled", "PlusBrushSurfaceOverlay", "PlusBrushDialogCard",
            "PlusBrushBorderSubtle", "PlusBrushBorderDefault", "PlusBrushBorderHover", "PlusBrushBorderFocus",
            "PlusBrushTextPrimary", "PlusBrushTextSecondary", "PlusBrushTextDisabled", "PlusBrushTextOnAccent",
            "PlusBrushAccent", "PlusBrushAccentHover", "PlusBrushAccentPressed",
            "PlusBrushAccentMuted", "PlusBrushAccentSoft",
            "PlusBrushGlassCard", "PlusBrushGlassPopover",
            "PlusBrushSemanticSuccess", "PlusBrushSemanticWarning",
            "PlusBrushSemanticDanger", "PlusBrushSemanticInfo",
        };

        private static readonly string[] PlusGeometryKeys = {
            "PlusRadiusXs", "PlusRadiusSm", "PlusRadiusMd", "PlusRadiusLg", "PlusRadiusXl", "PlusRadiusPill",
            "PlusControlHeight", "PlusControlHeightSmall", "PlusControlHeightLarge", "PlusIconSize",
            "PlusSpace1", "PlusSpace2", "PlusSpace3", "PlusSpace4", "PlusSpace5", "PlusSpace6", "PlusSpace8",
            "PlusFontSizeXs", "PlusFontSizeSm", "PlusFontSizeBase", "PlusFontSizeMd",
            "PlusFontSizeLg", "PlusFontSizeXl", "PlusFontSize2Xl",
            "PlusFontFamily", "PlusFontFamilyMono",
            "PlusElevationContact", "PlusElevationFloat", "PlusElevationRaised", "PlusElevationPopup",
            "PlusHighlightTop", "PlusHighlightTopStrong", "PlusShadowBottom",
            "PlusFocusRing", "PlusGlowPrimary", "PlusGlowDanger",
        };

        /// <summary>删除 FluentTheme 后仍必须本地提供的兼容键（含几何）。</summary>
        private static readonly string[] FluentCompatKeys = {
            "SystemControlForegroundBaseLowBrush", "SystemControlBackgroundAltMediumBrush",
            "MenuFlyoutItemForegroundPressed", "TextControlForegroundDisabled",
            // Avalonia 12 Fluent 文本键（Brushes.axaml 提供；Suki 后挂载会覆盖 Fluent 同名键导致暗色下黑字）
            "TextFillColorPrimaryBrush", "TextFillColorSecondaryBrush",
            "TextFillColorDisabledBrush", "TextControlForeground",
            "ComboBoxDropDownBackground", "ComboBoxDropDownBorderBrush",
            "RadioButtonOuterEllipseFill", "RadioButtonOuterEllipseStroke",
            "RadioButtonOuterEllipseFillPointerOver", "RadioButtonOuterEllipseStrokePointerOver",
            "SliderHorizontalThumbWidth", "SliderHorizontalThumbHeight", "ControlContentThemeFontSize",
            "ComboBoxThemeMinWidth", "ComboBoxMinHeight", "ComboBoxPadding",
            "ComboBoxDropdownBorderThickness", "ComboBoxDropdownBorderPadding",
            "AutoCompleteListPadding", "RadioButtonBorderThemeThickness",
        };

        /// <summary>ThemeManager.BrushBindings 投影所依赖的键（缺键会打 WARN 并保留旧值）。</summary>
        private static readonly string[] ThemeManagerBindingKeys = {
            "SystemControlForegroundBaseHighBrush", "SystemControlBackgroundAltHighBrush",
            "NeutralAccentBrush", "NeutralAccentBrushSemi",
            "AccentBrush1", "AccentBrush1Semi", "AccentBrush2", "AccentBrush2Semi",
            "AccentBrush3", "AccentBrush3Semi",
            "TickLineBrushLow", "BarNumberBrush", "FinalPitchBrush",
            "RealCurveFillBrush", "RealCurveStrokeBrush",
        };

        private static bool Resolves(string key) =>
            Application.Current!.Resources.TryGetResource(key, Application.Current.ActualThemeVariant, out _);

        [AvaloniaFact]
        public void DarkTheme_AllTokensResolve() {
            ThemeManager.Apply("Dark");

            var all = PlusColorKeys.Concat(PlusBrushKeys)
                .Concat(PlusGeometryKeys).Concat(FluentCompatKeys)
                .Concat(ThemeManagerBindingKeys)
                .ToArray();
            var missing = all.Where(k => !Resolves(k)).ToArray();
            Assert.True(missing.Length == 0, $"缺失资源键: {string.Join(", ", missing)}");
        }

        [AvaloniaFact]
        public void LightTheme_AllTokensResolve() {
            ThemeManager.Apply("Light");

            var all = PlusColorKeys.Concat(PlusBrushKeys)
                .Concat(PlusGeometryKeys).Concat(FluentCompatKeys)
                .Concat(ThemeManagerBindingKeys)
                .ToArray();
            var missing = all.Where(k => !Resolves(k)).ToArray();
            Assert.True(missing.Length == 0, $"缺失资源键: {string.Join(", ", missing)}");
        }

        /// <summary>三态往返：variant、IsDarkMode、静态画刷随切换正确变化且非 null。</summary>
        [AvaloniaFact]
        public void ThemeVariantRoundTrip_DarkLightCustom() {
            ThemeManager.Apply("Dark");
            Assert.Equal(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);
            Assert.True(ThemeManager.IsDarkMode);
            Assert.NotNull(ThemeManager.AccentBrush1);
            Assert.NotNull(ThemeManager.ForegroundBrush);
            // Suki 同步：暖灰为活动主题色（顺序铁律：先 ChangeBaseTheme 后 ChangeColorTheme）
            Assert.Equal("Plus 暖灰", SukiUI.SukiTheme.GetInstance().ActiveColorTheme?.DisplayName);

            ThemeManager.Apply("Light");
            Assert.Equal(ThemeVariant.Light, Application.Current!.RequestedThemeVariant);
            Assert.False(ThemeManager.IsDarkMode);
            Assert.NotNull(ThemeManager.AccentBrush1);
            Assert.Equal("Plus 暖灰", SukiUI.SukiTheme.GetInstance().ActiveColorTheme?.DisplayName);

            // 自定义 YAML（无文件则回退 Light 基座；注册为独立 ThemeVariant，非内置 Light）
            ThemeManager.Apply("SomeCustom");
            Assert.NotNull(Application.Current!.RequestedThemeVariant);
            Assert.NotEqual(ThemeVariant.Light, Application.Current!.RequestedThemeVariant);
            Assert.NotNull(ThemeManager.BackgroundBrush);
        }

        /// <summary>
        /// 切换后解析到的颜色值确实变化（防"切换不完全"回归）：
        /// 2026-09-25 起背景/色阶键直接接 MD3 颜色池，契约改为
        /// **主题 → 颜色池 → 旧键画刷** 三段同步：任一段脱节即红。
        /// </summary>
        [AvaloniaFact]
        public void ThemeSwitch_ChangesResolvedColorValues() {
            ThemeManager.Apply("Dark");
            Color darkSurface = ColorPool.Current.Color(Md3Role.Surface);
            Assert.Equal(darkSurface, BrushColorOf("SystemControlBackgroundAltHighBrush"));
            Assert.Equal(ColorPool.Current.Color(Md3Role.SurfaceContainer), BrushColorOf("PlusBrushSurfaceRaised"));
            // 窗口渐变背景画刷（停靠点也接颜色池）
            // 注意：资源键指向的是**同一个** LinearGradientBrush 实例，停靠点被 DynamicResource 就地更新，
            // 所以必须当场取"颜色值"，不能留画刷引用到下一段再读
            var bgDark = Assert.IsType<LinearGradientBrush>(ResolveValue("PlusBrushWindowBackground"));
            Assert.Equal(3, bgDark.GradientStops.Count);
            Color darkStop = bgDark.GradientStops[0].Color;

            ThemeManager.Apply("Light");
            Color lightSurface = ColorPool.Current.Color(Md3Role.Surface);
            Assert.Equal(lightSurface, BrushColorOf("SystemControlBackgroundAltHighBrush"));
            Assert.NotEqual(darkSurface, lightSurface);
            Color lightStop = Assert.IsType<LinearGradientBrush>(ResolveValue("PlusBrushWindowBackground")).GradientStops[0].Color;
            Assert.NotEqual(darkStop, lightStop);

            // 切回 Dark 确认往返
            ThemeManager.Apply("Dark");
            Assert.Equal(darkSurface, BrushColorOf("SystemControlBackgroundAltHighBrush"));
        }

        private static Color BrushColorOf(string key) =>
            Assert.IsAssignableFrom<ISolidColorBrush>(ResolveValue(key)).Color;

        /// <summary>
        /// 回归：TextBlock 默认前景必须跟随主题（防"Suki 后挂覆盖 Fluent 文本键 → 暗色下黑字"）。
        /// 暗色下亮字、浅色下深字，且解析的是 SolidColorBrush（非 Black 硬默认）。
        /// </summary>
        [AvaloniaFact]
        public void TextBlock_DefaultForegroundFollowsTheme() {
            ThemeManager.Apply("Dark");
            var win = new OpenUtau.App.Controls.WindowEx();
            var tb = new TextBlock { Text = "测试文字" };
            win.Content = tb;
            win.Show();
            var darkFg = Assert.IsAssignableFrom<ISolidColorBrush>(tb.Foreground);
            Assert.True(darkFg.Color.R > 0x80, $"dark theme fg should be light, got {darkFg.Color}");

            ThemeManager.Apply("Light");
            var lightFg = Assert.IsAssignableFrom<ISolidColorBrush>(tb.Foreground);
            Assert.True(lightFg.Color.R < 0x80, $"light theme fg should be dark, got {lightFg.Color}");
        }

        private static object? ResolveValue(string key) {
            Application.Current!.Resources.TryGetResource(key, Application.Current.ActualThemeVariant, out var v);
            return v;
        }

        /// <summary>
        /// 回归（2026-09-25 设计变更）：WindowEx = **原生窗口 + MD3 颜色池背景** ——
        /// 原生装饰 + 不自绘标题栏 + 无透明合成；且**不再继承 SukiWindow**
        /// （Suki 模板自带的两层背景会盖住 Window.Background，即"背景还是 Suki"的根因）。
        /// </summary>
        [AvaloniaFact]
        public void WindowEx_NativeChrome_Contract() {
            ThemeManager.Apply("Dark");
            ColorPool.SetDark(true);
            var win = new OpenUtau.App.Controls.WindowEx();
            Assert.Equal(WindowDecorations.Full, win.WindowDecorations);
            Assert.False(win.ExtendClientAreaToDecorationsHint);
            Assert.False(typeof(SukiUI.Controls.SukiWindow).IsAssignableFrom(typeof(OpenUtau.App.Controls.WindowEx)),
                "WindowEx 不应再继承 SukiWindow（其模板会盖住 MD3 背景）");

            win.Show();
            try {
                // 背景 = 颜色池的 surface，且不是全透明
                var solid = Assert.IsAssignableFrom<ISolidColorBrush>(win.Background);
                Assert.Equal(ColorPool.Current.Color(Md3Role.Surface), solid.Color);
                Assert.NotEqual(0, solid.Color.A);
            } finally {
                win.Close();
            }
        }

        /// <summary>
        /// 模板可应用、无缺失 PART 异常。TextBox 取 PlusTheme 高度 32
        /// （阶段 A 实证：SukiUI 7.x 无 TextBox 模板，TextBoxExtensions 只做附加属性定制）。
        /// </summary>
        [AvaloniaFact]
        public void InputControls_ApplyTemplateWithoutError() {
            ThemeManager.Apply("Dark");
            var win = new OpenUtau.App.Controls.WindowEx();
            var stack = new Avalonia.Controls.StackPanel();
            win.Content = stack;
            var controls = new Avalonia.Controls.Control[] {
                new TextBox(), new ComboBox(), new ToggleSwitch(), new CheckBox(),
                new RadioButton(), new Slider(), new ProgressBar(), new Avalonia.Controls.Primitives.ToggleButton(),
            };
            foreach (var c in controls) {
                stack.Children.Add(c);
            }
            win.Show();
            foreach (var c in controls) {
                c.ApplyTemplate();   // 缺必选 PART 会在此抛异常
            }
            // TextBox 取 PlusTheme 高度（32）
            Assert.Equal(32, ((TextBox)controls[0]).Height);
        }

        /// <summary>视觉树按名查找（Avalonia 11 模板部件经 NameScope 注册，控件无 GetTemplateChild）。</summary>
        private static Avalonia.Visual? FindPart(Avalonia.Visual root, string name) {
            if ((root as INamed)?.Name == name) {
                return root;
            }
            foreach (var child in root.GetVisualChildren()) {
                var r = FindPart(child, name);
                if (r != null) {
                    return r;
                }
            }
            return null;
        }

        /// <summary>
        /// 诊断：Suki ToggleSwitch（模板 PART：SwitchBackground 轨道 + PanelSelected 选中填充 +
        /// PART_SwitchKnob/SwitchKnob）。off：填充在轨道外（Clip 缩回）、knob 在左；
        /// on：填充覆盖轨道、knob 在右。轨道底色动态渐变无法断言颜色，用相对位置断言。
        /// </summary>
        [AvaloniaFact]
        public void ToggleSwitch_TrackAndKnobPerState() {
            ThemeManager.Apply("Dark");
            var off = new ToggleSwitch { IsChecked = false };
            var on = new ToggleSwitch { IsChecked = true };
            var win = new OpenUtau.App.Controls.WindowEx();
            var sp = new Avalonia.Controls.StackPanel { Children = { off, on } };
            win.Content = sp;
            win.Show();
            off.ApplyTemplate();
            on.ApplyTemplate();

            var offTrack = FindPart(off, "SwitchBackground") as Border;
            var onTrack = FindPart(on, "SwitchBackground") as Border;
            var offFill = FindPart(off, "PanelSelected") as Avalonia.Controls.Panel;
            var onFill = FindPart(on, "PanelSelected") as Avalonia.Controls.Panel;
            var offKnob = FindPart(off, "SwitchKnob") as Border;
            var onKnob = FindPart(on, "SwitchKnob") as Border;
            Assert.NotNull(offTrack);
            Assert.NotNull(onTrack);
            Assert.NotNull(offFill);
            Assert.NotNull(onFill);
            Assert.NotNull(offKnob);
            Assert.NotNull(onKnob);

            // 选中填充（Clip 动画）：off 缩回轨道外，on 展开覆盖轨道
            Assert.True(offFill!.Bounds.X > 0, $"off fill should sit outside track, got X={offFill.Bounds.X}");
            Assert.True(onFill!.Bounds.X < 0, $"on fill should cover track, got X={onFill.Bounds.X}");
            // knob 相对位置：off 在左、on 在右
            Assert.True(offKnob!.Bounds.X < onKnob!.Bounds.X,
                $"off knob should be left of on knob, got off={offKnob.Bounds.X} on={onKnob.Bounds.X}");
        }

        /// <summary>契约：ListBoxItem 选中背景 = 颜色池 secondary-container（v4.0 起统一走池，不再用旧 accent-muted）。</summary>
        [AvaloniaFact]
        public void ListBoxItem_SelectedGetsAccentMuted() {
            ThemeManager.Apply("Dark");
            ColorPool.SetDark(true);
            var expected = Md3ColorPool.ToColor(Md3SchemeColors.Create(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, true).Get(Md3Role.SecondaryContainer));
            var item = new ListBoxItem { IsSelected = true };
            var win = new OpenUtau.App.Controls.WindowEx();
            win.Content = item;
            win.Show();
            item.ApplyTemplate();
            // 定案：外观写在应用级样式里、作用于**控件自身属性**（模板部件由主题拥有，不再从外面改）
            var bg = Assert.IsAssignableFrom<ISolidColorBrush>(item.Background);
            Assert.Equal(expected, bg.Color);
        }

        /// <summary>诊断：ComboBox 边框属性取 v4.0 值（PlusBrushBorderDefault + 1px）。</summary>
        [AvaloniaFact]
        public void ComboBox_BorderRenders() {
            ThemeManager.Apply("Dark");
            var combo = new ComboBox();
            var win = new OpenUtau.App.Controls.WindowEx();
            win.Content = combo;
            win.Show();
            combo.ApplyTemplate();
            Assert.NotNull(combo.BorderBrush);
            Assert.True(combo.BorderThickness.Top > 0, $"BorderThickness should be >0, got {combo.BorderThickness}");
            var bb = Assert.IsAssignableFrom<ISolidColorBrush>(combo.BorderBrush);
            Assert.True(bb.Color.R > 0x20, $"BorderBrush should be visible gray, got {bb.Color}");
        }

        /// <summary>
        /// 契约（v4.0 · MD3 控件风格层）：默认按钮 = 描边按钮 —— 圆角 8、内边距 14,6、
        /// 最小高 32、描边 outline-variant、文字 primary；字体仍由 SukiOverrides 收敛为 HarmonyOS 13px。
        /// </summary>
        [AvaloniaFact]
        public void Button_GetsMd3OutlinedStyle() {
            ThemeManager.Apply("Dark");
            ColorPool.SetDark(true);
            var win = new OpenUtau.App.Controls.WindowEx();
            var btn = new Avalonia.Controls.Button();
            win.Content = btn;
            win.Show();
            btn.ApplyTemplate();
            Assert.Equal(new CornerRadius(8), btn.CornerRadius);
            Assert.True(double.IsNaN(btn.Height), $"按钮高度应由 Padding/MinHeight 决定，实际 {btn.Height}");
            Assert.Equal(new Thickness(14, 6, 14, 6), btn.Padding);
            Assert.Equal(32, btn.MinHeight);
            // SukiOverrides 收敛层：字体归 Plus 令牌（HarmonyOS 13px）
            Assert.Equal(13, btn.FontSize);
            Assert.Equal("HarmonyOS Sans SC", btn.FontFamily.Name);
            // MD3：描边走 outline-variant、文字走 primary（同一套颜色池）
            var scheme = Md3SchemeColors.Create(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, false);
            Assert.Equal(Md3ColorPool.ToColor(scheme.Get(Md3Role.Primary)),
                Assert.IsAssignableFrom<ISolidColorBrush>(btn.Foreground).Color);
        }

        /// <summary>ChangePianorollColor 不破坏任何投影键。</summary>
        [AvaloniaFact]
        public void ChangePianorollColor_KeepsBindingsResolvable() {
            ThemeManager.Apply("Dark");
            ThemeManager.ChangePianorollColor("Red");

            var missing = ThemeManagerBindingKeys.Where(k => !Resolves(k)).ToArray();
            Assert.Empty(missing);
            Assert.NotNull(ThemeManager.AccentPen1);
        }

        /// <summary>
        /// 向后兼容：旧 33 键 YAML（只填 legacy 字段）经 BuildPalette 自动映射出 Plus* 键，
        /// 且 legacy 主色正确投影到 PlusSurfaceBase/PlusAccent，accent 透明档由主色推导。
        /// </summary>
        [AvaloniaFact]
        public void LegacyThemeYaml_MapsToPlusKeys() {
            ThemeManager.Apply("Dark");  // 基座

            var legacy = new CustomTheme.ThemeYaml {
                IsDarkMode = true,
                BackgroundColor = "#112233",
                BackgroundColorPointerOver = "#223344",
                BackgroundColorPressed = "#334455",
                BackgroundColorDisabled = "#445566",
                SystemAccentColor = "#556677",
                SystemAccentColorLight1 = "#667788",
                SystemAccentColorDark1 = "#778899",
                ForegroundColor = "#8899aa",
                ForegroundColorDisabled = "#99aabb",
                WarningColor = "#aabbcc",
            };
            var palette = CustomTheme.BuildPalette(legacy);

            // Plus* 映射存在
            Assert.True(palette.ContainsKey("PlusSurfaceBase"));
            Assert.True(palette.ContainsKey("PlusSurfaceHover"));
            Assert.True(palette.ContainsKey("PlusAccent"));
            Assert.True(palette.ContainsKey("PlusAccentMuted"));
            Assert.True(palette.ContainsKey("PlusTextPrimary"));
            Assert.True(palette.ContainsKey("PlusSemanticWarningBg"));
            // 映射值正确
            Assert.Equal(Color.Parse("#112233"), palette["PlusSurfaceBase"]);
            Assert.Equal(Color.Parse("#556677"), palette["PlusAccent"]);
            Assert.Equal(Color.Parse("#8899aa"), palette["PlusTextPrimary"]);
            // accent 透明档由主色推导（alpha 收缩，RGB 保留）
            var muted = Assert.IsType<Color>(palette["PlusAccentMuted"]);
            Assert.Equal(0x55, muted.R);
            Assert.Equal(0x66, muted.G);
            Assert.Equal(0x77, muted.B);
            Assert.True(muted.A < 0x55);
        }
    }
}

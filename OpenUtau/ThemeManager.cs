using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using OpenUtau.App.Controls;
using OpenUtau.Core.Util;
using ReactiveUI;
using Serilog;
using SukiUI;
using SukiUI.Models;

namespace OpenUtau.App {
    public class ThemeChangedEvent { }

    public class ThemeManager {
        public static bool IsDarkMode = false;
        public static FontFamily UiFontFamily {
            get {
                if (Application.Current?.TryFindResource("PlusFontFamily", out var res) == true && res is FontFamily ff) {
                    return ff;
                }
                return FontFamily.Parse("Segoe UI");
            }
        }
        public static FontFamily MonoFontFamily {
            get {
                if (Application.Current?.TryFindResource("PlusFontFamilyMono", out var res) == true && res is FontFamily ff) {
                    return ff;
                }
                return FontFamily.Parse("Consolas, Courier New");
            }
        }
        public static IBrush ForegroundBrush = Brushes.Black;
        public static IBrush BackgroundBrush = Brushes.White;
        public static IBrush NeutralAccentBrush = Brushes.Gray;
        public static IBrush NeutralAccentBrushSemi = Brushes.Gray;
        public static IPen NeutralAccentPen = new Pen(Brushes.Black);
        public static IPen NeutralAccentPenSemi = new Pen(Brushes.Black);
        public static IBrush AccentBrush1 = Brushes.White;
        public static IPen AccentPen1 = new Pen(Brushes.White);
        public static IPen AccentPen1Thickness2 = new Pen(Brushes.White);
        public static IPen AccentPen1Thickness3 = new Pen(Brushes.White);
        public static IBrush AccentBrush1Semi = Brushes.Gray;
        public static IBrush AccentBrush2 = Brushes.Gray;
        public static IPen AccentPen2 = new Pen(Brushes.White);
        public static IPen AccentPen2Thickness2 = new Pen(Brushes.White);
        public static IPen AccentPen2Thickness3 = new Pen(Brushes.White);
        public static IBrush AccentBrush2Semi = Brushes.Gray;
        public static IBrush AccentBrush3 = Brushes.Gray;
        public static IPen AccentPen3 = new Pen(Brushes.White);
        public static IPen AccentPen3Thick = new Pen(Brushes.White);
        public static IBrush AccentBrush3Semi = Brushes.Gray;
        public static IBrush TickLineBrushLow = Brushes.Black;
        public static IBrush BarNumberBrush = Brushes.Black;
        public static IPen BarNumberPen = new Pen(Brushes.White);
        public static IBrush FinalPitchBrush = Brushes.Gray;
        public static IPen FinalPitchPen = new Pen(Brushes.Gray);
        public static IBrush RealCurveFillBrush = Brushes.Gray;
        public static IBrush RealCurveStrokeBrush = Brushes.Gray;
        public static IPen RealCurvePen = new Pen(Brushes.Gray, 1D, DashStyle.Dash);
        public static IBrush WhiteKeyBrush = Brushes.White;
        public static IBrush WhiteKeyNameBrush = Brushes.Black;
        public static IBrush CenterKeyBrush = Brushes.White;
        public static IBrush CenterKeyNameBrush = Brushes.Black;
        public static IBrush BlackKeyBrush = Brushes.Black;
        public static IBrush BlackKeyNameBrush = Brushes.White;
        public static IBrush ExpBrush = Brushes.White;
        public static IBrush ExpNameBrush = Brushes.Black;
        public static IBrush ExpShadowBrush = Brushes.Gray;
        public static IBrush ExpShadowNameBrush = Brushes.White;
        public static IBrush ExpActiveBrush = Brushes.Black;
        public static IBrush ExpActiveNameBrush = Brushes.White;

        public static List<TrackColor> TrackColors = new List<TrackColor>(){
                new TrackColor("Pink", "#F06292", "#EC407A", "#F48FB1", "#FAC7D8"),
                new TrackColor("Red", "#EF5350", "#E53935", "#E57373", "#F2B9B9"),
                new TrackColor("Orange", "#FF8A65", "#FF7043", "#FFAB91", "#FFD5C8"),
                new TrackColor("Yellow", "#FBC02D", "#F9A825", "#FDD835", "#FEF1B6"),
                new TrackColor("Light Green", "#CDDC39", "#C0CA33", "#DCE775", "#F2F7CE"),
                new TrackColor("Green", "#66BB6A", "#43A047", "#A5D6A7", "#D2EBD3"),
                new TrackColor("Light Blue", "#4FC3F7", "#29B6F6", "#81D4FA", "#C0EAFD"),
                new TrackColor("Blue", "#4EA6EA", "#1E88E5", "#90CAF9", "#C8E5FC"),
                new TrackColor("Purple", "#BA68C8", "#AB47BC", "#CE93D8", "#E7C9EC"),
                new TrackColor("Pink2", "#E91E63", "#C2185B", "#F06292", "#F8B1C9"),
                new TrackColor("Red2", "#D32F2F", "#B71C1C", "#EF5350", "#F7A9A8"),
                new TrackColor("Orange2", "#FF5722", "#E64A19", "#FF7043", "#FFB8A1"),
                new TrackColor("Yellow2", "#FF8F00", "#FF7F00", "#FFB300", "#FFE097"),
                new TrackColor("Light Green2", "#AFB42B", "#9E9D24", "#CDDC39", "#E6EE9C"),
                new TrackColor("Green2", "#2E7D32", "#1B5E20", "#43A047", "#A1D0A3"),
                new TrackColor("Light Blue2", "#1976D2", "#0D47A1", "#2196F3", "#90CBF9"),
                new TrackColor("Blue2", "#3949AB", "#283593", "#5C6BC0", "#AEB5E0"),
                new TrackColor("Purple2", "#7B1FA2", "#4A148C", "#AB47BC", "#D5A3DE"),
            };

        public static List<string> GetAvailableThemes() {
            Colors.CustomTheme.ListThemes();
            return ["Light", "Dark", ..Colors.CustomTheme.Themes.Select(v => v.Key)];
        }

        private static bool sukiRegistered;
        private static readonly SukiColorTheme PlusWarmGrayTheme =
            new("Plus 暖灰", Color.Parse("#B0C4DE"), Color.Parse("#B0C4DE"));

        /// <summary>
        /// SukiUI 主题同步：切换基底明暗 + 选中暖灰定制色。
        /// 必须在 RequestedThemeVariant 赋值之前调用（ChangeBaseTheme 可能覆写该属性）。
        /// 顺序铁律：先 ChangeBaseTheme 后 ChangeColorTheme —— 实测 ChangeBaseTheme
        /// 会把 ActiveColorTheme 重置为默认色，色彩切换必须最后执行。
        /// SukiUI 未挂载的环境（如部分测试）静默跳过。
        /// </summary>
        private static void ApplySukiTheme(bool isDark) {
            try {
                var suki = SukiTheme.GetInstance();
                if (!sukiRegistered) {
                    suki.AddColorTheme(PlusWarmGrayTheme);
                    sukiRegistered = true;
                }
                suki.ChangeBaseTheme(isDark ? ThemeVariant.Dark : ThemeVariant.Light);
                suki.ChangeColorTheme(PlusWarmGrayTheme);
            } catch (System.Exception e) {
                Log.Warning(e, "[Theme] SukiUI 同步失败（SukiUI 未挂载环境正常忽略）");
            }
        }

        /// <summary>
        /// 主题唯一写入口（v4.0 ThemeVariant 架构）：
        /// Light/Dark → 直接切 RequestedThemeVariant；自定义 YAML → 注册为 ThemeVariant（InheritVariant 按 IsDarkMode）。
        /// 调色板全部经 ThemeDictionaries + DynamicResource 解析，不再逐键拷贝根字典。
        /// 调用方：App.SetTheme / PreferencesViewModel / ThemeEditorWindow / CustomTheme。
        /// </summary>
        public static void Apply(string themeName) {
            if (Application.Current == null) {
                return;
            }
            if (themeName is "Light" or "Dark") {
                ApplySukiTheme(themeName == "Dark");   // Suki 先行：ChangeBaseTheme 可能覆写 RequestedThemeVariant
                Application.Current.RequestedThemeVariant = themeName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
            } else {
                var variant = Colors.CustomTheme.RegisterVariant(themeName);
                ApplySukiTheme(Colors.CustomTheme.Default.IsDarkMode);
                Application.Current.RequestedThemeVariant = variant;
            }
            RebuildProjection();
            // MD3 颜色池跟随主题：旧背景/色阶键（Brushes.axaml / Plus.Resources.axaml）已直接接
            // md3 角色色，池子的深浅色变体必须跟主题同步，否则旧键会停在上一套变体
            // （ThemeManager.Apply 是主题唯一入口：偏好页 / 主题编辑器 / 自定义主题都经它）
            Theming.ColorPool.SetDark(IsDarkMode);
        }

        /// <summary>供 ThemeEditor 实时改写资源键后刷新静态画刷投影（不改主题本身）。</summary>
        public static void RefreshProjection() {
            RebuildProjection();
        }

        /// <summary>
        /// 静态画刷投影：从资源根字典按 schema 重读绑定键。缺键打 WARN 且不覆写旧值（有兜底）。
        /// 单一真相 = Application.Current.Resources；本方法是纯投影，不再有并行真相。
        /// </summary>
        private static void RebuildProjection() {
            if (Application.Current == null) {
                return;
            }
            IResourceDictionary resDict = Application.Current.Resources;
            var themeVariant = Application.Current.ActualThemeVariant;
            if (resDict.TryGetResource("IsDarkMode", themeVariant, out var isDarkObj) && isDarkObj is bool isDark) {
                IsDarkMode = isDark;
            }
            foreach (var (key, setter) in BrushBindings) {
                if (resDict.TryGetResource(key, themeVariant, out var val) && val is IBrush brush) {
                    setter(brush);
                } else {
                    Log.Warning("[Theme] missing brush key '{0}' — keeping previous value", key);
                }
            }
            SetKeyboardBrush();
            TextLayoutCache.Clear();
            MessageBus.Current.SendMessage(new ThemeChangedEvent());
        }

        private delegate void BrushSetter(IBrush brush);

        /// <summary>
        /// 键 → 静态字段投影 schema。新增画笔在此登记一行即可，杜绝"新画笔不同步拿到 null"。
        /// </summary>
        private static readonly (string Key, BrushSetter Setter)[] BrushBindings = {
            ("SystemControlForegroundBaseHighBrush", b => ForegroundBrush = b),
            ("SystemControlBackgroundAltHighBrush", b => BackgroundBrush = b),
            ("NeutralAccentBrush", b => { NeutralAccentBrush = b; NeutralAccentPen = new Pen(b, 1); }),
            ("NeutralAccentBrushSemi", b => { NeutralAccentBrushSemi = b; NeutralAccentPenSemi = new Pen(b, 1); }),
            ("AccentBrush1", b => { AccentBrush1 = b; AccentPen1 = new Pen(b); AccentPen1Thickness2 = new Pen(b, 2); AccentPen1Thickness3 = new Pen(b, 3); }),
            ("AccentBrush1Semi", b => AccentBrush1Semi = b),
            ("AccentBrush2", b => { AccentBrush2 = b; AccentPen2 = new Pen(b, 1); AccentPen2Thickness2 = new Pen(b, 2); AccentPen2Thickness3 = new Pen(b, 3); }),
            ("AccentBrush2Semi", b => AccentBrush2Semi = b),
            ("AccentBrush3", b => { AccentBrush3 = b; AccentPen3 = new Pen(b, 1); AccentPen3Thick = new Pen(b, 3); }),
            ("AccentBrush3Semi", b => AccentBrush3Semi = b),
            ("TickLineBrushLow", b => TickLineBrushLow = b),
            ("BarNumberBrush", b => { BarNumberBrush = b; BarNumberPen = new Pen(b, 1); }),
            ("FinalPitchBrush", b => { FinalPitchBrush = b; FinalPitchPen = new Pen(b, 1); }),
            ("RealCurveFillBrush", b => RealCurveFillBrush = b),
            ("RealCurveStrokeBrush", b => { RealCurveStrokeBrush = b; RealCurvePen = new Pen(b, 2, DashStyle.Dash); }),
        };

        public static void ChangePianorollColor(string color) {
            if (Application.Current == null) {
                return;
            }
            try {
                IResourceDictionary resDict = Application.Current.Resources;
                TrackColor tcolor = GetTrackColor(color);

                resDict["SelectedTrackAccentBrush"] = tcolor.AccentColor;
                resDict["SelectedTrackAccentLightBrush"] = tcolor.AccentColorLight;
                resDict["SelectedTrackAccentLightBrushSemi"] = tcolor.AccentColorLightSemi;
                resDict["SelectedTrackAccentDarkBrush"] = tcolor.AccentColorDark;
                resDict["SelectedTrackCenterKeyBrush"] = tcolor.AccentColorCenterKey;

                RebuildProjection();
            } catch { }
        }
        private static void SetKeyboardBrush() {
            if (Application.Current == null) {
                return;
            }
            IResourceDictionary resDict = Application.Current.Resources;
            object? outVar;
            var themeVariant = Application.Current.ActualThemeVariant;

            if (Preferences.Default.UseTrackColor) {
                if (IsDarkMode) {
                    if (resDict.TryGetResource("SelectedTrackAccentBrush", themeVariant, out outVar)) {
                        CenterKeyNameBrush = (IBrush)outVar!;
                        WhiteKeyBrush = (IBrush)outVar!;
                    }
                    if (resDict.TryGetResource("SelectedTrackCenterKeyBrush", themeVariant, out outVar)) {
                        CenterKeyBrush = (IBrush)outVar!;
                    }
                    if (resDict.TryGetResource("WhiteKeyNameBrush", themeVariant, out outVar)) {
                        WhiteKeyNameBrush = (IBrush)outVar!;
                    }
                    if (resDict.TryGetResource("BlackKeyBrush", themeVariant, out outVar)) {
                        BlackKeyBrush = (IBrush)outVar!;
                    }
                    if (resDict.TryGetResource("BlackKeyNameBrush", themeVariant, out outVar)) {
                        BlackKeyNameBrush = (IBrush)outVar!;
                    }
                    ExpBrush = BlackKeyBrush;
                    ExpNameBrush = BlackKeyNameBrush;
                    ExpActiveBrush = WhiteKeyBrush;
                    ExpActiveNameBrush = WhiteKeyNameBrush;
                    ExpShadowBrush = CenterKeyBrush;
                    ExpShadowNameBrush = CenterKeyNameBrush;
                } else { // LightMode
                    if (resDict.TryGetResource("SelectedTrackAccentBrush", themeVariant, out outVar)) {
                        CenterKeyNameBrush = (IBrush)outVar!;
                        WhiteKeyNameBrush = (IBrush)outVar!;
                        BlackKeyBrush = (IBrush)outVar!;
                    }
                    if (resDict.TryGetResource("SelectedTrackCenterKeyBrush", themeVariant, out outVar)) {
                        CenterKeyBrush = (IBrush)outVar!;
                    }
                    if (resDict.TryGetResource("WhiteKeyBrush", themeVariant, out outVar)) {
                        WhiteKeyBrush = (IBrush)outVar!;
                    }
                    if (resDict.TryGetResource("BlackKeyNameBrush", themeVariant, out outVar)) {
                        BlackKeyNameBrush = (IBrush)outVar!;
                    }
                    ExpBrush = WhiteKeyBrush;
                    ExpNameBrush = WhiteKeyNameBrush;
                    ExpActiveBrush = BlackKeyBrush;
                    ExpActiveNameBrush = BlackKeyNameBrush;
                    ExpShadowBrush = CenterKeyBrush;
                    ExpShadowNameBrush = CenterKeyNameBrush;
                }
            } else { // DefColor
                if (resDict.TryGetResource("WhiteKeyBrush", themeVariant, out outVar)) {
                    WhiteKeyBrush = (IBrush)outVar!;
                }
                if (resDict.TryGetResource("WhiteKeyNameBrush", themeVariant, out outVar)) {
                    WhiteKeyNameBrush = (IBrush)outVar!;
                }
                if (resDict.TryGetResource("CenterKeyBrush", themeVariant, out outVar)) {
                    CenterKeyBrush = (IBrush)outVar!;
                }
                if (resDict.TryGetResource("CenterKeyNameBrush", themeVariant, out outVar)) {
                    CenterKeyNameBrush = (IBrush)outVar!;
                }
                if (resDict.TryGetResource("BlackKeyBrush", themeVariant, out outVar)) {
                    BlackKeyBrush = (IBrush)outVar!;
                }
                if (resDict.TryGetResource("BlackKeyNameBrush", themeVariant, out outVar)) {
                    BlackKeyNameBrush = (IBrush)outVar!;
                }
                if (!IsDarkMode) {
                    ExpBrush = WhiteKeyBrush;
                    ExpNameBrush = WhiteKeyNameBrush;
                    ExpActiveBrush = BlackKeyBrush;
                    ExpActiveNameBrush = BlackKeyNameBrush;
                    ExpShadowBrush = CenterKeyBrush;
                    ExpShadowNameBrush = CenterKeyNameBrush;
                } else {
                    ExpBrush = BlackKeyBrush;
                    ExpNameBrush = BlackKeyNameBrush;
                    ExpActiveBrush = WhiteKeyBrush;
                    ExpActiveNameBrush = WhiteKeyNameBrush;
                    ExpShadowBrush = CenterKeyBrush;
                    ExpShadowNameBrush = CenterKeyNameBrush;
                }
            }
        }

        public static string GetString(string key) {
            TryGetString(key, out string value);
            return value;
        }

        public static bool TryGetString(string key, out string value) {
            if (Application.Current == null) {
                value = key;
                return false;
            }
            IResourceDictionary resDict = Application.Current.Resources;
            // 注意：字符串在 MergedDictionaries（多语言），用 ThemeVariant.Default 即可解析；
            // 不能用 ActualThemeVariant（UI 线程绑定属性），否则后台线程（如 PianoRollViewModel 构造）调用会跨线程崩溃。
            if (resDict.TryGetResource(key, ThemeVariant.Default, out var outVar) && outVar is string s) {
                value = s;
                return true;
            }
            value = key;
            return false;
        }

        public static TrackColor GetTrackColor(string name) {
            if (TrackColors.Any(c => c.Name == name)) {
                return TrackColors.First(c => c.Name == name);
            }
            return TrackColors.First(c => c.Name == "Blue");
        }
    }

    public class TrackColor {
        public string Name { get; set; } = "";
        public SolidColorBrush AccentColor { get; set; }
        public SolidColorBrush AccentColorDark { get; set; } // Pressed
        public SolidColorBrush AccentColorLight { get; set; } // PointerOver
        public SolidColorBrush AccentColorLightSemi { get; set; } // BackGround
        public SolidColorBrush AccentColorCenterKey { get; set; } // Keyboard

        public TrackColor(string name, string accentColor, string darkColor, string lightColor, string centerKey) {
            Name = name;
            AccentColor = SolidColorBrush.Parse(accentColor);
            AccentColorDark = SolidColorBrush.Parse(darkColor);
            AccentColorLight = SolidColorBrush.Parse(lightColor);
            AccentColorLightSemi = SolidColorBrush.Parse(lightColor);
            AccentColorLightSemi.Opacity = 0.5;
            AccentColorCenterKey = SolidColorBrush.Parse(centerKey);
        }
    }
}

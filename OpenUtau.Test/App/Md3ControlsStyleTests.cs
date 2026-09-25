using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using OpenUtau.App;
using OpenUtau.Core.Theming;
using OpenUtau.Theming;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// 全局 MD3 控件风格层契约（2026-09-25「其他窗口只同步 UI 风格」）：
    /// 其余 35 个窗口/对话框不再逐个改造，统一靠 App 级样式层拿到池色与圆角规格；
    /// 同时守住「不碰布局」——该层不得改 Padding/MinHeight 之外的结构性属性。
    /// </summary>
    [Collection("Theme")]
    public class Md3ControlsStyleTests {
        private static string AppXaml() =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "App.axaml"));

        private static string StyleXaml() =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Styles", "Md3Controls.axaml"));

        [AvaloniaFact]
        public void Md3Controls_InstalledLast() {
            string app = AppXaml();
            Assert.Contains("Styles/Md3Controls.axaml", app);
            // 顺序：输入主题 / 菜单（Md3InputThemes / Md3Menus）在前，本层在后才能覆盖其默认值
            int mine = app.IndexOf("Styles/Md3Controls.axaml", StringComparison.Ordinal);
            foreach (string later in new[] { "Styles/Md3InputThemes.axaml", "Styles/Md3Menus.axaml" }) {
                int other = app.IndexOf(later, StringComparison.Ordinal);
                Assert.True(other >= 0 && other < mine, $"{later} 必须排在 MD3 控件风格层之前");
            }
        }

        [AvaloniaFact]
        public void Md3Controls_CoversCommonControls_WithPoolRoles() {
            string xaml = StyleXaml();
            foreach (string selector in new[] {
                "TextBox", "ComboBox", "CheckBox, RadioButton", "ListBox",
                "ToggleSwitch", "Slider", "ProgressBar", "ToolTip", "Separator",
            }) {
                Assert.Contains($"Selector=\"{selector}\"", xaml);
            }
            // 颜色只能来自颜色池
            Assert.DoesNotMatch("#[0-9A-Fa-f]{6}", xaml);
            Assert.DoesNotContain("Plus", xaml.Replace("PlusInfo", ""));
            // 按钮/列表项的外观（含状态）在 ControlTheme 里（Md3ControlThemes.axaml）
            string theme = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Styles", "Md3ControlThemes.axaml"));
            Assert.Contains("Md3ButtonTheme", theme);
            Assert.Contains("Md3ListBoxItemTheme", theme);
        }

        [AvaloniaFact]
        public void Md3Controls_DoesNotTouchLayout() {
            string xaml = StyleXaml();
            // 只允许形状/颜色类属性；不得出现宽度/对齐/间距等布局属性
            foreach (string forbidden in new[] {
                "Property=\"Width\"", "Property=\"Height\"", "Property=\"HorizontalAlignment\"",
                "Property=\"VerticalAlignment\"", "Property=\"Margin\" Value=\"0,", "Property=\"Spacing\"",
            }) {
                Assert.DoesNotContain(forbidden, xaml);
            }
        }

        [AvaloniaFact]
        public void TrackHeader_MenuKeys_ResolveLocally() {
            // 曾经的悬空引用：定义 RenderersMenuRes/PhonemizersMenuRes，却按单数名 StaticResource 取用
            string xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Controls", "TrackHeader.axaml"));
            var used = Regex.Matches(xaml, @"\{StaticResource ([A-Za-z0-9_]+)\}")
                .Select(m => m.Groups[1].Value).Distinct().ToList();
            var defined = Regex.Matches(xaml, "x:Key=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToHashSet();
            var missing = used.Where(k => !defined.Contains(k)).ToList();
            Assert.True(missing.Count == 0, "TrackHeader 内悬空引用：" + string.Join(", ", missing));
        }
    }
}

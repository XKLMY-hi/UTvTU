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

        private static string StripComments(string xaml) =>
            Regex.Replace(xaml, "<!--.*?-->", "", RegexOptions.Singleline);

        /// <summary>
        /// 「不碰布局」的**精确化**（2026-10 Lead 裁决 (a)，W49 欢迎页重设计引发）。
        ///
        /// 本契约的原意（见类注释）：这一层是给 35 个窗口统一发"池色 + 圆角"的**全局层**，
        /// 所以**裸元素选择器**上的布局属性是危险的 —— `Button { Height 44 }` 会让所有窗口一起变形。
        /// 但**类选择器**（`Button.navItem`）只作用于显式挂该类的控件，那里的高度/间距是**组件自身规格**，
        /// 沉淀到共享层正是为了消灭"偏好设置一套、欢迎页另一套"的漂移，应当允许。
        ///
        /// 原实现是全文 grep `Property="Height"` ⇒ 无法区分这两种情形，等于**禁止组件样式进共享层**
        /// （实测：fx-ui 提取 navItem 后本用例 809→810 的唯一红点）。
        /// 现在改为：按 &lt;Style Selector="…"&gt; 分块，**只对不含 '.' 的裸元素选择器**禁布局属性。
        /// </summary>
        [AvaloniaFact]
        public void Md3Controls_DoesNotTouchLayout_ForGlobalElementSelectors() {
            string xaml = StripComments(StyleXaml());
            string[] forbidden = {
                "Property=\"Width\"", "Property=\"Height\"", "Property=\"HorizontalAlignment\"",
                "Property=\"VerticalAlignment\"", "Property=\"Spacing\"",
            };
            MatchCollection blocks = Regex.Matches(xaml,
                "<Style Selector=\"([^\"]+)\"[^>]*>(.*?)</Style>", RegexOptions.Singleline);
            int globalBlocks = 0;
            foreach (Match m in blocks) {
                string selector = m.Groups[1].Value;
                if (selector.Contains('.')) {
                    continue;   // 类/伪类限定的组件样式：允许自带布局规格
                }
                globalBlocks++;
                string body = m.Groups[2].Value;
                foreach (string f in forbidden) {
                    Assert.False(body.Contains(f, StringComparison.Ordinal),
                        $"全局选择器 {selector} 不得设置布局属性 {f}（会波及其它 35 个窗口）");
                }
                // Margin 只允许**归零**（既有合法写法：ToggleSwitch/Slider 用 Margin 0 中和 Fluent 默认间距）；
                // 非零 Margin 会移动所有该类型控件 ⇒ 仍然禁止
                foreach (Match setter in Regex.Matches(body, "Property=\"Margin\"\\s+Value=\"([^\"]*)\"")) {
                    Assert.True(setter.Groups[1].Value == "0",
                        $"全局选择器 {selector} 的 Margin 只能归零，实际 Value=\"{setter.Groups[1].Value}\"");
                }
            }
            // 解析兜底：解析到的全局块太少 ⇒ 说明正则失效（而非真的合规），别让契约悄悄失效
            Assert.True(globalBlocks >= 8, $"解析到的全局样式块过少（{globalBlocks}），契约可能已失效");
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

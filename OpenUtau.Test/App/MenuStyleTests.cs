using System;
using System.IO;
using Avalonia.Headless.XUnit;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// 弹出菜单契约（2026-09-25 用户反馈后修正）：
    /// 文字左对齐（不再留无用的图标列）、悬浮高光圆角 + md3 角色色、
    /// 弹出容器用 md3 容器色、淡入走动效接口（不再手搓 KeyFrame）。
    /// </summary>
    public class MenuStyleTests {
        private static string ReadStyles(string name) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Styles", name));

        [AvaloniaFact]
        public void MenuItems_AreLeftAligned_WithoutIconColumn() {
            string xaml = ReadStyles("Md3Menus.axaml");
            // 无图标占位列（否则文字会被顶到 24px 之后，看着不左对齐）
            Assert.DoesNotContain("Content=\"{TemplateBinding Icon}\"", xaml);
            Assert.Contains("Content=\"{TemplateBinding Header}\"", xaml);
            Assert.Contains("HorizontalAlignment=\"Left\"", xaml);
        }

        [AvaloniaFact]
        public void MenuItems_HoverUsesMd3_RoundedFill() {
            string xaml = ReadStyles("Md3Menus.axaml");
            // 圆角填充式高光 + md3 角色色（旧 Plus 令牌不再用于菜单）
            Assert.Contains("Border x:Name=\"PART_Item\" Background=\"Transparent\" CornerRadius=\"8\"", xaml);
            Assert.Contains("^:pointerover /template/ Border#PART_Item", xaml);
            Assert.Contains("md3.surface-container-highest", xaml);
            Assert.DoesNotContain("PlusBrushSurfaceHover", xaml);
            Assert.DoesNotContain("PlusBrushSurfaceOverlay", xaml);
            Assert.DoesNotContain("PlusBrushBorderDefault", xaml);
            Assert.DoesNotContain("PlusRadiusLg", xaml);
        }

        [AvaloniaFact]
        public void PopupContainers_UseMd3Roles_AndDeclarativeFade() {
            string xaml = ReadStyles("Md3Menus.axaml");
            Assert.Contains("md3.surface-container-high", xaml);
            Assert.Contains("md3.outline-variant", xaml);
            Assert.Contains("CornerRadius=\"12\"", xaml);
            // 淡入走动效接口（挂载即播），不再手搓 KeyFrame 秒数
            // 浮层淡入改由 Styles/Md3Transitions.axaml 的 Border.menuPopup 动画提供（框架 Animation）
            string transitions = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Styles", "Md3Transitions.axaml"));
            Assert.Contains("Border.menuPopup", transitions);
            Assert.Contains("<Animation", transitions);
            Assert.DoesNotContain("motion:Motion.", xaml);
            Assert.DoesNotContain("<KeyFrame", xaml);
        }
    }
}

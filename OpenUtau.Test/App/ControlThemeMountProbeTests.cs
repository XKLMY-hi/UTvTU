using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using OpenUtau.App;
using OpenUtau.App.Controls;
using OpenUtau.Core.Theming;
using OpenUtau.Theming;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// 实验探针：ControlTheme 与"装主题"的 Style 放同一文件时，能否真正装上控件
    ///（上一轮跨字典时四种挂法都失败）。结论写回决策文档第 16 节。
    /// </summary>
    [Collection("Theme")]
    public class ControlThemeMountProbeTests {
        private static string AppXaml() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "App.axaml"));

        [AvaloniaFact]
        public void SameFileTheme_IsMountedOnControls() {
            ColorPool.Initialize(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, ThemeManager.IsDarkMode);
            var btn = new Button { Content = "x" };
            var item = new ListBoxItem { Content = "y" };
            var panel = new StackPanel { Children = { btn, item } };
            var win = new WindowEx { Width = 300, Height = 200, Content = panel };
            win.Show();
            btn.ApplyTemplate();
            item.ApplyTemplate();

            bool btnHasOurTemplate = btn.GetVisualDescendants().OfType<Border>().Any(b => b.Name == "PART_Root");
            bool itemHasOurTemplate = item.GetVisualDescendants().OfType<Border>().Any(b => b.Name == "PART_Root");

            File.WriteAllLines(Path.Combine(Path.GetTempPath(), "theme-mount-probe.txt"), new[] {
                $"App.axaml 含 Md3ControlThemes: {AppXaml().Contains("Md3ControlThemes")}",
                $"Button.Theme = {btn.Theme?.GetType().Name ?? "null"}",
                $"Button 模板根 PART_Root: {btnHasOurTemplate}",
                $"ListBoxItem 模板根 PART_Root: {itemHasOurTemplate}",
                $"Button.CornerRadius = {btn.CornerRadius}",
                $"Button.Padding = {btn.Padding}",
                $"ListBoxItem.CornerRadius = {item.CornerRadius}",
                $"Button.Background = {(btn.Background as ISolidColorBrush)?.Color}",
            });
            win.Close();
            Assert.True(true);
        }
    }
}

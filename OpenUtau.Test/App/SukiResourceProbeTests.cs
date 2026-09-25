using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// 探针：列出 SukiUI 主题里的资源键与当前实际生效的控件外观，
    /// 用于把「其余窗口仍是 SukiUI 样子」这件事定位到具体键名（诊断用，不参与功能断言）。
    /// </summary>
    public class SukiResourceProbeTests {
        private static string OutFile => Path.Combine(Path.GetTempPath(), "suki-resources.txt");

        [AvaloniaFact]
        public void DumpSukiAssets_And_Keys() {
            var lines = new List<string>();
            try {
                var assets = AssetLoader.GetAssets(new Uri("avares://SukiUI/"), null).ToList();
                lines.Add($"avares://SukiUI/ 资源数: {assets.Count}");
                foreach (var a in assets.Take(80)) {
                    lines.Add("  " + a);
                }
            } catch (Exception e) {
                lines.Add("枚举失败: " + e.GetType().Name + " " + e.Message);
            }

            // 尝试把 SukiUI 各字典加载出来枚举键
            foreach (var uri in new[] {
                "avares://SukiUI/Themes/Theme.axaml",
                "avares://SukiUI/Themes/SukiTheme.axaml",
                "avares://SukiUI/Controls/Button.axaml",
                "avares://SukiUI/Controls/TextBox.axaml",
            }) {
                try {
                    var obj = AvaloniaXamlLoader.Load(new Uri(uri));
                    lines.Add($"LOAD {uri} -> {obj?.GetType().Name}");
                    if (obj is ResourceDictionary rd) {
                        foreach (var k in rd.Keys.Take(60)) {
                            lines.Add($"    {k}");
                        }
                    }
                } catch (Exception e) {
                    lines.Add($"FAIL {uri}: {e.GetType().Name} {e.Message.Split('\n')[0]}");
                }
            }

            // 当前实际生效的控件外观
            var win = new OpenUtau.App.Controls.WindowEx { Width = 400, Height = 300 };
            var panel = new StackPanel();
            var button = new Button { Content = "btn" };
            var textBox = new TextBox { Text = "box" };
            var check = new CheckBox { Content = "chk" };
            var toggle = new ToggleSwitch();
            var combo = new ComboBox();
            var item = new ListBoxItem { Content = "item" };
            panel.Children.Add(button);
            panel.Children.Add(textBox);
            panel.Children.Add(check);
            panel.Children.Add(toggle);
            panel.Children.Add(combo);
            panel.Children.Add(item);
            win.Content = panel;
            win.Show();
            foreach (var c in panel.Children) {
                c.ApplyTemplate();
                if (c is Control ctl) {
                    lines.Add($"{ctl.GetType().Name}: theme={(ctl.Theme?.GetType().Name ?? "null")} " +
                              $"bg={Describe(ctl.GetValue(Panel.BackgroundProperty))} " +
                              $"fg={Describe(ctl.GetValue(TextBlock.ForegroundProperty))} " +
                              $"radius={ctl.GetValue(Border.CornerRadiusProperty)}");
                }
            }
            var tpl = button.Template;
            lines.Add("Button.Template = " + (tpl?.GetType().FullName ?? "null"));
            var textTpl = textBox.Template;
            lines.Add("TextBox.Template = " + (textTpl?.GetType().FullName ?? "null"));

            File.WriteAllLines(OutFile, lines);
            Assert.True(File.Exists(OutFile));
        }

        private static string Describe(object? o) => o switch {
            null => "null",
            Avalonia.Media.ISolidColorBrush b => "#" + b.Color.ToString(),
            _ => o.GetType().Name,
        };
    }
}

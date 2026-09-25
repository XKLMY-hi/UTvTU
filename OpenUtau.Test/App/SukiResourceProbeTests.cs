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
using Avalonia.VisualTree;
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

        /// <summary>
        /// 探针：找出 WindowEx（SukiWindow 派生）里**到底哪一层在画背景**——
        /// 逐个 dump 视觉树中带背景的元素（类型 + 名称 + 尺寸 + 画刷）与所有 Suki 控件。
        /// </summary>
        [AvaloniaFact]
        public void DumpWindowBackgroundLayers() {
            var lines = new List<string>();
            var win = new OpenUtau.App.Controls.WindowEx { Width = 520, Height = 360 };
            win.Content = new TextBlock { Text = "probe" };
            win.Show();
            win.ApplyTemplate();
            lines.Add($"Window: type={win.GetType().Name} bg={Describe(win.Background)}");
            Dump(win, 0, lines);
            File.WriteAllLines(Path.Combine(Path.GetTempPath(), "window-bg-layers.txt"), lines);
            Assert.True(lines.Count > 0);
        }

        /// <summary>
        /// 探针：把 SukiUI 主题/样式里注册的资源键（含画刷值）全列出来——
        /// 白色/浅色的那些就是"背景还是 Suki"的来源，之后用颜色池角色覆盖即可。
        /// </summary>
        [AvaloniaFact]
        public void DumpSukiThemeResourceKeys() {
            var lines = new List<string>();
            if (Application.Current == null) {
                Assert.Fail("no app");
                return;
            }
            foreach (var style in Application.Current.Styles) {
                string tn = style.GetType().FullName ?? style.GetType().Name;
                if (tn.StartsWith("SukiUI") || tn.Contains("Suki")) {
                    lines.Add($"STYLE {tn}");
                    DumpResources(style, lines, "  ");
                }
            }
            // App.Resources 里以 Suki 开头的键
            foreach (var kv in Application.Current.Resources) {
                string k = kv.Key?.ToString() ?? "";
                if (k.Contains("Suki") || k.Contains("Glass") || k.Contains("Card")) {
                    lines.Add($"APP {k} = {Describe(kv.Value)}");
                }
            }
            File.WriteAllLines(Path.Combine(Path.GetTempPath(), "suki-theme-keys.txt"), lines);
            Assert.True(lines.Count > 0);
        }

        private static void DumpResources(object? o, List<string> lines, string pad) {
            if (o is ResourceDictionary rd) {
                foreach (var kv in rd) {
                    lines.Add($"{pad}{kv.Key} = {Describe(kv.Value)}");
                }
            }
            if (o is IResourceNode node && node.TryGetResource("SukiBackground", null, out var v)) {
                lines.Add($"{pad}(SukiBackground) = {Describe(v)}");
            }
            if (o is Avalonia.Styling.Styles styles) {
                foreach (var child in styles) {
                    lines.Add($"{pad}CHILD {child.GetType().FullName}");
                    DumpResources(child, lines, pad + "  ");
                }
            }
        }

        private static void Dump(Avalonia.Visual v, int depth, List<string> lines) {
            foreach (var child in v.GetVisualChildren()) {
                string pad = new string(' ', depth * 2);
                string self = child switch {
                    Border b => $"Border bg={Describe(b.Background)} border={Describe(b.BorderBrush)} r={b.CornerRadius}",
                    Avalonia.Controls.Shapes.Shape s => $"Shape({s.GetType().Name}) fill={Describe(s.Fill)}",
                    Panel p => $"Panel({p.GetType().Name}) bg={Describe(p.Background)}",
                    _ => "",
                };
                bool suki = child.GetType().FullName?.StartsWith("SukiUI") == true;
                if (self.Length > 0 || suki) {
                    string name = (child as Control)?.Name is { Length: > 0 } n ? $" name={n}" : "";
                    string tag = suki ? " [SUKI]" : "";
                    lines.Add($"{pad}{child.GetType().Name}{tag}{name} {self} bounds={child.Bounds.Width:0}x{child.Bounds.Height:0}");
                }
                Dump(child, depth + 1, lines);
            }
        }
    }
}

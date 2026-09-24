using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using OpenUtau.App;
using OpenUtau.App.Views;
using OpenUtau.Core.Theming;
using OpenUtau.Theming;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// 欢迎视图契约测试（内嵌主窗口后的新界面）。
    ///
    /// 两件事必须成立：
    /// 1) 视图能加载（XAML 里所有 StaticResource 图标键可解析）；
    /// 2) 视图用到的所有 DynamicResource 键都能解析——其中颜色键**必须来自 MD3 颜色池**
    ///    （欢迎页是首个迁移到颜色池的界面，禁止再混用旧的 Plus*/Suki 颜色键）。
    /// </summary>
    public class WelcomeViewTests {
        // 视图 XAML 由测试工程以链接文件复制到输出目录（见 OpenUtau.Test.csproj）
        private static string ReadXaml(string fileName) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Views", fileName));

        private static List<string> KeysOf(string xaml, string kind) =>
            Regex.Matches(xaml, "\\{" + kind + " ([A-Za-z0-9_.\\-]+)\\}")
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList();

        [AvaloniaFact]
        public void WelcomeView_Instantiates() {
            ColorPool.Initialize(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, false);
            var view = new WelcomeView();
            Assert.NotNull(view);
            Assert.Null(view.Host);   // 宿主由 MainWindow 注入
        }

        [AvaloniaFact]
        public void MainWindow_HostsWelcomeView_HiddenByDefault() {
            string xaml = ReadXaml("MainWindow.axaml");
            Assert.Contains("x:Name=\"WelcomeHost\"", xaml);
            Assert.Contains("WelcomeHost", xaml);
            // 初始隐藏：由 MainWindow 在构造末尾按"是否带命令行工程文件"决定是否显示
            Assert.Matches("x:Name=\"WelcomeHost\"[^/]*IsVisible=\"False\"", xaml);
        }

        [AvaloniaFact]
        public void AllDynamicResourceKeys_Resolve() {
            ColorPool.Initialize(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, false);
            Application app = Application.Current!;
            var missing = new List<string>();
            foreach (string key in KeysOf(ReadXaml("WelcomeView.axaml"), "DynamicResource")) {
                if (!app.TryFindResource(key, out _)) {
                    missing.Add(key);
                }
            }
            Assert.True(missing.Count == 0, "未解析的动态资源键：" + string.Join(", ", missing));
        }

        [AvaloniaFact]
        public void IconKeys_Resolve() {
            Application app = Application.Current!;
            var missing = new List<string>();
            foreach (string key in KeysOf(ReadXaml("WelcomeView.axaml"), "StaticResource")) {
                if (!app.TryFindResource(key, out _)) {
                    missing.Add(key);
                }
            }
            Assert.True(missing.Count == 0, "未解析的静态资源键：" + string.Join(", ", missing));
        }

        [AvaloniaFact]
        public void ColorKeys_ComeFromMd3Pool_Only() {
            // 契约：欢迎视图的颜色一律取 md3 角色键；其余动态键必须是文案键（字符串资源）。
            // 也就是说：旧颜色键（Plus* / Suki / Fluent 命名空间）在这里一律不允许出现。
            var md3Keys = new HashSet<string>(Enum.GetValues<Md3Role>().Select(ColorPool.Key));
            var md3ColorKeys = new HashSet<string>(Enum.GetValues<Md3Role>().Select(ColorPool.ColorKey));
            var foreign = new List<string>();
            foreach (string key in KeysOf(ReadXaml("WelcomeView.axaml"), "DynamicResource")) {
                if (md3Keys.Contains(key) || md3ColorKeys.Contains(key)) {
                    continue;
                }
                if (key.StartsWith("md3.", StringComparison.Ordinal)) {
                    foreign.Add(key + "（md3 前缀但不在颜色池键集合内）");
                    continue;
                }
                if (!ThemeManager.TryGetString(key, out string? text) || string.IsNullOrEmpty(text)) {
                    foreign.Add(key + "（既不是颜色池键，也不是文案键）");
                }
            }
            Assert.True(foreign.Count == 0, "欢迎视图引用了非法键：" + string.Join(", ", foreign));
        }

        [AvaloniaFact]
        public void TextKeys_ComeFromStringResources() {
            Application app = Application.Current!;
            var dynamicKeys = KeysOf(ReadXaml("WelcomeView.axaml"), "DynamicResource");
            // 视图里的文案键应当能在字符串资源里解析（welcome.* / singers.* / menu.* 等）
            var textKeys = dynamicKeys.Where(k => !k.StartsWith("md3.", StringComparison.Ordinal)).ToList();
            Assert.NotEmpty(textKeys);
            foreach (string key in textKeys) {
                Assert.True(app.TryFindResource(key, out _), $"文案键未解析：{key}");
            }
        }
    }
}

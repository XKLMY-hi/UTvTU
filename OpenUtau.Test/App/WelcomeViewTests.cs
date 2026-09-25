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
    [Collection("Theme")]   // 这些用例会改全局主题/颜色池，串行执行避免互相污染
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
        public void WelcomeView_MatchesDesignGeometry() {
            // 设计稿 1-Welcome：左品牌面板固定宽 + 右启动器自适应（面板按用户要求收窄为悬浮卡片）
            string xaml = ReadXaml("WelcomeView.axaml");
            Assert.Contains("ColumnDefinitions=\"352,*\"", xaml);
            // 品牌卡片实底 = surface-container（与最近工程行同色，用户裁定；稿子的 primary-container 实底不用了）
            Assert.Contains("md3.surface-container", xaml);
            Assert.DoesNotContain("md3.primary-container", xaml);
            // 悬浮圆角卡片：列 352 - 外缩 16×2 = 卡 320，圆角 16（内边距 32 → 内容仍落在 48 基准线）
            Assert.Contains("Margin=\"16\"", xaml);
            Assert.Contains("CornerRadius=\"16\"", xaml);
            // 四个入口卡片：新建 / 打开 / 导入音轨 / 模板
            foreach (string handler in new[] { "OnNewProject", "OnOpenProject", "OnImportAudio", "OnShowTemplates" }) {
                Assert.Contains($"PointerPressed=\"{handler}\"", xaml);
            }
            // 最近工程行用容器底色（稿：bg-[#1A211F]）
            Assert.Contains("md3.surface-container", xaml);
        }

        [AvaloniaFact]
        public void WelcomeView_Motion_ComesFromMotionInterface() {
            // 页面级过渡一律走附加属性接口（时长/缓动由 Md3Motion 令牌给），视图里不许写死秒数或曲线
            string xaml = ReadXaml("WelcomeView.axaml");
            // 品牌卡片自左进入；启动器三段自下进入并 60ms 交错；模板弹层挂载即弹
            Assert.Contains("motion:Motion.Enter=\"FromLeft\"", xaml);
            Assert.Contains("motion:Motion.Enter=\"FromBottom\"", xaml);
            Assert.Contains("motion:Motion.Delay=\"60\"", xaml);
            Assert.Contains("motion:Motion.Delay=\"120\"", xaml);
            Assert.Contains("motion:Motion.Enter=\"Scale\" motion:Motion.AutoPlay=\"True\"", xaml);
            // 悬停微动效不做（2026-09-25 用户否决：只要页面/面板级过渡）
            Assert.DoesNotContain("Motion.Hover", xaml);
            Assert.DoesNotMatch("Duration=\"0:0", xaml);
            Assert.DoesNotMatch("Easing=\"", xaml);
        }

        [AvaloniaFact]
        public void WelcomeView_UsesNoLegacyColorKeys() {
            // 颜色一律来自颜色池：旧的 Plus*/Suki/Fluent 颜色键与硬编码色值都不允许出现
            string xaml = ReadXaml("WelcomeView.axaml");
            foreach (string legacy in new[] { "PlusSurface", "PlusBorder", "SystemControl", "AccentBrush", "NeutralAccent" }) {
                Assert.DoesNotContain(legacy, xaml);
            }
            Assert.DoesNotMatch("#[0-9A-Fa-f]{6}", xaml);
        }

        [AvaloniaFact]
        public void WelcomeView_RecentRows_NotSubjectToGlobalListBoxItemStyle() {
            // 约定：新 MD3 界面不吃旧的全局隐式样式。
            // Styles.axaml 有全局 ListBoxItem{MinHeight=28,Height=28}（旧密度覆盖），
            // 会把设计稿 68px 的最近工程行夹扁 → 欢迎页改用 ScrollViewer + ItemsControl，行自己定尺寸。
            string xaml = ReadXaml("WelcomeView.axaml");
            Assert.DoesNotContain("<ListBox", xaml);
            Assert.Contains("Classes=\"recentRow\"", xaml);
            Assert.Contains("<ItemsControl", xaml);
        }

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
            string xaml = ReadXaml("WelcomeView.axaml");
            // 视图内自定义的键（如弹层外框 ControlTheme）在同文件定义，不走应用资源
            var local = Regex.Matches(xaml, "x:Key=\"([^\"]+)\"")
                .Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);
            var missing = new List<string>();
            foreach (string key in KeysOf(xaml, "StaticResource")) {
                if (local.Contains(key)) {
                    continue;
                }
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

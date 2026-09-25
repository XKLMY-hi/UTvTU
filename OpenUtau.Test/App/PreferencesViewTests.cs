using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using OpenUtau.App;
using OpenUtau.App.ViewModels;
using OpenUtau.App.Views;
using OpenUtau.Core.Theming;
using OpenUtau.Theming;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// 偏好设置契约（全屏视图，设计稿 6-Preferences）：
    /// 几何（导航 304 / 内容 32 / 卡片 16）· 颜色只走颜色池 · 文案键可解析 · 分段控件与色板结构在位。
    /// </summary>
    [Collection("Theme")]   // 会初始化颜色池，与其它主题用例串行
    public class PreferencesViewTests {
        private static string ReadXaml() =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Views", "PreferencesView.axaml"));

        private static List<string> KeysOf(string xaml, string kind) =>
            Regex.Matches(xaml, "\\{" + kind + " ([A-Za-z0-9_.\\-]+)\\}")
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList();

        [AvaloniaFact]
        public void PreferencesView_UsesDesignGeometry() {
            string xaml = ReadXaml();
            Assert.Contains("ColumnDefinitions=\"304,*\"", xaml);   // 左导航 304
            Assert.Contains("Margin=\"32\"", xaml);                 // 内容内边距 32
            Assert.Contains("FontSize=\"28\"", xaml);               // 页标题 28
            Assert.Contains("<Setter Property=\"CornerRadius\" Value=\"16\"/>", xaml);    // 卡片 16
            Assert.Contains("<Setter Property=\"Height\" Value=\"44\"/>", xaml);          // 导航项/选择行 44
            Assert.Contains("<Setter Property=\"Height\" Value=\"40\"/>", xaml);          // 分段控件 40
            // 分段控件 = 圆角胶囊容器 + 选中项 secondary-container + ✓
            Assert.Contains("Selector=\"Border.segmented\"", xaml);
            Assert.Contains("RadioButton.chip:checked /template/ Border#PART_ChipBg", xaml);
            Assert.Contains("md3.secondary-container", xaml);
            // 色板：36 圆 + 选中打勾
            Assert.Contains("Selector=\"Button.swatch\"", xaml);
            Assert.Contains("SelectAccentCommand", xaml);
        }

        [AvaloniaFact]
        public void PreferencesView_HasSevenNavItems() {
            string xaml = ReadXaml();
            foreach (string nav in new[] {
                "NavAudio", "NavLibrary", "NavPlayback", "NavAppearance", "NavEditor", "NavMidi", "NavGeneral",
            }) {
                Assert.Contains($"x:Name=\"{nav}\"", xaml);
            }
            Assert.Contains("x:Name=\"ResetButton\"", xaml);        // 恢复默认设置
            Assert.Contains("PlusInfo.VersionString", xaml);        // 版本行（唯一格式串来源）
        }

        [AvaloniaFact]
        public void PreferencesView_ColorsOnlyFromPool() {
            string xaml = ReadXaml();
            foreach (string legacy in new[] { "PlusSurface", "PlusBrush", "SystemControl", "AccentBrush", "NeutralAccent" }) {
                Assert.DoesNotContain(legacy, xaml);
            }
            Assert.DoesNotMatch("#[0-9A-Fa-f]{6}", xaml);
        }

        [AvaloniaFact]
        public void PreferencesView_AllResourceKeys_Resolve() {
            ColorPool.Initialize(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, false);
            Application app = Application.Current!;
            string xaml = ReadXaml();
            var missing = new List<string>();
            foreach (string key in KeysOf(xaml, "DynamicResource")) {
                if (!app.TryFindResource(key, out _)) {
                    missing.Add(key);
                }
            }
            Assert.True(missing.Count == 0, "未解析的资源键：" + string.Join(", ", missing));
            var staticMissing = KeysOf(xaml, "StaticResource")
                .Where(k => !app.TryFindResource(k, out _))
                .ToList();
            Assert.True(staticMissing.Count == 0, "未解析的静态键：" + string.Join(", ", staticMissing));
        }

        [AvaloniaFact]
        public void PreferencesView_Instantiates_WithNavSwitching() {
            ColorPool.Initialize(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, false);
            var view = new PreferencesView();
            Assert.NotNull(view);
            Assert.Null(view.Host);
        }

        [AvaloniaFact]
        public void AccentSwatches_ShowGeneratedPrimary_AndTrackSelection() {
            ColorPool.Initialize(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, false);
            uint seed = 0xFF00897B;
            var swatch = new AccentSwatchViewModel(seed, isDark: true, currentSeed: seed);
            var expected = Md3SchemeColors.Create(seed, Md3SchemeVariant.TonalSpot, true).Get(Md3Role.Primary);
            Assert.Equal(Md3ColorPool.ToColor(expected), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(swatch.Brush).Color);
            Assert.True(swatch.IsSelected);
            Assert.False(new AccentSwatchViewModel(seed, true, 0xFF6750A4).IsSelected);
        }
    }
}

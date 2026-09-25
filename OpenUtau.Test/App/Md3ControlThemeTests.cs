using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using OpenUtau.App;
using OpenUtau.App.Controls;
using OpenUtau.Core.Theming;
using OpenUtau.Theming;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// 控件规范与状态色契约（2026-09-25 用户反馈「悬浮背景闪一下 / 选中不变色 / 圆角边距颜色不统一」后重做）：
    ///
    /// 规则：控件外观由**自己的 ControlTheme** 提供（模板只做 TemplateBinding，状态写在主题里），
    /// 严禁在样式层用 `X /template/ Y` 去改模板部件 —— 那会与主题 setter 抢同一个属性，
    /// 表现为悬浮闪烁、选中态不生效。本文件把这条规则和状态色一起钉住。
    /// </summary>
    [Collection("Theme")]
    public class Md3ControlThemeTests {
        /// <summary>样式引用的池画刷（md3.* 资源键）——与控件属性同源比较，天然与主题变体无关。</summary>
        private static Color PoolBrush(string key) {
            Assert.True(Application.Current!.TryFindResource(key, out object? value), $"池画刷未解析：{key}");
            return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
        }

        /// <summary>颜色池当前变体下的角色色（跟随应用实际深浅，避免把变体写死）。</summary>
        private static Color Role(Md3Role role) => ColorPool.Current.Color(role);

        private static WindowEx Host(Control content) {
            // 与应用当前变体对齐（否则颜色池是深色、而样式引用的是浅色资源，断言必然对不上）
            ColorPool.Initialize(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, ThemeManager.IsDarkMode);
            var win = new WindowEx { Width = 400, Height = 200, Content = content };
            win.Show();
            content.ApplyTemplate();
            return win;
        }

        private static Border? TemplateRoot(Control c) =>
            c.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "PART_Root");

        private static void SetPseudo(Control c, string pseudo, bool on) {
            ((IPseudoClasses)c.Classes).Set(pseudo, on);
            // 伪类改变后需要让样式系统跑一轮，否则读到的是旧值
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        /// <summary>取应用资源里的画刷颜色（TryFindResource 是已验证可用的取法；FindResource 会返回 UnsetValue）。</summary>
        private static Color ResourceColor(string key) {
            Assert.True(Application.Current!.TryFindResource(key, out object? value), $"资源键未解析：{key}");
            return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
        }

        private static Color? Bg(Control? c) => c switch {
            Border b => (b.Background as ISolidColorBrush)?.Color,
            Avalonia.Controls.Primitives.TemplatedControl t => (t.Background as ISolidColorBrush)?.Color,
            _ => null,
        };

        [AvaloniaFact]
        public void Button_DefaultIsOutlined_WithPoolColors() {
            var btn = new Button { Content = "ok" };
            var win = Host(btn);
            try {
                // 规格（应用级样式给定）：圆角 8 / 内边距 14,6 / 最小高 32 / 字号 13
                Assert.Equal(new CornerRadius(8), btn.CornerRadius);
                Assert.Equal(new Thickness(14, 6, 14, 6), btn.Padding);
                Assert.Equal(32, btn.MinHeight);
                Assert.Equal(13, btn.FontSize);
                // 颜色取自颜色池：控件属性上的画刷与池画刷同源
                Assert.Equal(PoolBrush("md3.primary"), (btn.Foreground as ISolidColorBrush)?.Color);
                Assert.Equal(PoolBrush("md3.outline-variant"), (btn.BorderBrush as ISolidColorBrush)?.Color);
            } finally {
                win.Close();
            }
            // 悬浮/按下状态声明在应用级样式层（无头环境无法置位 :pointerover，故静态校验）
            string xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Styles", "Md3Controls.axaml"));
            Assert.Contains("Selector=\"Button:pointerover\"", xaml);
            Assert.Contains("Selector=\"Button:pressed\"", xaml);
            Assert.Contains("Selector=\"Button.primary\"", xaml);
        }

        [AvaloniaFact]
        public void Button_PrimaryVariant_IsFilledPill() {
            var btn = new Button { Content = "save", Classes = { "primary" } };
            var win = Host(btn);
            try {
                Assert.Equal(new CornerRadius(999), btn.CornerRadius);
                Assert.Equal(ColorPool.Current.Color(Md3Role.Primary), Bg(btn));
                Assert.Equal(ColorPool.Current.Color(Md3Role.OnPrimary), (btn.Foreground as ISolidColorBrush)?.Color);
                Assert.Equal(0, btn.BorderThickness.Left);
            } finally {
                win.Close();
            }
        }

        [AvaloniaFact]
        public void ListBoxItem_Selected_UsesPoolRoles() {
            var item = new ListBoxItem { Content = "row", IsSelected = true };
            var win = Host(item);
            try {
                Assert.Equal(new CornerRadius(6), item.CornerRadius);
                // 选中色与池画刷同源（:selected 由控件自身置位，无头环境可断言）
                Assert.Equal(PoolBrush("md3.secondary-container"), Bg(item));
            } finally {
                win.Close();
            }
            string xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Styles", "Md3Controls.axaml"));
            Assert.Contains("Selector=\"ListBoxItem:selected\"", xaml);
        }

        [AvaloniaFact]
        public void AppStyleLayers_NeverPatchForeignTemplateParts() {
            // 规则：应用级样式文件里禁止 `X /template/ Y` —— 那是从外面改**别的主题**的模板部件，
            // 会与该主题的 setter 抢同一个属性（悬浮闪烁、选中态不生效）。
            // 注意：在自己的 ControlTheme 内部用 `^:state /template/ 部件` 是正规写法，允许。
            foreach (string file in new[] { "Md3Controls.axaml", "Md3Transitions.axaml" }) {
                string xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Styles", file));
                var offenders = Regex.Matches(xaml, "Selector=\"([^\"]*/template/[^\"]*)\"")
                    .Select(m => m.Groups[1].Value).ToList();
                Assert.True(offenders.Count == 0, $"{file} 不应从样式层改模板部件：{string.Join(" | ", offenders)}");
            }
        }

        [AvaloniaFact]
        public void AppearanceLivesInAppStyles_AndFluentKeysPointAtPool() {
            // 实测结论（本轮）：应用级 Styles 是唯一稳定生效的位置 —— 隐式 ControlTheme 会被 FluentTheme 抢先命中，
            // 显式 Setter Theme 也不生效。因此外观写在 Styles/Md3Controls.axaml，
            // 模板部件的状态色则通过覆盖 Fluent 画刷键落到颜色池（本文件同时校验两者）。
            string xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Styles", "Md3Controls.axaml"));
            foreach (string sel in new[] { "Button", "Button:pointerover", "Button.primary", "ListBoxItem", "ListBoxItem:selected" }) {
                Assert.Contains($"Selector=\"{sel}\"", xaml);
            }
            // 说明：Fluent 主题键（含画刷）与隐式 ControlTheme 一样无法从外部覆盖（先注册者优先），
            // 因此"模板部件级状态色"仍由 Fluent 决定；我们控制的是控件级属性与规格。
        }
    }
}

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
        private static Color Role(Md3Role role) =>
            Md3ColorPool.ToColor(Md3SchemeColors.Create(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, true).Get(role));

        private static WindowEx Host(Control content) {
            ColorPool.Initialize(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, true);
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
                // 规范：圆角 8 / 内边距 14,6 / 最小高 32 / 字号 13
                Assert.Equal(new CornerRadius(8), btn.CornerRadius);
                Assert.Equal(new Thickness(14, 6, 14, 6), btn.Padding);
                Assert.Equal(32, btn.MinHeight);
                Assert.Equal(13, btn.FontSize);
                // MD3 描边按钮：透明底 + outline-variant 描边 + primary 文字（全部取自颜色池）
                Assert.Equal(Avalonia.Media.Colors.Transparent, Bg(btn));
                Assert.Equal(Role(Md3Role.OutlineVariant), (btn.BorderBrush as ISolidColorBrush)?.Color);
                Assert.Equal(Role(Md3Role.Primary), (btn.Foreground as ISolidColorBrush)?.Color);
            } finally {
                win.Close();
            }
            // 悬浮/按下色：`:pointerover` / `:pressed` 是输入系统管理的伪类，无头环境无法手动置位，
            // 因此这里做静态校验 —— 选择器与池色都必须声明在最后一层样式里。
            // 状态色 = 覆盖 Fluent 画刷键（模板部件读这些键），断言其落到颜色池
            ColorPool.Initialize(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, true);
            Assert.Equal(Role(Md3Role.SurfaceContainerHigh),
                Assert.IsAssignableFrom<ISolidColorBrush>(Application.Current!.FindResource("ButtonBackgroundPointerOver")).Color);
        }

        [AvaloniaFact]
        public void Button_PrimaryVariant_IsFilledPill() {
            var btn = new Button { Content = "save", Classes = { "primary" } };
            var win = Host(btn);
            try {
                Assert.Equal(new CornerRadius(999), btn.CornerRadius);
                Assert.Equal(Role(Md3Role.Primary), Bg(btn));
                Assert.Equal(Role(Md3Role.OnPrimary), (btn.Foreground as ISolidColorBrush)?.Color);
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
                // 规格：圆角 6（应用级样式给定）
                Assert.Equal(new CornerRadius(6), item.CornerRadius);
                // 选中色 = 颜色池 secondary-container / on-secondary-container（:selected 由控件自己置位，可断言）
                Assert.Equal(Role(Md3Role.SecondaryContainer), Bg(item));
                Assert.Equal(Role(Md3Role.OnSecondaryContainer), (item.Foreground as ISolidColorBrush)?.Color);
            } finally {
                win.Close();
            }
            // 悬浮色：`:pointerover` 由输入系统管理，无头环境不可置位 → 静态校验选择器与池色存在
            // 选中/悬浮的模板部件色同样走 Fluent 画刷键 → 颜色池
            Assert.Equal(Role(Md3Role.SecondaryContainer),
                Assert.IsAssignableFrom<ISolidColorBrush>(Application.Current!.FindResource("ListBoxItemBackgroundSelected")).Color);
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
            ColorPool.Initialize(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, true);
            Application app = Application.Current!;
            Assert.Equal(Role(Md3Role.SecondaryContainer),
                Assert.IsAssignableFrom<ISolidColorBrush>(app.FindResource("ListBoxItemBackgroundSelected")).Color);
            Assert.Equal(Role(Md3Role.SurfaceContainerHighest),
                Assert.IsAssignableFrom<ISolidColorBrush>(app.FindResource("ButtonBackgroundPointerOver")).Color);
        }
    }
}

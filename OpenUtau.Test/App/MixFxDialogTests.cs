using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.App.Views;
using OpenUtau.Core;
using OpenUtau.Core.Theming;
using OpenUtau.Core.Ustx;
using OpenUtau.Theming;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// 三面板实时效果机架对话框（<see cref="MixFxDialog"/>）的行为与契约测试：
    /// · 三块模块面板（电源开关 + 曲线屏 + 旋钮组）在位，模块开关绑**冻结契约**键；
    /// · 非模态提交语义：实时预览 / ESC·取消还原到打开时快照 / 直接关窗保留 / 未动参数不标脏；
    /// · XAML 里用到的资源键（md3 色池 + 文案键）全部可解析，且无字面量色值。
    /// </summary>
    [Collection("Theme")]
    public class MixFxDialogTests {
        static void UsePool() =>
            ColorPool.Initialize(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, true);

        static UTrack NewTrack(UMixFx? fx = null) =>
            new UTrack { TrackNo = 0, TrackName = "Rack Track", MixFx = fx };

        static string DialogXaml() =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Views", "MixFxDialog.axaml"));

        static List<string> KeysOf(string xaml, string kind) =>
            Regex.Matches(xaml, "\\{" + kind + " ([A-Za-z0-9_.\\-]+)\\}")
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList();

        // ══════════════════════ 结构 ══════════════════════

        [AvaloniaFact]
        public void Dialog_BuildsThreeModulePanels_WithPowerCurveAndKnobs() {
            UsePool();
            var dialog = new MixFxDialog(NewTrack(new UMixFx()));
            try {
                dialog.Show();
                var visual = dialog.GetVisualDescendants().ToList();

                // 三个模块卡（EQ / 压缩 / 混响）
                var modules = visual.OfType<Border>().Where(b => b.Classes.Contains("module")).ToList();
                Assert.Equal(3, modules.Count);
                // 每块都有自己的曲线屏
                Assert.Single(visual.OfType<EqCurveDisplay>());
                Assert.Single(visual.OfType<CompCurveDisplay>());
                Assert.Single(visual.OfType<ReverbCurveDisplay>());
                // 旋钮数量：EQ 4（低/中/高/频点）+ 压缩 3（阈值/比率/补偿）+ 混响 4（大小/阻尼/预延迟/湿声）
                Assert.Equal(11, visual.OfType<Knob>().Count());
                // 开关数量：3 个模块电源 + 顶栏"导出时套用" + 顶栏"总电源"
                Assert.Equal(5, visual.OfType<ToggleButton>().Count());
                Assert.Equal(3, visual.OfType<ToggleButton>().Count(t => t.Classes.Contains("compact")));
            } finally {
                dialog.Close();
            }
        }

        [AvaloniaFact]
        public void Dialog_ModuleSwitches_BindFrozenCanonicalKeys() {
            UsePool();
            string xaml = DialogXaml();
            // 冻结契约：模块开关只绑 EqEnabled / CompEnabled / ReverbEnabled
            Assert.Contains("IsChecked=\"{Binding EqEnabled}\"", xaml);
            Assert.Contains("IsChecked=\"{Binding CompEnabled}\"", xaml);
            Assert.Contains("IsChecked=\"{Binding ReverbEnabled}\"", xaml);
            // 旧的反向别名不得再用于绑定
            Assert.DoesNotContain("EqBypassed", xaml);
            Assert.DoesNotContain("CompBypassed", xaml);
            Assert.DoesNotContain("ReverbBypassed", xaml);
        }

        // ══════════════════════ 实时预览与提交语义 ══════════════════════

        [AvaloniaFact]
        public void EditsArePreviewedLive_OnTheTrack() {
            UsePool();
            var track = NewTrack(new UMixFx { Enabled = true, EqLowDb = 0 });
            var dialog = new MixFxDialog(track);
            try {
                dialog.Show();
                var vm = Assert.IsType<MixFxViewModel>(dialog.DataContext);
                Assert.NotNull(track.MixFx);
                double before = track.MixFx!.EqLowDb;

                vm.EqLowDb = 7.5;
                Assert.NotEqual(before, track.MixFx!.EqLowDb);
                Assert.Equal(7.5, track.MixFx.EqLowDb);

                // 模块开关走冻结契约键 → 直接落到模型上
                vm.EqEnabled = false;
                Assert.False(track.MixFx.EqEnabled);
                vm.CompEnabled = false;
                Assert.False(track.MixFx.CompEnabled);
                vm.ReverbEnabled = false;
                Assert.False(track.MixFx.ReverbEnabled);

                // 总电源
                vm.Enabled = false;
                Assert.False(track.MixFx.Enabled);
            } finally {
                dialog.Close();
            }
        }

        [AvaloniaFact]
        public void Escape_RestoresTheSnapshotFromWhenTheWindowOpened() {
            UsePool();
            var snapshot = new UMixFx { Enabled = true, EqEnabled = false, EqLowDb = 2.5, CompRatio = 3 };
            var track = NewTrack(snapshot);
            var dialog = new MixFxDialog(track);
            try {
                dialog.Show();
                var vm = Assert.IsType<MixFxViewModel>(dialog.DataContext);

                // 改一堆参数（实时预览会换掉 track.MixFx 的引用）
                vm.EqLowDb = 9.0;
                vm.EqEnabled = true;
                vm.CompRatio = 12;
                Assert.NotSame(snapshot, track.MixFx);

                // ESC = 取消
                dialog.RaiseEvent(new KeyEventArgs {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Escape,
                });

                // 引用与取值都回到打开时的快照
                Assert.Same(snapshot, track.MixFx);
                Assert.Equal(2.5, track.MixFx!.EqLowDb);
                Assert.False(track.MixFx.EqEnabled);
                Assert.Equal(3, track.MixFx.CompRatio);
            } finally {
                dialog.Close();
            }
        }

        [AvaloniaFact]
        public void ClosingWithoutCancel_KeepsTheEdits() {
            UsePool();
            var snapshot = new UMixFx { Enabled = true, EqLowDb = 0 };
            var track = NewTrack(snapshot);
            var dialog = new MixFxDialog(track);
            try {
                dialog.Show();
                var vm = Assert.IsType<MixFxViewModel>(dialog.DataContext);
                vm.EqLowDb = 6.0;
                var previewed = track.MixFx;
                Assert.True(vm.IsDirty);

                dialog.Close();   // 等价点标题栏 ×（保留改动）

                Assert.Same(previewed, track.MixFx);
                Assert.Equal(6.0, track.MixFx!.EqLowDb);
            } finally {
                // 已经关过了
            }
        }

        [AvaloniaFact]
        public void UntouchedRack_IsNotDirty_AndKeepsTrackStateAsIs() {
            UsePool();
            var snapshot = new UMixFx { Enabled = true, EqLowDb = 1.25 };
            var track = NewTrack(snapshot);
            var dialog = new MixFxDialog(track);
            try {
                dialog.Show();
                var vm = Assert.IsType<MixFxViewModel>(dialog.DataContext);

                // 打开时不写回：快照引用与取值都不变 → 关窗不会把工程标脏
                Assert.False(vm.IsDirty);
                Assert.Same(snapshot, track.MixFx);
                Assert.Equal(1.25, track.MixFx!.EqLowDb);

                dialog.Close();
                Assert.False(vm.IsDirty);
                Assert.Same(snapshot, track.MixFx);
            } finally {
                // 已经关过了
            }
        }

        [AvaloniaFact]
        public void TrackWithoutMixFx_StaysNullUntilSomethingIsActuallyEdited() {
            UsePool();
            var track = NewTrack(null);
            var dialog = new MixFxDialog(track);
            try {
                dialog.Show();
                var vm = Assert.IsType<MixFxViewModel>(dialog.DataContext);
                // 未编辑 → 不凭空创建 UMixFx（保持"零开销旁通"语义）
                Assert.Null(track.MixFx);
                Assert.False(vm.Enabled);

                vm.EqLowDb = 3.0;   // 编辑后才具现化
                Assert.NotNull(track.MixFx);
                Assert.Equal(3.0, track.MixFx!.EqLowDb);
            } finally {
                dialog.Close();
            }
        }

        [AvaloniaFact]
        public void Revert_WithNoOriginalFx_PutsNullBack() {
            UsePool();
            var track = NewTrack(null);
            var dialog = new MixFxDialog(track);
            try {
                dialog.Show();
                var vm = Assert.IsType<MixFxViewModel>(dialog.DataContext);
                vm.EqLowDb = 5.0;
                Assert.NotNull(track.MixFx);

                vm.Revert();
                Assert.Null(track.MixFx);   // 打开时就是 null → 还原成 null
            } finally {
                dialog.Close();
            }
        }

        // ══════════════════════ XAML 契约（把人工自检固化成断言） ══════════════════════

        [AvaloniaFact]
        public void DialogXaml_AllResourceKeys_Resolve() {
            ColorPool.Initialize(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, false);
            Application app = Application.Current!;
            string xaml = DialogXaml();

            var local = Regex.Matches(xaml, "x:Key=\"([^\"]+)\"")
                .Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);

            var missing = KeysOf(xaml, "DynamicResource")
                .Where(k => !local.Contains(k) && !app.TryFindResource(k, out _))
                .ToList();
            Assert.True(missing.Count == 0, "未解析的资源键：" + string.Join(", ", missing));

            var staticMissing = KeysOf(xaml, "StaticResource")
                .Where(k => !local.Contains(k) && !app.TryFindResource(k, out _))
                .ToList();
            Assert.True(staticMissing.Count == 0, "未解析的静态键：" + string.Join(", ", staticMissing));
        }

        [AvaloniaFact]
        public void DialogXaml_ColorsOnlyFromTheMd3Pool_AndNoLiteralColors() {
            string xaml = DialogXaml();
            // 无字面量色值（与 PreferencesViewTests 同款口径）
            Assert.DoesNotMatch("#[0-9A-Fa-f]{6}", xaml);
            Assert.DoesNotContain("Color.Parse", xaml);
            // 模块强调色与结构色一律走色池
            Assert.Contains("md3.color.primary", xaml);
            Assert.Contains("md3.color.tertiary", xaml);
            Assert.Contains("md3.color.secondary", xaml);
            Assert.Contains("{DynamicResource md3.surface-container-low}", xaml);
            Assert.Contains("{DynamicResource md3.outline-variant}", xaml);
            // 不再使用旧令牌
            foreach (string legacy in new[] { "SystemControl", "AccentBrush", "NeutralAccent", "PlusBrush" }) {
                Assert.DoesNotContain(legacy, xaml);
            }
            // 实色红线：不引入玻璃/半透明/模糊
            foreach (string glass in new[] { "BlurEffect", "Opacity=\"0.", "TransparencyLevelHint" }) {
                Assert.DoesNotContain(glass, xaml);
            }
            // 零动画策略 → "减少动效"天然成立
            Assert.DoesNotContain("<Transitions>", xaml);
            Assert.DoesNotContain("<Style.Animations>", xaml);
        }

        [AvaloniaFact]
        public void Dialog_KeyboardFocusVisuals_ForOwnControls() {
            UsePool();
            string xaml = DialogXaml();
            // 自研开关（内联模板，不属于 fx-ctl 的主题范围）必须有焦点环
            Assert.Contains("ToggleButton.md3switch:focus-visible /template/ Border#SwitchTrack", xaml);
            // 旋钮是键盘可操作的（方向键 / Home / End / PageUp·Down）→ 必须可聚焦
            Assert.True(new Knob().Focusable, "Knob 必须可聚焦，否则键盘改参不可达");
            // 曲线屏是纯显示，不参与焦点（没有需要焦点视觉的部件）
            Assert.False(new EqCurveDisplay().Focusable, "曲线屏是纯显示控件，不应抢焦点");
        }

        [AvaloniaFact]
        public void DialogXaml_UsesLocalizedKeysForEveryUserVisibleString() {
            string xaml = DialogXaml();
            foreach (string key in new[] {
                "mixfx.caption", "mixfx.rack.track", "mixfx.rack.power", "mixfx.rack.power.tip",
                "mixfx.rack.module.tip", "mixfx.rack.dry", "mixfx.rack.hint",
                "mixfx.applyonexport", "mixfx.recommended", "mixfx.library",
                "mixfx.library.save", "mixfx.library.delete", "button.ok", "button.cancel",
                "mixfx.eq", "mixfx.compressor", "mixfx.reverb",
                "mixfx.eq.low", "mixfx.eq.mid", "mixfx.eq.high", "mixfx.eq.midfreq",
                "mixfx.comp.threshold", "mixfx.comp.ratio", "mixfx.comp.makeup",
                "mixfx.reverb.size", "mixfx.reverb.damp", "mixfx.reverb.wet", "mixfx.reverb.predelay",
            }) {
                Assert.Contains(key, xaml);
            }
            // 没有裸写的英文界面文案（Control 名/属性名之外不出现 Title="EQ" 这类硬编码）
            Assert.DoesNotContain("Text=\"EQ\"", xaml);
            Assert.DoesNotContain("Text=\"COMPRESSOR\"", xaml);
            Assert.DoesNotContain("Text=\"REVERB\"", xaml);
        }

        [AvaloniaFact]
        public void DialogXaml_StylesAreScopedInline_NoAppOrStylesFileNeeded() {
            string xaml = DialogXaml();
            // 样式全部内联在窗口里（不新增 Styles 文件、不动 App.axaml）
            Assert.Contains("<Window.Styles>", xaml);
            Assert.Contains("x:Class=\"OpenUtau.App.Views.MixFxDialog\"", xaml);
            // 纯类选择器必须带 x:SetterTargetType（本仓 Avalonia 12 约定）——
            // 这里断言的是"没有裸的纯类选择器"：所有选择器都以类型名开头。
            foreach (Match m in Regex.Matches(xaml, "Selector=\"([^\"]+)\"")) {
                string selector = m.Groups[1].Value;
                if (selector.StartsWith("Window.") || selector.Contains(" ")) {
                    continue;   // 后代/窗口限定选择器
                }
                Assert.False(selector.StartsWith("."),
                    $"纯类选择器缺少类型限定或 x:SetterTargetType：{selector}");
            }
        }
    }
}

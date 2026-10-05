using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using ShapePath = Avalonia.Controls.Shapes.Path;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App;
using OpenUtau.App.Controls;
using OpenUtau.App.Views;
using OpenUtau.Core.Theming;
using OpenUtau.Core.Util;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Vst;
using OpenUtau.Test.TestSupport;
using OpenUtau.Theming;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// 机架控件家族契约（fx-rack）：
    ///   · 模板结构完整（PART_* 部件在位）；
    ///   · 关键属性生效（IsChecked / SelectedIndex / Value / IsPowered / Track …）；
    ///   · 键盘可达（RaiseEvent KeyDown 走真实的 OnKeyDown 路径）；
    ///   · **颜色只来自颜色池**（运行时把控件树下所有画刷与角色色集合比对）；
    ///   · 陈列室窗口不依赖运行中的工程即可构造，且五个控件都在场；
    ///   · 新增文案键 EN/zh 双字典都在（zh 值不同 ⇒ 不是回退到英文）。
    /// </summary>
    [Collection("Theme")]   // 会初始化颜色池/切语言，与其它主题用例串行
    public class ControlGalleryTests {
        /// <summary>本轮新增的字符串键（fx-rack 区块；EN/zh 各一份）。</summary>
        private static readonly string[] NewKeys = {
            "menu.tools.controlgallery",
            "controlgallery.title", "controlgallery.caption", "controlgallery.states",
            "controlgallery.section.power", "controlgallery.section.segmented",
            "controlgallery.section.readout", "controlgallery.section.card", "controlgallery.section.rack",
            "controlgallery.note.power", "controlgallery.note.segmented", "controlgallery.note.readout",
            "controlgallery.note.card", "controlgallery.note.rack",
            "controlgallery.state.default", "controlgallery.state.hover", "controlgallery.state.pressed",
            "controlgallery.state.active", "controlgallery.state.disabled", "controlgallery.state.bypass",
            "controlgallery.demo.note", "controlgallery.demo.gain", "controlgallery.demo.freq", "controlgallery.demo.mix",
            "fxrack.title", "fxrack.readonly", "fxrack.hint.drag", "fxrack.empty", "fxcard.bypass",
        };

        // ── 夹具 ────────────────────────────────────────────────────────────

        /// <summary>颜色池与应用当前深浅色对齐（否则控件取深色、断言取浅色，必然对不上）。</summary>
        private static void SyncPool() =>
            ColorPool.Initialize(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, ThemeManager.IsDarkMode);

        private static WindowEx Host(Control content, double width = 1200, double height = 900) {
            SyncPool();
            var win = new WindowEx { Width = width, Height = height, Content = content };
            // 冻结动效：带 .no-motion 的窗口里控件不挂 Transitions ⇒ 状态色改动**同步**可读
            //（否则 BrushTransition 会插值，读到的还是起始色）。这同时验证 no-motion 契约。
            win.Classes.Set("no-motion", true);
            win.Show();
            Layout(win, width, height);
            return win;
        }

        /// <summary>强制一轮布局：嵌套控件（卡片里的开关、机架行）的模板要布局后才落进视觉树。</summary>
        private static void Layout(Window win, double width, double height) {
            win.Measure(new Size(width, height));
            win.Arrange(new Rect(0, 0, width, height));
            Dispatcher.UIThread.RunJobs();
        }

        private static T Part<T>(Visual root, string name) where T : Visual =>
            root.GetVisualDescendants().OfType<T>().First(v => (v as INamed)?.Name == name);

        private static Color ColorOf(IBrush brush) =>
            Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

        private static Color Pool(Md3Role role) => ColorPool.Current.Color(role);

        private static UTrack DemoTrack() {
            var track = new UTrack { TrackNo = 0, TrackName = "Lead Vocal", TrackColor = "Blue" };
            track.MixFx = new UMixFx {
                Enabled = true,
                EqEnabled = true,
                CompEnabled = false,
                ReverbEnabled = true,
                EqPreset = "vocal_air",
                CompPreset = "gentle",
                ReverbPreset = "small_room",
            };
            track.VstSlots.Add(new VstPluginSlot(0) { PluginUid = "OTT" });
            track.VstSlots.Add(new VstPluginSlot(1));
            return track;
        }

        private static KeyEventArgs KeyDown(Key key, KeyModifiers modifiers = KeyModifiers.None) => new() {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
        };

        // ── 1. 电源开关 ─────────────────────────────────────────────────────

        [AvaloniaFact]
        public void PowerSwitch_TemplateAndCheckedColors_FromPool() {
            var sw = new Md3PowerSwitch();
            var win = Host(sw);
            try {
                var body = Part<Border>(sw, "PART_Circle");
                var glyph = Part<ShapePath>(sw, "PART_Glyph");
                Assert.NotNull(Part<Panel>(sw, "PART_Root"));
                Assert.NotNull(Part<Ellipse>(sw, "PART_Focus"));
                Assert.NotNull(Part<Ellipse>(sw, "PART_StateLayer"));
                Assert.True(sw.Focusable, "键盘可达：必须可聚焦");

                // 关：surface-container-highest 底 + outline-variant 描边 + outline 符号
                Assert.False(sw.IsChecked);
                Assert.Equal(Pool(Md3Role.SurfaceContainerHighest), ColorOf(body.Background));
                Assert.Equal(Pool(Md3Role.OutlineVariant), ColorOf(body.BorderBrush));
                Assert.Equal(Pool(Md3Role.Outline), ColorOf(glyph.Fill));

                // 开：primary 底 + on-primary 符号（点亮）
                sw.IsChecked = true;
                Assert.Equal(Pool(Md3Role.Primary), ColorOf(body.Background));
                Assert.Equal(Pool(Md3Role.OnPrimary), ColorOf(glyph.Fill));
                Assert.True(sw.Classes.Contains(":checked"), "开状态应挂 :checked 伪类");
            } finally {
                win.Close();
            }
        }

        [AvaloniaFact]
        public void PowerSwitch_ForcedVisuals_AreDistinct() {
            var sw = new Md3PowerSwitch();
            var win = Host(sw);
            try {
                var body = Part<Border>(sw, "PART_Circle");
                var layer = Part<Ellipse>(sw, "PART_StateLayer");
                var ring = Part<Ellipse>(sw, "PART_Focus");

                sw.ForcedVisual = Md3RackVisual.Hover;
                Assert.Equal(Pool(Md3Role.Outline), ColorOf(body.BorderBrush));   // 描边升档
                Assert.True(layer.IsVisible);
                Assert.Equal(0.08, layer.Opacity, 3);                              // MD3 状态层 8%

                sw.ForcedVisual = Md3RackVisual.Pressed;
                Assert.Equal(Pool(Md3Role.SurfaceContainerHigh), ColorOf(body.Background));
                Assert.Equal(0.12, layer.Opacity, 3);                              // 12%

                sw.ForcedVisual = Md3RackVisual.Focus;
                Assert.True(ring.IsVisible);
                Assert.Equal(Pool(Md3Role.Primary), ColorOf(ring.Stroke));

                sw.ForcedVisual = Md3RackVisual.Disabled;
                Assert.Equal(0.38, sw.Opacity, 3);                                 // MD3 disabled 规格

                sw.ForcedVisual = Md3RackVisual.Bypassed;
                Assert.Equal(0.65, sw.Opacity, 3);
                Assert.Equal(Pool(Md3Role.SurfaceContainer), ColorOf(body.Background));

                sw.ForcedVisual = null;
                Assert.Equal(1.0, sw.Opacity, 3);
            } finally {
                win.Close();
            }
        }

        [AvaloniaFact]
        public void PowerSwitch_KeyboardTogglesAndRaisesEvent() {
            var sw = new Md3PowerSwitch();
            var win = Host(sw);
            try {
                var raised = new List<bool>();
                sw.Toggled += (_, v) => raised.Add(v);

                sw.RaiseEvent(KeyDown(Key.Space));
                Assert.True(sw.IsChecked);
                sw.RaiseEvent(KeyDown(Key.Space));
                Assert.False(sw.IsChecked);
                sw.RaiseEvent(KeyDown(Key.Enter));
                Assert.True(sw.IsChecked);
                sw.RaiseEvent(KeyDown(Key.Left));
                Assert.False(sw.IsChecked);
                sw.RaiseEvent(KeyDown(Key.Right));
                Assert.True(sw.IsChecked);
                Assert.Equal(new[] { true, false, true, false, true }, raised);
            } finally {
                win.Close();
            }
        }

        // ── 2. 分段选择器 ───────────────────────────────────────────────────

        [AvaloniaFact]
        public void Segmented_BuildsSegmentPerOption_AndPaintsSelection() {
            var seg = new Md3SegmentedControl();
            seg.SetOptions("Low", "Mid", "High");
            var win = Host(seg);
            try {
                var segments = seg.GetVisualDescendants().OfType<Border>()
                    .Where(b => (b as INamed)?.Name == "PART_Segment").ToList();
                Assert.Equal(3, segments.Count);
                Assert.Equal("Low", Part<TextBlock>(seg, "PART_Text").Text);
                Assert.Equal(0, seg.SelectedIndex);
                Assert.Equal("Low", seg.SelectedOption);

                // 选中项 = secondary-container 胶囊
                Assert.Equal(Pool(Md3Role.SecondaryContainer), ColorOf(segments[0].Background));
                Assert.Equal(Pool(Md3Role.SurfaceContainerHigh), ColorOf(segments[1].Background));

                seg.SelectedIndex = 2;
                Assert.Equal(Pool(Md3Role.SecondaryContainer), ColorOf(segments[2].Background));
                Assert.Equal(Pool(Md3Role.SurfaceContainerHigh), ColorOf(segments[0].Background));
                var lowText = seg.GetVisualDescendants().OfType<TextBlock>()
                    .First(t => (t as INamed)?.Name == "PART_Text" && t.Text == "Low");
                var highText = seg.GetVisualDescendants().OfType<TextBlock>()
                    .First(t => (t as INamed)?.Name == "PART_Text" && t.Text == "High");
                Assert.Equal(Pool(Md3Role.OnSurfaceVariant), ColorOf(lowText.Foreground));
                Assert.Equal(Pool(Md3Role.OnSecondaryContainer), ColorOf(highText.Foreground));
            } finally {
                win.Close();
            }
        }

        [AvaloniaFact]
        public void Segmented_KeyboardMovesSelection_AndOptionsAreCoerced() {
            var seg = new Md3SegmentedControl();
            seg.SetOptions("A", "B", "C", "D");
            var win = Host(seg);
            try {
                var seen = new List<int>();
                seg.SelectionChanged += (_, i) => seen.Add(i);

                seg.RaiseEvent(KeyDown(Key.Right));
                Assert.Equal(1, seg.SelectedIndex);
                seg.RaiseEvent(KeyDown(Key.End));
                Assert.Equal(3, seg.SelectedIndex);
                seg.RaiseEvent(KeyDown(Key.Right));
                Assert.Equal(0, seg.SelectedIndex);          // 环形
                seg.RaiseEvent(KeyDown(Key.Left));
                Assert.Equal(3, seg.SelectedIndex);
                seg.RaiseEvent(KeyDown(Key.Home));
                Assert.Equal(0, seg.SelectedIndex);
                Assert.Equal(new[] { 1, 3, 0, 3, 0 }, seen);

                // 越界夹取；空集合 ⇒ -1
                seg.SelectedIndex = 99;
                Assert.Equal(3, seg.SelectedIndex);
                seg.Options = Array.Empty<string>();
                Assert.Equal(-1, seg.SelectedIndex);
                Assert.Null(seg.SelectedOption);
            } finally {
                win.Close();
            }
        }

        [AvaloniaFact]
        public void Segmented_DisabledAndCompact_AreHonoured() {
            var seg = new Md3SegmentedControl { IsCompact = true, IsEnabled = false };
            seg.SetOptions("On", "Off");
            var win = Host(seg);
            try {
                var root = Part<Border>(seg, "PART_Root");
                Assert.Equal(32, root.MinHeight);          // 紧凑规格
                Assert.Equal(0.38, seg.Opacity, 3);        // 禁用规格
                Assert.True(seg.Classes.Contains(":disabled"));
            } finally {
                win.Close();
            }
        }

        // ── 3. 参数读数 ─────────────────────────────────────────────────────

        [AvaloniaFact]
        public void Readout_CoercesFormatsAndDrags() {
            var readout = new Md3NumericReadout {
                Label = "Gain",
                Unit = "dB",
                Minimum = -12,
                Maximum = 12,
                Format = "+0.0;-0.0;0.0",
                Step = 0.1,
                Value = 99,
            };
            var win = Host(readout);
            try {
                Assert.Equal(12, readout.Value);           // 构造值被夹到上限
                readout.Value = -99;
                Assert.Equal(-12, readout.Value);
                readout.Value = 1.5;
                Assert.Equal("+1.5 dB", readout.DisplayText);
                Assert.Equal("+1.5", Part<TextBlock>(readout, "PART_Value").Text);
                Assert.Equal("dB", Part<TextBlock>(readout, "PART_Unit").Text);
                Assert.True(Part<TextBlock>(readout, "PART_Label").IsVisible);
                Assert.True(Part<TextBlock>(readout, "PART_Unit").IsVisible);

                // 拖拽：范围 24 ⇒ 自动灵敏度 0.12/px；向上 50px ⇒ +6
                readout.BeginDrag(new Point(0, 0));
                readout.DragTo(new Point(0, -50));
                Assert.Equal(7.5, readout.Value, 3);
                readout.DragTo(new Point(0, 500));         // 向下 ⇒ 递减并被夹到下限
                Assert.Equal(-12, readout.Value, 3);
                readout.EndDrag();
                Assert.Equal(1.0, readout.Opacity, 3);     // 拖拽结束回到常态视觉
            } finally {
                win.Close();
            }
        }

        [AvaloniaFact]
        public void Readout_KeyboardStepsLimitsAndForcedVisuals() {
            var readout = new Md3NumericReadout { Minimum = -12, Maximum = 12, Step = 1, Value = 0, Unit = "dB" };
            var win = Host(readout);
            try {
                var body = Part<Border>(readout, "PART_Root");
                readout.RaiseEvent(KeyDown(Key.Up));
                Assert.Equal(1, readout.Value, 3);
                readout.RaiseEvent(KeyDown(Key.Down, KeyModifiers.Shift));   // Shift = 1/10 精调
                Assert.Equal(0.9, readout.Value, 3);
                readout.RaiseEvent(KeyDown(Key.PageUp));                     // 粗调 10×
                Assert.Equal(10.9, readout.Value, 3);
                readout.RaiseEvent(KeyDown(Key.End));
                Assert.Equal(12, readout.Value, 3);
                readout.RaiseEvent(KeyDown(Key.Home));
                Assert.Equal(-12, readout.Value, 3);

                readout.ForcedVisual = Md3RackVisual.Hover;
                Assert.Equal(Pool(Md3Role.SurfaceContainerHighest), ColorOf(body.Background));
                Assert.Equal(Pool(Md3Role.Outline), ColorOf(body.BorderBrush));
                readout.ForcedVisual = Md3RackVisual.Pressed;
                Assert.Equal(Pool(Md3Role.Primary), ColorOf(body.BorderBrush));
                Assert.Equal(Pool(Md3Role.Primary), ColorOf(Part<TextBlock>(readout, "PART_Value").Foreground));
                readout.ForcedVisual = Md3RackVisual.Focus;
                Assert.True(Part<Border>(readout, "PART_Focus").IsVisible);
                readout.ForcedVisual = Md3RackVisual.Disabled;
                Assert.Equal(0.38, readout.Opacity, 3);
            } finally {
                win.Close();
            }
        }

        // ── 4. 模块面板 ─────────────────────────────────────────────────────

        [AvaloniaFact]
        public void ModuleCard_StatesBypassChipAndAccentGradient() {
            var content = new TextBlock { Text = "params" };
            var card = new FxModuleCard {
                Title = "EQ",
                Subtitle = "vocal_air",
                Accent = Md3Role.Primary,
                CardContent = content,
            };
            var win = Host(card);
            try {
                var root = Part<Border>(card, "PART_Root");
                var accent = Part<Border>(card, "PART_Accent");
                var chip = Part<Border>(card, "PART_StateChip");
                var chipText = Part<TextBlock>(card, "PART_StateText");
                var body = Part<Border>(card, "PART_Body");

                // 默认：卡片底色 + 内容区可见
                Assert.Equal(Pool(Md3Role.SurfaceContainer), ColorOf(root.Background));
                Assert.Equal(Pool(Md3Role.OutlineVariant), ColorOf(root.BorderBrush));
                Assert.False(chip.IsVisible);
                Assert.True(body.IsVisible);
                Assert.Equal("EQ", Part<TextBlock>(card, "PART_Title").Text);
                Assert.True(Part<TextBlock>(card, "PART_Subtitle").IsVisible);

                // 强调色条 = 模块角色色（旁通时褪色为 outline-variant）
                Assert.Equal(Pool(Md3Role.Primary), ColorOf(accent.Background));

                // 选中：底色升 high + 描边 primary
                card.IsSelected = true;
                Assert.Equal(Pool(Md3Role.SurfaceContainerHigh), ColorOf(root.Background));
                Assert.Equal(Pool(Md3Role.Primary), ColorOf(root.BorderBrush));

                // 旁通：底色降 low + 角标"旁通" + 内容 50% + 色条褪色
                card.IsSelected = false;
                card.IsBypassed = true;
                Assert.Equal(Pool(Md3Role.SurfaceContainerLow), ColorOf(root.Background));
                Assert.True(chip.IsVisible);
                Assert.Equal(ThemeManager.GetString("fxcard.bypass"), chipText.Text);
                Assert.Equal(0.5, body.Opacity, 3);
                Assert.Equal(Pool(Md3Role.OutlineVariant), ColorOf(accent.Background));

                // 电源关：旁通角标换成"关"
                card.IsBypassed = false;
                card.IsPowered = false;
                Assert.True(chip.IsVisible);
                Assert.Equal(ThemeManager.GetString("effects.off"), chipText.Text);
            } finally {
                win.Close();
            }
        }

        [AvaloniaFact]
        public void ModuleCard_PowerSwitchBubblesToggle() {
            var card = new FxModuleCard { Title = "Comp", CardContent = new TextBlock { Text = "x" } };
            var win = Host(card);
            try {
                var toggled = new List<bool>();
                card.PowerToggled += (_, v) => toggled.Add(v);
                var power = Part<Md3PowerSwitch>(card, "PART_Power");
                Assert.True(power.IsChecked);
                power.IsChecked = false;
                Assert.False(card.IsPowered);
                Assert.Equal(new[] { false }, toggled);
            } finally {
                win.Close();
            }
        }

        // ── 5. 机架列表（只读设计稿） ───────────────────────────────────────

        [AvaloniaFact]
        public void RackPanel_ProjectsBuiltInAndVstIntoOneOrder() {
            var track = DemoTrack();
            var panel = new FxRackPanel { Track = track };
            var win = Host(panel);
            try {
                Assert.Equal(5, panel.Entries.Count);
                Assert.Equal(new[] { 1, 2, 3, 4, 5 }, panel.Entries.Select(e => e.Order));
                Assert.Equal(new[] { "EQ", "COMP", "REV", "VST", "VST" }, panel.Entries.Select(e => e.Kind));
                Assert.Equal(new[] { "mixfx.eq", "mixfx.compressor", "mixfx.reverb" },
                    panel.Entries.Where(e => e.IsBuiltIn).Select(e => e.TitleKey));
                Assert.Equal("vocal_air", panel.Entries[0].Detail);
                Assert.True(panel.Entries[0].IsPowered);
                Assert.False(panel.Entries[1].IsPowered);       // 压缩器关
                Assert.True(panel.Entries[3].IsPowered);        // OTT 已加载
                Assert.False(panel.Entries[3].IsEmptySlot);
                Assert.True(panel.Entries[4].IsEmptySlot);      // 空槽
                Assert.Equal(1, panel.Entries[4].SlotIndex);

                // 行数 = 条目数；内置模块名走字符串键（当前语言已解析）
                var rows = panel.GetVisualDescendants().OfType<Border>()
                    .Where(b => (b as INamed)?.Name == "PART_Row").ToList();
                Assert.Equal(5, rows.Count);
                Assert.Equal(ThemeManager.GetString("mixfx.eq"),
                    panel.GetVisualDescendants().OfType<TextBlock>()
                        .First(t => (t as INamed)?.Name == "PART_RowTitle").Text);
                Assert.Equal(ThemeManager.GetString("effects.emptyslot"),
                    panel.GetVisualDescendants().OfType<TextBlock>()
                        .Last(t => (t as INamed)?.Name == "PART_RowTitle").Text);

                // 总开关角标 = 真实 Enabled 状态
                Assert.Equal(ThemeManager.GetString("effects.on"),
                    Part<TextBlock>(panel, "PART_MasterText").Text);
                Assert.Equal(Pool(Md3Role.SecondaryContainer), ColorOf(Part<Border>(panel, "PART_MasterChip").Background));

                // 关总开关 ⇒ 内置行全部转旁通（真实语义：整条链旁通）
                track.MixFx!.Enabled = false;
                panel.Rebuild();
                Assert.All(panel.Entries.Where(e => e.IsBuiltIn), e => Assert.True(e.IsBypassed));
                Assert.Equal(ThemeManager.GetString("effects.off"),
                    Part<TextBlock>(panel, "PART_MasterText").Text);
            } finally {
                win.Close();
            }
        }

        [AvaloniaFact]
        public void RackPanel_EmptyTrackShowsEmptyState_AndSelectionWritesNothing() {
            var panel = new FxRackPanel { Track = new UTrack { TrackNo = 1, TrackName = "Empty" } };
            var win = Host(panel);
            try {
                Assert.Empty(panel.Entries);
                Assert.True(Part<TextBlock>(panel, "PART_Empty").IsVisible);
                Assert.False(Part<StackPanel>(panel, "PART_Items").IsVisible);
                Assert.Equal(-1, panel.SelectedIndex);

                // 无轨道同样安全（陈列室/未打开工程时也能渲染）
                panel.Track = null;
                Assert.Empty(panel.Entries);

                // ── 只读：选行/按键都不写模型 ──
                var track = DemoTrack();
                var before = track.MixFx!.Clone();
                panel.Track = track;
                // 语义：机架总有一个"当前模块" ⇒ 挂上带行的轨道后自动选中首行（空列表才是 -1）
                Assert.Equal(0, panel.SelectedIndex);
                var seen = new List<int>();
                panel.SelectionChanged += (_, i) => seen.Add(i);
                panel.RaiseEvent(KeyDown(Key.Down));
                Assert.Equal(1, panel.SelectedIndex);
                panel.RaiseEvent(KeyDown(Key.End));
                Assert.Equal(4, panel.SelectedIndex);
                panel.RaiseEvent(KeyDown(Key.Home));
                Assert.Equal(0, panel.SelectedIndex);
                Assert.Equal(new[] { 1, 4, 0 }, seen);

                // 选中行的底色 = secondary-container
                var rows = panel.GetVisualDescendants().OfType<Border>()
                    .Where(b => (b as INamed)?.Name == "PART_Row").ToList();
                Assert.Equal(Pool(Md3Role.SecondaryContainer), ColorOf(rows[0].Background));

                // 模型零改动
                Assert.Equal(before.Enabled, track.MixFx!.Enabled);
                Assert.Equal(before.EqEnabled, track.MixFx.EqEnabled);
                Assert.Equal(before.CompEnabled, track.MixFx.CompEnabled);
                Assert.Equal(before.ReverbEnabled, track.MixFx.ReverbEnabled);
                Assert.Equal(before.EqPreset, track.MixFx.EqPreset);
                Assert.Equal("OTT", track.VstSlots[0].PluginUid);
                Assert.False(track.VstSlots[0].Bypassed);
            } finally {
                win.Close();
            }
        }

        // ── 6. 颜色池铁律（运行时） ─────────────────────────────────────────

        /// <summary>
        /// no-motion 契约：窗口带 .no-motion（或应用级「减少动效」开启）时控件不挂过渡；
        /// 反之过渡在位。这是"no-motion 生效时不动画"的落地方式（代码构筑的模板没法走样式层置空）。
        /// </summary>
        [AvaloniaFact]
        public void NoMotionClass_ControlsTransitions() {
            var frozen = new Md3PowerSwitch();
            var winFrozen = Host(frozen);            // Host 会加 .no-motion
            try {
                Assert.Null(Part<Border>(frozen, "PART_Circle").Transitions);
            } finally {
                winFrozen.Close();
            }

            bool reduceMotion = Preferences.Default.ReduceMotion;
            var animated = new Md3PowerSwitch();
            SyncPool();
            var win = new WindowEx { Width = 300, Height = 200, Content = animated };
            win.Show();
            Layout(win, 300, 200);
            try {
                Transitions transitions = Part<Border>(animated, "PART_Circle").Transitions;
                if (reduceMotion) {
                    Assert.Null(transitions);
                } else {
                    Assert.NotNull(transitions);
                }
            } finally {
                win.Close();
            }
        }

        [AvaloniaFact]
        public void Family_PaintsOnlyPoolColors() {
            var track = DemoTrack();
            var seg = new Md3SegmentedControl();
            seg.SetOptions("A", "B", "C");
            var stack = new StackPanel {
                Children = {
                    new Md3PowerSwitch { IsChecked = true },
                    new Md3PowerSwitch { ForcedVisual = Md3RackVisual.Hover },
                    seg,
                    new Md3NumericReadout { Label = "G", Unit = "dB", Minimum = -12, Maximum = 12, Value = 1.5 },
                    new FxModuleCard {
                        Title = "EQ", Subtitle = "vocal_air", Accent = Md3Role.Primary, IsSelected = true,
                        CardContent = new Md3NumericReadout { Unit = "dB", Value = 1 },
                    },
                    new FxModuleCard { Title = "Comp", Accent = Md3Role.Tertiary, IsBypassed = true, IsPowered = true },
                    new FxModuleCard { Title = "Rev", Accent = Md3Role.Secondary, IsPowered = false },
                    new FxRackPanel { Track = track },
                },
            };
            var win = Host(stack, 1400, 1200);
            try {
                var poolColors = new HashSet<Color>();
                foreach (Md3Role role in Enum.GetValues<Md3Role>()) {
                    poolColors.Add(ColorPool.Current.Color(role));
                }
                var offenders = new List<string>();
                foreach (Visual v in win.GetVisualDescendants()) {
                    CollectOffenders(v, poolColors, offenders);
                }
                Assert.True(offenders.Count == 0,
                    "控件族只允许用颜色池角色色，发现越界色值：" + string.Join(" | ", offenders));
            } finally {
                win.Close();
            }
        }

        private static void CollectOffenders(Visual v, HashSet<Color> poolColors, List<string> offenders) {
            switch (v) {
                case Border b:
                    Check("Background", b.Background);
                    Check("BorderBrush", b.BorderBrush);
                    break;
                case TextBlock t:
                    Check("Foreground", t.Foreground);
                    break;
                case Shape s:
                    Check("Fill", s.Fill);
                    Check("Stroke", s.Stroke);
                    break;
            }
            return;

            void Check(string slot, IBrush brush) {
                switch (brush) {
                    case null:
                        return;
                    case ISolidColorBrush solid when poolColors.Contains(solid.Color):
                        return;
                    case LinearGradientBrush gradient:
                        foreach (GradientStop stop in gradient.GradientStops) {
                            if (!poolColors.Contains(stop.Color)) {
                                offenders.Add($"{v.GetType().Name}.{slot}@{stop.Color}");
                            }
                        }
                        if (gradient.GradientStops.Count == 0) {
                            offenders.Add($"{v.GetType().Name}.{slot}(空渐变)");
                        }
                        return;
                    default:
                        offenders.Add($"{v.GetType().Name}.{slot}={Describe(brush)}");
                        return;
                }
            }

            static string Describe(IBrush brush) => brush switch {
                ISolidColorBrush s => s.Color.ToString(),
                _ => brush.GetType().Name,
            };
        }

        // ── 7. 陈列室窗口 ───────────────────────────────────────────────────

        [AvaloniaFact]
        public void GalleryWindow_ConstructsWithoutProject_AndHostsEverySection() {
            SyncPool();
            var win = new ControlGalleryWindow();
            try {
                // 标题走字符串键；窗口继承自有 WindowEx（原生装饰、实色）
                Assert.Equal(ThemeManager.GetString("controlgallery.title"), win.Title);
                Assert.IsAssignableFrom<OpenUtau.App.Controls.WindowEx>(win);

                win.Classes.Set("no-motion", true);   // 冻结动效，便于同步读状态色
                win.Show();
                Layout(win, win.Width, win.Height);
                var all = win.GetVisualDescendants().ToList();

                Assert.True(all.OfType<Md3PowerSwitch>().Count() >= 9, "电源开关：3 实时 + 6 冻结");
                Assert.True(all.OfType<Md3SegmentedControl>().Count() >= 8, "分段选择器：4 实时 + 5 冻结");
                Assert.True(all.OfType<Md3NumericReadout>().Count() >= 10, "读数：实时 + 卡内 + 冻结");
                Assert.True(all.OfType<FxModuleCard>().Count() >= 7, "模块面板：3 实时 + 4 冻结");
                Assert.True(all.OfType<FxRackPanel>().Count() == 2, "机架：真实数据 + 空态");

                // 机架演示用真实数据渲染：5 行（3 内置 + 2 VST 槽）
                var rack = all.OfType<FxRackPanel>().First(p => p.Entries.Count > 0);
                Assert.Equal(5, rack.Entries.Count);
                Assert.Empty(all.OfType<FxRackPanel>().First(p => p.Entries.Count == 0).Entries);
            } finally {
                win.Close();
            }
        }

        // ── 8. 文案与入口 ───────────────────────────────────────────────────

        /// <summary>
        /// W12 改造：直接读两份语言资源字典，**不再调用 App.SetLanguage**——
        /// 语言是进程级全局状态，切换窗口期会污染并行用例（集成树出现过一次 Light 偶发失败）。
        /// 现在顺带断言两语言文件**键集完全一致**。
        /// </summary>
        [AvaloniaFact]
        public void NewStrings_ResolveInBothLanguages() {
            ResourceDictionary enDict = StringDictionaryProbe.English();
            ResourceDictionary zhDict = StringDictionaryProbe.Chinese();

            var enKeys = StringDictionaryProbe.Keys(enDict);
            var zhKeys = StringDictionaryProbe.Keys(zhDict);
            Assert.True(enKeys.SetEquals(zhKeys),
                "EN/zh 键集不一致：zh 缺 " + string.Join(", ", enKeys.Except(zhKeys)) +
                "；en 缺 " + string.Join(", ", zhKeys.Except(enKeys)));

            foreach (string key in NewKeys) {
                string? en = StringDictionaryProbe.Value(enDict, key);
                string? zh = StringDictionaryProbe.Value(zhDict, key);
                Assert.False(string.IsNullOrWhiteSpace(en), $"EN 缺键：{key}");
                Assert.False(string.IsNullOrWhiteSpace(zh), $"zh-CN 缺键：{key}");
                Assert.NotEqual(en, zh);
            }
        }

        [AvaloniaFact]
        public void MainWindow_ToolsMenu_HasControlGalleryEntry() {
            // MainWindow.axaml 由测试工程复制到输出目录（同名入口是"开发者/工具菜单"的键与处理器）
            string xaml = File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "Views", "MainWindow.axaml"));
            Assert.Contains("menu.tools.controlgallery", xaml);
            Assert.Contains("Click=\"OnMenuControlGallery\"", xaml);
        }
    }
}

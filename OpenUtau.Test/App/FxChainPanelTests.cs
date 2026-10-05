using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Theming;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Vst;
using OpenUtau.Test.TestSupport;
using OpenUtau.Theming;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// 效果链面板（<see cref="FxChainPanel"/> / <see cref="FxChainRow"/>，B2/B3/B4/B5）契约测试：
    ///   · 链行顺序 = 内置三件套（固定 DSP 序）→ VST 槽（按 SlotIndex），与**只读稿**
    ///     <see cref="FxRackPanel.BuildEntries"/> 的投影一致（防两套口径漂移）；
    ///   · 每行 = 把手 · 序号 · 名称 · 格式徽标 · 旁通开关（尺寸写进断言）；
    ///   · 旁通 / 加内置 / 移除 / 重排全部走 <c>DocManager</c> 命令并可 -Undo 还原；
    ///   · 双击派发到正确编辑器（Fake 派发端，不启动真实窗口）；
    ///   · 空态文案走键；颜色只取 md3 色池。
    /// </summary>
    [Collection("Theme")]
    public class FxChainPanelTests {
        /// <summary>本轮新增的字符串键（EN/zh 各一份，值必须不同）。</summary>
        static readonly string[] NewKeys = {
            "fxchain.empty", "fxchain.notrack", "fxchain.add.tip", "fxchain.addvst",
            "fxchain.badge.builtin", "fxchain.power.tip", "fxchain.remove.tip",
            "fxchain.open.tip", "fxchain.grip.tip", "fxchain.grip.fixed.tip",
        };

        // 轨道号取 950+：MessageBus 全进程共享，避开其它用例（0/1/900+）的双向干扰
        const int BaseTrackNo = 950;

        // W24 测试卫生：DocManager 的全局派发字段改用**作用域版**（构造保存 + 确定性通道，
        // Dispose 原样恢复）；参数取最保守档（nullChannel / installScheduler:false），理由见
        // MixerGeometryTests 的同类注释。
        readonly DocManagerTestSetup.ScopedDispatcher dispatcher;

        public FxChainPanelTests() {
            // ExecuteCmd 在测试线程内联执行（否则会被 PostOnUIThread 延后 → 断言时序敏感）
            dispatcher = DocManagerTestSetup.EnterScopedDispatcher(nullChannel: true, installScheduler: false);
        }

        public void Dispose() => dispatcher.Dispose();

        // ── 夹具 ────────────────────────────────────────────────────────────

        static void SyncPool() =>
            ColorPool.Initialize(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, ThemeManager.IsDarkMode);

        static WindowEx Host(Control content, double width = 320, double height = 720) {
            SyncPool();
            var win = new WindowEx { Width = width, Height = height, Content = content };
            win.Classes.Set("no-motion", true);   // 冻结动效：状态色同步可读
            win.Show();
            Layout(win, width, height);
            return win;
        }

        static void Layout(Window win, double width, double height) {
            win.Measure(new Size(width, height));
            win.Arrange(new Rect(0, 0, width, height));
            Dispatcher.UIThread.RunJobs();
        }

        /// <summary>
        /// 等待条件成立，**并每轮 pump 一次 UI dispatcher**。
        /// 链行重建编组到 UI 线程（<c>Dispatcher.UIThread.Post</c>），所以
        /// 「命令已执行 / 通知已到」与「行已重建」之间隔着一轮消息循环；
        /// headless 下没有别的泵，只 <c>Thread.Sleep</c> 会永远等不到。
        /// </summary>
        static void PumpUntil(Func<bool> cond, int timeoutMs = 5000) {
            var sw = Stopwatch.StartNew();
            while (!cond()) {
                Assert.True(sw.ElapsedMilliseconds < timeoutMs,
                    $"PumpUntil 超时（{timeoutMs} ms）—— 条件始终未成立，或重建没有落到 UI 线程");
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }
        }

        static T Part<T>(Visual root, string name) where T : Visual =>
            root.GetVisualDescendants().OfType<T>().First(v => (v as INamed)?.Name == name);

        static List<FxChainRow> Rows(Visual root) => root.GetVisualDescendants().OfType<FxChainRow>().ToList();

        /// <summary>探针派发端：只记录"应该开哪个编辑器"，绝不启动窗口。</summary>
        sealed class FakeLauncher : IFxChainEditorLauncher {
            public List<(UTrack track, MixFxModule module)> BuiltInCalls { get; } = new();
            public List<(UTrack track, int slotIndex)> VstCalls { get; } = new();
            public List<(UTrack track, int slotIndex)> BrowseCalls { get; } = new();
            public void OpenBuiltInEditor(UTrack track, MixFxModule module) => BuiltInCalls.Add((track, module));
            public void OpenVstEditor(UTrack track, int slotIndex) => VstCalls.Add((track, slotIndex));
            public void BrowsePlugin(UTrack track, int slotIndex) => BrowseCalls.Add((track, slotIndex));
        }

        /// <summary>已注册 UID 的工程：VST 徽标/名字靠注册表解析（未注册只有 UID 串）。</summary>
        static UTrack DemoTrack(out UProject project, bool withMixFx = true) {
            project = new UProject { Saved = true };
            project.tracks[0].TrackNo = BaseTrackNo;
            var track = project.tracks[0];
            track.TrackName = "Chain A";
            if (withMixFx) {
                track.MixFx = new UMixFx {
                    Enabled = true,
                    EqEnabled = true, CompEnabled = false, ReverbEnabled = true,
                    EqPreset = "vocal_air", CompPreset = "gentle", ReverbPreset = "small_room",
                };
            }
            track.VstSlots = new List<VstPluginSlot>();
            return track;
        }

        static void LoadProject(UProject project) =>
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(project));

        static UTrack RichTrack(out UProject project) {
            var track = DemoTrack(out project);
            track.VstSlots.Add(new VstPluginSlot(0) { PluginUid = "test:fxchain-a" });
            track.VstSlots.Add(new VstPluginSlot(1) { PluginUid = "test:fxchain-b" });
            VstTestSetup.Register(VstTestSetup.MakeEntry("test:fxchain-a", "ChainA"));
            VstTestSetup.Register(VstTestSetup.MakeEntry("test:fxchain-b", "ChainB"));
            return track;
        }

        static void DoubleClick(Control target) {
            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            target.RaiseEvent(new PointerPressedEventArgs(target, pointer, target, new Point(10, 10), 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
                KeyModifiers.None, 2));
        }

        // ══════════════════ 链行顺序 / 徽标 / 序号 ══════════════════

        [AvaloniaFact]
        public void Rows_AreBuiltInThenVst_InDspOrder_WithBadgeAndPowerState() {
            var track = RichTrack(out var project);
            LoadProject(project);
            var panel = new FxChainPanel { Track = track };
            var win = Host(panel);
            try {
                var rows = panel.ViewModel.Rows;
                Assert.Equal(5, rows.Count);
                Assert.Equal(new[] { 1, 2, 3, 4, 5 }, rows.Select(r => r.Order));
                Assert.Equal(
                    new[] { FxChainItemKind.BuiltIn, FxChainItemKind.BuiltIn, FxChainItemKind.BuiltIn,
                            FxChainItemKind.Vst, FxChainItemKind.Vst },
                    rows.Select(r => r.Kind));
                // 内置段：名称走字符串键，徽标走「内置」键，槽位下标 -1
                Assert.Equal(new[] { "mixfx.eq", "mixfx.compressor", "mixfx.reverb" },
                    rows.Where(r => r.Kind == FxChainItemKind.BuiltIn).Select(r => r.NameKey));
                Assert.All(rows.Where(r => r.Kind == FxChainItemKind.BuiltIn), r => {
                    Assert.True(r.BadgeIsBuiltIn);
                    Assert.Equal(-1, r.SlotIndex);
                    Assert.Equal(string.Empty, r.Name);   // 内置行不用数据名（走 NameKey）
                });
                Assert.Equal(new[] { MixFxModule.Eq, MixFxModule.Compressor, MixFxModule.Reverb },
                    rows.Where(r => r.Kind == FxChainItemKind.BuiltIn).Select(r => r.Module!.Value));
                // VST 段：数据名 + VST3 技术徽标（注册表解析）+ 真实槽位下标
                Assert.Equal(new[] { "ChainA", "ChainB" },
                    rows.Where(r => r.Kind == FxChainItemKind.Vst).Select(r => r.Name));
                Assert.All(rows.Where(r => r.Kind == FxChainItemKind.Vst), r => Assert.False(r.BadgeIsBuiltIn));
                Assert.Equal(new[] { "VST3", "VST3" },
                    rows.Where(r => r.Kind == FxChainItemKind.Vst).Select(r => r.BadgeText));
                Assert.Equal(new[] { 0, 1 },
                    rows.Where(r => r.Kind == FxChainItemKind.Vst).Select(r => r.SlotIndex));
                // 电源状态 = 模型真值；"不过声"= 模块关 / 旁通 / 总电源关
                Assert.Equal(new[] { true, false, true, true, true }, rows.Select(r => r.IsPowered));
                Assert.Equal(new[] { false, true, false, false, false }, rows.Select(r => r.IsMuted));
                // 链非空 ⇒ 不显示空态
                Assert.False(panel.ViewModel.IsEmpty);
                Assert.False(Part<TextBlock>(panel, "EmptyHint").IsVisible);
                Assert.False(Part<TextBlock>(panel, "NoTrackHint").IsVisible);
                Assert.Equal("Chain A", Part<TextBlock>(panel, "HeaderName").Text);
                // 行控件数与链行数一致
                Assert.Equal(5, Rows(panel).Count);
            } finally {
                win.Close();
            }
        }

        [AvaloniaFact]
        public void Rows_MatchTheReadOnlyRackProjection_NoSecondOpinionOnOrder() {
            var track = RichTrack(out var project);
            LoadProject(project);
            var panel = new FxChainPanel { Track = track };
            var win = Host(panel);
            try {
                // 只读稿（FxRackPanel.BuildEntries）与正式面板必须同一条链、同一个顺序。
                // 口径差异（明示）：只读稿会列出空槽，正式面板只列已加载插件 ⇒ 本用例用"全部已加载"的轨道。
                var reference = FxRackPanel.BuildEntries(track);
                var mine = panel.ViewModel.Rows;
                Assert.Equal(reference.Count, mine.Count);
                Assert.Equal(reference.Select(e => e.Order), mine.Select(r => r.Order));
                Assert.Equal(reference.Select(e => e.IsBuiltIn),
                    mine.Select(r => r.Kind == FxChainItemKind.BuiltIn));
                Assert.Equal(reference.Where(e => e.IsBuiltIn).Select(e => e.TitleKey),
                    mine.Where(r => r.Kind == FxChainItemKind.BuiltIn).Select(r => r.NameKey));
                Assert.Equal(reference.Select(e => e.SlotIndex), mine.Select(r => r.SlotIndex));
                Assert.Equal(reference.Select(e => e.IsPowered), mine.Select(r => r.IsPowered));
                // VST 徽标：只读稿的 Kind 与面板的 BadgeText 同源（slot.PluginTypeDisplay）
                Assert.Equal(reference.Where(e => !e.IsBuiltIn).Select(e => e.Kind),
                    mine.Where(r => r.Kind == FxChainItemKind.Vst).Select(r => r.BadgeText));
                // 总电源关：内置段整体"不过声"（与只读稿 IsBypassed 同义）
                track.MixFx!.Enabled = false;
                panel.ViewModel.Rebuild();
                var referenceOff = FxRackPanel.BuildEntries(track);
                Assert.All(referenceOff.Where(e => e.IsBuiltIn), e => Assert.True(e.IsBypassed));
                Assert.All(panel.ViewModel.Rows.Where(r => r.Kind == FxChainItemKind.BuiltIn),
                    r => Assert.True(r.IsMuted));
            } finally {
                win.Close();
            }
        }

        [AvaloniaFact]
        public void Rows_CarryTheCompleteRowGeometry_B4() {
            var track = RichTrack(out var project);
            LoadProject(project);
            var panel = new FxChainPanel { Track = track };
            var win = Host(panel);
            try {
                var row = Rows(panel)[0];
                var root = Part<Border>(row, "RowRoot");
                // 行容器（B4 自定；设计稿无此元素）
                Assert.Equal(new CornerRadius(12), root.CornerRadius);
                Assert.Equal(new Thickness(1), root.BorderThickness);
                Assert.Equal(new Thickness(10, 6), root.Padding);
                Assert.Equal(44, root.MinHeight);
                // 把手 + 序号 + 名称 + 徽标 + 状态角标 + 旁通开关 + 移除（部件齐全）
                Assert.NotNull(Part<Border>(row, "GripBox"));
                Assert.NotNull(Part<TextBlock>(row, "OrderText"));
                Assert.NotNull(Part<TextBlock>(row, "NameText"));
                Assert.Equal(12, Part<TextBlock>(row, "NameText").FontSize);
                Assert.Equal(FontWeight.SemiBold, Part<TextBlock>(row, "NameText").FontWeight);
                var badge = Part<Border>(row, "BadgeChip");
                Assert.Equal(new CornerRadius(999), badge.CornerRadius);
                Assert.Equal(new Thickness(6, 1), badge.Padding);
                var chip = Part<Border>(row, "StateChip");
                Assert.Equal(22, chip.Height);
                Assert.Equal(new CornerRadius(999), chip.CornerRadius);
                Assert.Equal(new Thickness(10, 0), chip.Padding);
                // 旁通开关 = VST-Plugin `Power Toggle`：34×20 圆角 999 / 内边距 0 3 / 指示点 14×14
                var power = Part<ToggleButton>(row, "PowerToggle");
                Assert.Equal(34, power.Width);
                Assert.Equal(20, power.Height);
                var trackBg = Part<Border>(power, "PART_Track");
                Assert.Equal(34, trackBg.Width);
                Assert.Equal(20, trackBg.Height);
                Assert.Equal(new CornerRadius(999), trackBg.CornerRadius);
                var thumb = Part<Border>(power, "PART_Thumb");
                Assert.Equal(14, thumb.Width);
                Assert.Equal(14, thumb.Height);
                Assert.Equal(new CornerRadius(999), thumb.CornerRadius);
                Assert.Equal(new Thickness(3, 0), thumb.Margin);
            } finally {
                win.Close();
            }
        }

        // ══════════════════ 空态 ══════════════════

        [AvaloniaFact]
        public void EmptyChain_ShowsTheLocalizedHint_AndAcceptsDrop() {
            var track = DemoTrack(out var project, withMixFx: false);
            LoadProject(project);
            var panel = new FxChainPanel { Track = track };
            var win = Host(panel);
            try {
                Assert.Empty(panel.ViewModel.Rows);
                Assert.True(panel.ViewModel.IsEmpty);
                Assert.True(panel.ViewModel.ShowEmptyHint);
                var hint = Part<TextBlock>(panel, "EmptyHint");
                Assert.True(hint.IsVisible);
                Assert.Equal(ThemeManager.GetString("fxchain.empty"), hint.Text);
                Assert.False(Part<TextBlock>(panel, "NoTrackHint").IsVisible);

                // 空态可拖入：内置模块 → 建链（可撤销）；VST 插件 → 落槽
                Assert.True(panel.ViewModel.DropPayload(FxChainDragData.BuiltInPayload(MixFxModule.Reverb)));
                Assert.NotNull(track.MixFx);
                Assert.True(track.MixFx!.ReverbEnabled);
                Assert.False(track.MixFx.EqEnabled);
                Assert.Equal(3, panel.ViewModel.Rows.Count);
                DocManager.Inst.Undo();
                Assert.Null(track.MixFx);   // -Undo：回到"未配置效果"

                Assert.True(panel.ViewModel.DropPayload(VstTestSetup.MakeEntry("test:fxchain-drop").Uid));
                VstTestSetup.Register(VstTestSetup.MakeEntry("test:fxchain-drop", "Dropped"));
                Assert.Equal("test:fxchain-drop", track.VstSlots[0].PluginUid);
                // 面板不写 MixerViewModel（只读模型 + 命令）
                Assert.Equal(1, panel.ViewModel.Rows.Count(r => r.Kind == FxChainItemKind.Vst));
            } finally {
                win.Close();
            }
        }

        [AvaloniaFact]
        public void NoTrack_ShowsSelectATrack_InsteadOfTheEmptyHint() {
            var panel = new FxChainPanel();
            var win = Host(panel);
            try {
                Assert.False(panel.ViewModel.HasTrack);
                Assert.Empty(panel.ViewModel.Rows);
                Assert.True(Part<TextBlock>(panel, "NoTrackHint").IsVisible);
                Assert.Equal(ThemeManager.GetString("fxchain.notrack"), Part<TextBlock>(panel, "NoTrackHint").Text);
                Assert.False(Part<TextBlock>(panel, "EmptyHint").IsVisible);
            } finally {
                win.Close();
            }
        }

        // ══════════════════ 冻结契约接线（选中轨道） ══════════════════

        [AvaloniaFact]
        public void BindSelection_FollowsTheHostSelectionStream() {
            var track = RichTrack(out var project);
            LoadProject(project);
            var panel = new FxChainPanel();
            var win = Host(panel);
            try {
                var subject = new System.Reactive.Subjects.Subject<UTrack?>();
                panel.BindSelection(subject);
                Assert.Empty(panel.ViewModel.Rows);

                subject.OnNext(track);          // 宿主（W1 MixerViewModel.SelectedTrack）推一条轨道
                Assert.Equal(5, panel.ViewModel.Rows.Count);
                Assert.Equal("Chain A", Part<TextBlock>(panel, "HeaderName").Text);

                subject.OnNext(null);           // 取消选中 → 回到空态
                Assert.Empty(panel.ViewModel.Rows);
                Assert.False(panel.ViewModel.HasTrack);

                // 面板从不反向写宿主：ViewModel 没有 MixerViewModel 依赖（Track 只读）
                Assert.Same(track, track);
            } finally {
                win.Close();
            }
        }

        // ══════════════════ 双击派发编辑器（B4/B5） ══════════════════

        [AvaloniaFact]
        public void DoubleClick_BuiltInRow_OpensTheBuiltInEditor() {
            var track = RichTrack(out var project);
            LoadProject(project);
            var panel = new FxChainPanel { Track = track };
            var launcher = new FakeLauncher();
            panel.ViewModel.Launcher = launcher;
            var win = Host(panel);
            try {
                var rows = Rows(panel);
                DoubleClick(Part<Border>(rows[1], "RowRoot"));   // 第 2 行 = 压缩器
                Assert.Single(launcher.BuiltInCalls);
                Assert.Same(track, launcher.BuiltInCalls[0].track);
                Assert.Equal(MixFxModule.Compressor, launcher.BuiltInCalls[0].module);
                Assert.Empty(launcher.VstCalls);

                // Enter 等价于双击（键盘可达）
                rows[0].RaiseEvent(new KeyEventArgs {
                    RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter,
                });
                Assert.Equal(2, launcher.BuiltInCalls.Count);
                Assert.Equal(MixFxModule.Eq, launcher.BuiltInCalls[1].module);
            } finally {
                win.Close();
            }
        }

        [AvaloniaFact]
        public void DoubleClick_VstRow_OpensTheNativeEditorOfThatSlot() {
            var track = RichTrack(out var project);
            LoadProject(project);
            var panel = new FxChainPanel { Track = track };
            var launcher = new FakeLauncher();
            panel.ViewModel.Launcher = launcher;
            var win = Host(panel);
            try {
                var rows = Rows(panel);
                Assert.Equal(5, rows.Count);
                DoubleClick(Part<Border>(rows[4], "RowRoot"));   // 第 5 行 = 槽 1
                Assert.Single(launcher.VstCalls);
                Assert.Same(track, launcher.VstCalls[0].track);
                Assert.Equal(1, launcher.VstCalls[0].slotIndex);
                Assert.Empty(launcher.BuiltInCalls);
            } finally {
                win.Close();
            }
        }

        [AvaloniaFact]
        public void PlusButton_AddsBuiltInAndBrowsesVst_ThroughTheLauncher() {
            var track = DemoTrack(out var project, withMixFx: false);
            LoadProject(project);
            var panel = new FxChainPanel { Track = track };
            var launcher = new FakeLauncher();
            panel.ViewModel.Launcher = launcher;
            var win = Host(panel);
            try {
                // 「＋」→ 内置模块：建链（B2：内置和 VST 一样能被"加入"），可撤销
                panel.ViewModel.AddBuiltIn(MixFxModule.Eq);
                Assert.NotNull(track.MixFx);
                Assert.True(track.MixFx!.EqEnabled);
                Assert.Equal(3, panel.ViewModel.Rows.Count);
                DocManager.Inst.Undo();
                Assert.Null(track.MixFx);

                // 已在链上的模块：再点只保证电源开，不重复建链
                track.MixFx = new UMixFx { Enabled = true, EqEnabled = false };
                panel.ViewModel.Rebuild();
                panel.ViewModel.AddBuiltIn(MixFxModule.Eq);
                Assert.True(track.MixFx.EqEnabled);

                // 「＋」→ 浏览插件：落到第一个空槽
                panel.ViewModel.BrowseVst();
                Assert.Single(launcher.BrowseCalls);
                Assert.Equal(0, launcher.BrowseCalls[0].slotIndex);
                Assert.Empty(launcher.BrowseCalls[0].track.VstSlots);   // 未凭空建槽（开窗不写模型）
            } finally {
                win.Close();
            }
        }

        // ══════════════════ 旁通 / 移除：命令 + 撤销往返（B8） ══════════════════

        [AvaloniaFact]
        public void BuiltInBypass_GoesThroughCommand_AndUndoRestores() {
            var track = RichTrack(out var project);
            LoadProject(project);
            var panel = new FxChainPanel { Track = track };
            var win = Host(panel);
            try {
                Assert.True(track.MixFx!.EqEnabled);
                var eqRow = panel.ViewModel.Rows[0];
                eqRow.TogglePowerCommand.Execute(null);
                Assert.False(track.MixFx.EqEnabled);
                Assert.False(panel.ViewModel.Rows[0].IsPowered);
                Assert.True(panel.ViewModel.Rows[0].IsMuted);

                DocManager.Inst.Undo();
                Assert.True(track.MixFx.EqEnabled);
                DocManager.Inst.Redo();
                Assert.False(track.MixFx.EqEnabled);
                DocManager.Inst.Undo();
                Assert.True(track.MixFx.EqEnabled);
            } finally {
                win.Close();
            }
        }

        [AvaloniaFact]
        public void RowSwitch_Click_RoutesThroughTheViewModelCommand() {
            var track = RichTrack(out var project);
            LoadProject(project);
            var panel = new FxChainPanel { Track = track };
            var win = Host(panel);
            try {
                var row = Rows(panel)[0];
                var power = Part<ToggleButton>(row, "PowerToggle");
                Assert.True(power.IsChecked);
                // 用户点了一下：ToggleButton 先翻本地值，再冒泡 Click → 行把它交给 VM 命令
                power.IsChecked = false;
                power.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(track.MixFx!.EqEnabled);
                Assert.False(panel.ViewModel.Rows[0].IsPowered);
                // 命令会重建链行 ⇒ 需要一轮布局才能拿到新的行控件
                Layout(win, 320, 720);
                Assert.False(Part<ToggleButton>(Rows(panel)[0], "PowerToggle").IsChecked);
            } finally {
                win.Close();
            }
        }

        [AvaloniaFact]
        public void BuiltInRows_HaveNoRemoveAffordance_ButVstRowsDo() {
            var track = RichTrack(out var project);
            LoadProject(project);
            var panel = new FxChainPanel { Track = track };
            var win = Host(panel);
            try {
                var rows = Rows(panel);
                Assert.False(Part<Button>(rows[0], "RemoveButton").IsVisible);   // 内置：无删除语义
                Assert.True(Part<Button>(rows[3], "RemoveButton").IsVisible);    // VST：可移除

                panel.ViewModel.Rows[3].RemoveCommand.Execute(null);
                Assert.Equal("", track.VstSlots[0].PluginUid);
                DocManager.Inst.Undo();
                Assert.Equal("test:fxchain-a", track.VstSlots[0].PluginUid);
            } finally {
                win.Close();
            }
        }

        // ══════════════════ 把手：VST 可拖、内置禁用（裁决 1） ══════════════════

        [AvaloniaFact]
        public void Grip_IsDisabledOnBuiltInRows_AndEnabledOnVstRows() {
            var track = RichTrack(out var project);
            LoadProject(project);
            var panel = new FxChainPanel { Track = track };
            var win = Host(panel);
            try {
                var rows = Rows(panel);
                Assert.True(panel.ViewModel.Rows[0].CanReorder == false, "内置段顺序固定，不得可拖");
                Assert.True(panel.ViewModel.Rows[3].CanReorder, "VST 段可重排");
                Assert.Equal(0.35, Part<Border>(rows[0], "GripBox").Opacity, 3);
                Assert.Equal(1.0, Part<Border>(rows[3], "GripBox").Opacity, 3);
                Assert.Equal("内置模块顺序固定（EQ → 压缩 → 混响）",
                    ToolTip.GetTip(Part<Border>(rows[0], "GripBox")));
                Assert.Equal(ThemeManager.GetString("fxchain.grip.tip"),
                    ToolTip.GetTip(Part<Border>(rows[3], "GripBox")));

                // 单个 VST 槽时也不需要重排（无相邻项）
                track.VstSlots.RemoveAt(1);
                panel.ViewModel.Rebuild();
                Assert.False(panel.ViewModel.Rows.Last().CanReorder);
            } finally {
                win.Close();
            }
        }

        // ══════════════════ 线程契约（顺序无关的回归） ══════════════════

        /// <summary>
        /// 回归（集成树暴露的真缺陷）：链行重建必须编组到 UI 线程。
        ///
        /// 复刻的正是抛异常的那条栈：<c>DocManager.Publish</c> 遍历订阅者发生在
        /// **执行命令的那条线程**上，而 <c>FxChainViewModel.OnNext</c> 会重建绑定到
        /// <c>ItemsControl</c> 的 <c>ObservableCollection</c>；非 UI 线程直接改它 ⇒
        /// <c>InvalidOperationException: The calling thread cannot access this object…</c>。
        ///
        /// 顺序无关的写法：不依赖 <c>[AvaloniaFact]</c> 的线程偶然性，也不动
        /// <c>DocManager.mainThread</c> / <c>PostOnUIThread</c> 这些全局状态
        /// （改它们会波及并行运行的其他集合）。这里在**线程池线程**上直接做
        /// 「执行命令 + 喂订阅者回调」，断言三件事：
        ///   ① 后台线程侧不抛异常；
        ///   ② 线程契约：**触发命令的那条后台线程绝不直接改绑定的集合**
        ///      （锚在触发线程 id 上，与 Dispatcher 在 headless 会话里的线程身份无关）；
        ///   ③ 编组后的重建在 UI 侧收敛（pump 一轮消息循环 + 重复 layout 直到容器稳定）。
        /// 负控已验证：把 <c>Rebuild</c> 的编组去掉后本用例失败（后台线程上 12 次集合变更、
        /// 视觉树残留 10 个行控件），修回后通过。
        /// </summary>
        [AvaloniaFact]
        public void OffThreadChainCommandAndNotification_DoNotThrow_AndRowsConvergeOnUiThread() {
            var track = RichTrack(out var project);
            LoadProject(project);
            var panel = new FxChainPanel { Track = track };
            var win = Host(panel);
            try {
                Assert.Equal(new[] { "ChainA", "ChainB" },
                    panel.ViewModel.Rows.Where(r => r.Kind == FxChainItemKind.Vst).Select(r => r.Name));

                // 线程契约的正题：执行命令的那条后台线程，绝不能直接改绑定的
                // ObservableCollection。判据锚在**触发线程 id** 上——不用
                // CheckAccess()/IsThreadPoolThread：headless 会话里 dispatcher 的
                // 线程身份与"刚执行命令的后台线程"不一定可用它们区分。
                int mutations = 0;
                int mutationsOnWorkerThread = 0;
                int workerThreadId = -1;
                panel.ViewModel.Rows.CollectionChanged += (_, _) => {
                    mutations++;
                    if (Thread.CurrentThread.ManagedThreadId == workerThreadId) {
                        mutationsOnWorkerThread++;
                    }
                };

                Exception? workerError = null;
                var worker = Task.Run(() => {
                    workerThreadId = Thread.CurrentThread.ManagedThreadId;
                    try {
                        var cmd = TrackMixCommands.ReorderVstSlot(track, 0, 1);
                        cmd.Execute();                          // 模型层：载荷互换 + 异步重载
                        panel.ViewModel.OnNext(cmd, false);     // = Publish 的非 UI 线程路径
                        panel.ViewModel.OnNext(new VstSlotChangedNotification(track.TrackNo, 0), false);
                    } catch (Exception ex) {
                        workerError = ex;
                    }
                });
                Assert.True(worker.Wait(TimeSpan.FromSeconds(10)), "后台线程未在超时内结束（疑似死锁）");
                Assert.Null(workerError);

                // ① 数据层已互换（同步部分）
                Assert.Equal("test:fxchain-b", track.VstSlots[0].PluginUid);
                Assert.Equal("test:fxchain-a", track.VstSlots[1].PluginUid);
                // ② 线程契约守卫（确定性，环境无关）
                Assert.Equal(0, mutationsOnWorkerThread);
                // ③ 链行在 UI 侧收敛（pump 一轮消息循环）
                PumpUntil(() => panel.ViewModel.Rows.Count == 5
                                && panel.ViewModel.Rows[3].Name == "ChainB"
                                && panel.ViewModel.Rows[4].Name == "ChainA");
                Assert.True(mutations > 0, "应当发生过链行重建（否则用例没覆盖到）");
                // 视觉树收敛：集合重建引起的容器回收要**下一轮布局**才落地
                // （真实应用里下一帧自然发生；headless 需要显式重复 layout 直到稳定）
                PumpUntil(() => {
                    Layout(win, 320, 720);
                    return Rows(panel).Count == 5;
                });
                Assert.Equal("ChainB", Part<TextBlock>(Rows(panel)[3], "NameText").Text);

                // UI 线程上的调用仍然同步生效（编组不能把同线程路径变成异步）
                panel.ViewModel.Rebuild();
                Assert.Equal("ChainB", panel.ViewModel.Rows[3].Name);
            } finally {
                win.Close();
            }
        }

        // ══════════════════ 布局层几何（W10 口径：断言 Bounds 而非声明值） ══════════════════

        /// <summary>
        /// 链行内按钮的**实际渲染尺寸**（<see cref="Layoutable.Bounds"/>）。
        ///
        /// 为什么必须按 Bounds 断言：本仓有两处**继承来的几何**在属性层看不出来 ——
        ///   · <c>Styles/Md3ControlThemes.axaml:28</c> 的 Md3ButtonTheme 设 <c>MinHeight=32</c>，
        ///     布局取 Max(MinHeight, Height) ⇒ 只声明 <c>Height="22"</c> 的裸按钮会渲染成 32；
        ///   · <c>Styles/Styles.axaml:159</c> 的应用级 <c>Button { Margin: 0,4 }</c> 再给每个按钮
        ///     （含 ToggleButton：它派生自 Button）外加 8px 竖向 margin。
        /// 本地样式里显式写 <c>MinHeight</c> 与 <c>Margin=0</c> 是唯一的中和方式（Style setter
        /// 优先级高于 ControlTheme），**不动**全局 Md3ButtonTheme / Styles.axaml。
        ///
        /// 校准断言：同一宿主里放一个**裸** Button（Height=20、无本地样式），它必须渲染成 32 ——
        /// 这正是"继承几何"仍在的证据。若哪天主题把 MinHeight 修掉，这行会失败，届时可连同本地
        /// 中和 Setter 一起评估是否还需要保留。
        /// </summary>
        [AvaloniaFact]
        public void RowChrome_RendersAtDeclaredMetrics_NotInheritedThemeGeometry() {
            var track = RichTrack(out var project);
            LoadProject(project);
            var panel = new FxChainPanel { Track = track };
            SyncPool();
            var calibration = new Button { Height = 20, Width = 20, Content = "cal" };
            var stack = new StackPanel { Children = { panel, calibration } };
            var win = new WindowEx { Width = 320, Height = 780, Content = stack };
            win.Classes.Set("no-motion", true);
            win.Show();
            Layout(win, 320, 780);
            try {
                // 校准：裸按钮被主题 MinHeight=32 顶高（证明断言口径有效）
                Assert.Equal(32, calibration.Bounds.Height);

                var rows = Rows(panel);
                // ✕ 移除：声明 22×22 ⇒ Bounds 必须也是 22（本地 MinHeight=22 + Margin=0 中和）
                var remove = Part<Button>(rows[3], "RemoveButton");
                Assert.Equal(22, remove.Bounds.Height);
                Assert.Equal(22, remove.Bounds.Width);
                Assert.Equal(0, remove.Margin.Top);
                Assert.Equal(0, remove.Margin.Bottom);
                // ＋：声明 36×36 ⇒ Bounds 36，且应用级 8px 竖向 margin 已清零
                var add = Part<Button>(panel, "AddButton");
                Assert.Equal(36, add.Bounds.Height);
                Assert.Equal(36, add.Bounds.Width);
                Assert.Equal(0, add.Margin.Top);
                Assert.Equal(0, add.Margin.Bottom);
                // 旁通开关（ToggleButton 派生自 Button，同吃 Button 主题/应用级样式）：
                // 自带模板固定 34×20 ⇒ Bounds 必须是 20，不被顶到 32
                var power = Part<ToggleButton>(rows[0], "PowerToggle");
                Assert.Equal(20, power.Bounds.Height);
                Assert.Equal(34, power.Bounds.Width);
                Assert.Equal(0, power.Margin.Top);
                Assert.Equal(0, power.Margin.Bottom);
                // 行容器：最小高 44
                Assert.True(Part<Border>(rows[0], "RowRoot").Bounds.Height >= 44,
                    $"链行低于声明的最小高：实际 {Part<Border>(rows[0], "RowRoot").Bounds.Height}");
            } finally {
                win.Close();
            }
        }

        // ══════════════════ 颜色池铁律 ══════════════════

        [AvaloniaFact]
        public void Panel_PaintsOnlyPoolColors() {
            var track = RichTrack(out var project);
            LoadProject(project);
            var panel = new FxChainPanel { Track = track };
            var win = Host(panel);
            try {
                var poolColors = new HashSet<Color>();
                foreach (Md3Role role in Enum.GetValues<Md3Role>()) {
                    poolColors.Add(ColorPool.Current.Color(role));
                }
                var offenders = new List<string>();
                foreach (Visual v in win.GetVisualDescendants()) {
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
                }
                Assert.True(offenders.Count == 0,
                    "效果链面板只允许用颜色池角色色，发现越界色值：" + string.Join(" | ", offenders));
                return;

                void Check(string slot, IBrush brush) {
                    switch (brush) {
                        case null:
                            return;
                        // 透明 = 不绘制（透明命中区 / 无底色），不是越界色值
                        case ISolidColorBrush solid when solid.Color == Avalonia.Media.Colors.Transparent:
                            return;
                        case ISolidColorBrush solid when poolColors.Contains(solid.Color):
                            return;
                        case ISolidColorBrush solid:
                            offenders.Add($"{slot}={solid.Color}");
                            return;
                        default:
                            offenders.Add($"{slot}={brush.GetType().Name}");
                            return;
                    }
                }
            } finally {
                win.Close();
            }
        }

        // ══════════════════ 文案键（EN/zh） ══════════════════

        [AvaloniaFact]
        public void NewStrings_ResolveInBothLanguages() {
            var en = new Dictionary<string, string>();
            OpenUtau.App.App.SetLanguage("en-US");
            foreach (string key in NewKeys) {
                Assert.True(ThemeManager.TryGetString(key, out string value), $"EN 缺键：{key}");
                Assert.NotEqual(key, value);
                en[key] = value;
            }
            OpenUtau.App.App.SetLanguage("zh-CN");
            try {
                foreach (string key in NewKeys) {
                    Assert.True(ThemeManager.TryGetString(key, out string value), $"zh-CN 缺键：{key}");
                    Assert.NotEqual(key, value);
                    Assert.NotEqual(en[key], value);   // 与英文不同 ⇒ zh 字典确有该键
                }
            } finally {
                OpenUtau.App.App.SetLanguage("en-US");
            }
        }

        [AvaloniaFact]
        public void EmptyHintText_IsTheFrozenDesignCopy() {
            OpenUtau.App.App.SetLanguage("zh-CN");
            try {
                Assert.Equal("从素材库 · 效果器 拖入", ThemeManager.GetString("fxchain.empty"));
                Assert.Equal("内置", ThemeManager.GetString("fxchain.badge.builtin"));
            } finally {
                OpenUtau.App.App.SetLanguage("en-US");
            }
        }
    }

    /// <summary>
    /// VST 段重排（<c>TrackMixCommands.ReorderVstSlot</c>）——载荷互换 + -Undo 往返。
    /// 共享 <c>VstPluginManager</c>/<c>VstPluginRegistry</c> 单例的用例必须与其它 VST 用例串行
    /// （异步加载任务可能跨用例残留），故单独成类并进 <c>VstShared</c> 集合。
    /// </summary>
    [Collection("VstShared")]
    public class FxChainReorderTests : IDisposable {
        const string UidA = "test:reorder-a";
        const string UidB = "test:reorder-b";

        // W24：同上，全局派发字段作用域化（构造保存、Dispose 恢复）
        readonly DocManagerTestSetup.ScopedDispatcher dispatcher;

        public FxChainReorderTests() {
            dispatcher = DocManagerTestSetup.EnterScopedDispatcher(nullChannel: true, installScheduler: false);
            VstTestSetup.Register(VstTestSetup.MakeEntry(UidA, "ReorderA"));
            VstTestSetup.Register(VstTestSetup.MakeEntry(UidB, "ReorderB"));
        }

        public void Dispose() => dispatcher.Dispose();

        static void WaitFor(Func<bool> cond, int timeoutMs = 3000) {
            var sw = Stopwatch.StartNew();
            while (!cond()) {
                Assert.True(sw.ElapsedMilliseconds < timeoutMs, "WaitFor timed out");
                Thread.Sleep(10);
            }
        }

        static UTrack TwoSlotTrack(out UProject project, int trackNo) {
            project = new UProject { Saved = true };
            project.tracks[0].TrackNo = trackNo;
            var track = project.tracks[0];
            track.TrackName = "Reorder";
            track.VstSlots = new List<VstPluginSlot> {
                new(0) { PluginUid = UidA, StateData = new byte[] { 1 } },
                new(1) { PluginUid = UidB, StateData = new byte[] { 2 } },
            };
            return track;
        }

        /// <summary>
        /// W24：VST **进程级全局态**的定点处置 —— 装假 bridge → 独占闸内跑用例体 →
        /// finally 只 <c>RemoveTrack(自己那条轨的键)</c> 并还原 <c>Bridge</c>。
        ///
        /// 为什么不用 <c>ClearAll()</c>：那是**全表清空**，会把并行 collection 正在用的实例
        /// 一起卸掉（原 6 处 ClearAll 正是本用例组偶发红的机制之一，fx-core 在 W23 审计里登记）。
        /// 闸 <see cref="VstTestSetup.RunExclusive"/> 与 <c>TrackMixCommandsTest</c>/<c>RenderGateTest</c>
        /// 同源：VstPluginManager 的实例表与 Bridge 都是进程级单例，跨 collection 必须互斥。
        ///
        /// 注：工程内轨道的 TrackNo 会被 <c>UTrack.Validate</c> 重置为 <c>tracks.IndexOf</c>（单轨工程 ⇒ 0），
        /// 所以这里无法像裸 <c>new UTrack</c> 那样取 <c>NextTrackNo()</c> 专属键；隔离由"闸 + 只删自己那条轨"保证。
        /// </summary>
        static void WithVstGlobal(UTrack track, Action body) {
            var bridge = new FakeVstBridge();
            var saved = VstPluginManager.Inst.Bridge;
            VstTestSetup.RunExclusive(() => {
                try {
                    VstPluginManager.Inst.Bridge = bridge;
                    body();
                } finally {
                    VstPluginManager.Inst.RemoveTrack(track.TrackNo);
                    VstPluginManager.Inst.Bridge = saved;
                }
            });
        }

        /// <summary>
        /// 每个用例用**独占轨道号**：VstPluginManager 是进程单例，异步 Load 任务是
        /// fire-and-forget 的，跨用例共享轨道号会让"卸载时 SaveState 写回"落到别的用例头上。
        /// </summary>
        [AvaloniaFact]
        public void ReorderVstSlot_SwapsPayloads_AndUndoRestoresThem() {
            var track = TwoSlotTrack(out var project, 971);
            WithVstGlobal(track, () => {
                DocManager.Inst.ExecuteCmd(new LoadProjectNotification(project));
                var cmd = TrackMixCommands.ReorderVstSlot(track, 0, 1);

                DocManager.Inst.StartUndoGroup(deferValidate: true);
                try {
                    DocManager.Inst.ExecuteCmd(cmd);
                } finally {
                    DocManager.Inst.EndUndoGroup();
                }
                // 载荷互换：UID + 参数状态一起走（参数必须跟着插件走）
                Assert.Equal(UidB, track.VstSlots[0].PluginUid);
                Assert.Equal(UidA, track.VstSlots[1].PluginUid);
                Assert.Equal(new byte[] { 2 }, track.VstSlots[0].StateData);
                Assert.Equal(new byte[] { 1 }, track.VstSlots[1].StateData);
                // 下标与列表位置始终一致（重排不搬列表元素 ⇒ 实例↔下标映射不变）
                Assert.Equal(0, track.VstSlots[0].SlotIndex);
                Assert.Equal(1, track.VstSlots[1].SlotIndex);
                // 实例按新载荷重载
                WaitFor(() => VstPluginManager.Inst.GetEffect(track.TrackNo, 0)?.Slot?.PluginUid == UidB);
                WaitFor(() => VstPluginManager.Inst.GetEffect(track.TrackNo, 1)?.Slot?.PluginUid == UidA);

                // -Undo：回到原位（UID 是数据层契约；参数状态在实例已加载时会被
                // 卸载侧 SaveState 重写，属 FakeBridge 的固定返回值，故此处只断言 UID）
                DocManager.Inst.Undo();
                Assert.Equal(UidA, track.VstSlots[0].PluginUid);
                Assert.Equal(UidB, track.VstSlots[1].PluginUid);
                Assert.Equal(0, track.VstSlots[0].SlotIndex);
                Assert.Equal(1, track.VstSlots[1].SlotIndex);
                WaitFor(() => VstPluginManager.Inst.GetEffect(track.TrackNo, 0)?.Slot?.PluginUid == UidA);
            });
        }

        [AvaloniaFact]
        public void MoveRow_ThroughThePanel_MovesThePluginAndUndoPutsItBack() {
            var track = TwoSlotTrack(out var project, 972);
            WithVstGlobal(track, () => {
                DocManager.Inst.ExecuteCmd(new LoadProjectNotification(project));
                var vm = new FxChainViewModel();
                vm.Attach(track);
                try {
                    Assert.Equal(new[] { 0, 1 }, vm.Rows.Select(r => r.SlotIndex));
                    Assert.Equal(new[] { "ReorderA", "ReorderB" }, vm.Rows.Select(r => r.Name));

                    // 第一行下移一格 = 两个插件换位；界面按新的槽载荷重建
                    vm.MoveRow(vm.Rows[0], +1);
                    Assert.Equal(new[] { UidB, UidA }, track.VstSlots.Select(s => s.PluginUid));
                    Assert.Equal(new[] { "ReorderB", "ReorderA" }, vm.Rows.Select(r => r.Name));

                    // 段边界：第一行再上移 = 无操作（不越界、不留半个撤销步）
                    vm.MoveRow(vm.Rows[0], -1);
                    Assert.Equal(new[] { UidB, UidA }, track.VstSlots.Select(s => s.PluginUid));

                    // -Undo：一次撤销回到原位（多步移动也只算一步，见 MoveRow 的 UndoGroup）
                    DocManager.Inst.Undo();
                    Assert.Equal(new[] { UidA, UidB }, track.VstSlots.Select(s => s.PluginUid));
                    Assert.Equal(new[] { "ReorderA", "ReorderB" }, vm.Rows.Select(r => r.Name));
                } finally {
                    vm.Dispose();
                }
            });
        }

        [AvaloniaFact]
        public void BuiltInRows_AreNotReorderable_NoModelWrite() {
            var track = TwoSlotTrack(out var project, 973);
            WithVstGlobal(track, () => {
                track.MixFx = new UMixFx { Enabled = true };
                DocManager.Inst.ExecuteCmd(new LoadProjectNotification(project));
                var vm = new FxChainViewModel();
                vm.Attach(track);
                try {
                    var before = track.MixFx.Clone();
                    vm.MoveRow(vm.Rows[0], +1);      // 内置 EQ 行：不参与重排
                    vm.MoveRow(vm.Rows[0], -1);
                    Assert.Equal(new[] { UidA, UidB }, track.VstSlots.Select(s => s.PluginUid));
                    Assert.Equal(before.Enabled, track.MixFx.Enabled);
                    Assert.Equal(before.EqEnabled, track.MixFx.EqEnabled);
                    Assert.Equal(before.ReverbEnabled, track.MixFx.ReverbEnabled);
                } finally {
                    vm.Dispose();
                }
            });
        }
    }
}

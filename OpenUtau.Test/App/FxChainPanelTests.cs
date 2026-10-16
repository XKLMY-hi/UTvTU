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
using Avalonia.Headless;
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
    /// <summary>共享横向机架的真实控件、实例归属与可撤销编辑检查。</summary>
    [Collection("WorkspaceSession")]
    public class FxChainPanelTests : IDisposable {
        readonly DocManagerTestSetup.ScopedDispatcher dispatcher = DocManagerTestSetup.EnterScopedDispatcher(nullChannel: true, installScheduler: false);
        readonly UProject previous = DocManager.Inst.Project;
        readonly UProject project = new();
        UTrack Track => project.tracks[0];
        public FxChainPanelTests() { DocManager.Inst.ExecuteCmd(new LoadProjectNotification(project)); }
        public void Dispose() { DocManager.Inst.ExecuteCmd(new LoadProjectNotification(previous)); dispatcher.Dispose(); }
        static void Pump() => Dispatcher.UIThread.RunJobs();
        static WindowEx Host(Control content, double width = 1000, double height = 350) {
            ColorPool.Initialize(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, ThemeManager.IsDarkMode);
            var window = new WindowEx { Width = width, Height = height, Content = content };
            window.Classes.Set("no-motion", true); window.Show();
            window.Measure(new Size(width, height)); window.Arrange(new Rect(0, 0, width, height)); Pump(); return window;
        }
        FxChainPanel Panel() => new() { Track = Track };
        static List<EffectRackCard> Cards(Visual visual) => visual.GetVisualDescendants().OfType<EffectRackCard>().ToList();
        sealed class Launcher : IFxChainEditorLauncher {
            public int VstCalls; public int BrowseCalls; public int Slot = -1;
            public void OpenBuiltInEditor(UTrack track, MixFxModule module) { }
            public void OpenVstEditor(UTrack track, int slotIndex) { VstCalls++; Slot = slotIndex; }
            public void BrowsePlugin(UTrack track, int slotIndex) { BrowseCalls++; Slot = slotIndex; }
        }
        [AvaloniaFact]
        public void LegacyRowsKeepTheirSoundOrderWithoutWritingTheProject() {
            Track.MixFx = new UMixFx { Enabled = true, CompEnabled = false };
            Track.VstSlots.Add(new(0) { PluginUid = "rack:test:missing", Bypassed = true });
            using var panel = Panel();
            Assert.Equal(new[] { "legacy-Eq", "legacy-Compressor", "legacy-Reverb", "vst-0" }, panel.ViewModel.Rows.Select(r => r.EffectId));
            Assert.False(panel.ViewModel.Rows[1].IsPowered); Assert.False(panel.ViewModel.Rows[3].IsPowered);
            Assert.Null(Track.EffectRack);
        }
        [AvaloniaFact]
        public void EmptyRackDoesNotCreateDefaultsAndAddCreatesOnlyOneEffect() {
            using var panel = Panel(); Assert.True(panel.ViewModel.ShowEmptyHint); Assert.Null(Track.EffectRack);
            panel.ViewModel.AddBuiltIn(MixFxModule.Eq); Pump();
            Assert.Single(panel.ViewModel.Rows); Assert.Single(Track.EffectRack!);
            DocManager.Inst.Undo(); Pump(); Assert.Empty(panel.ViewModel.Rows); Assert.Null(Track.EffectRack);
        }
        [AvaloniaFact]
        public void RepeatedAddCreatesIndependentCardsAndCrossKindMoveIsUndoable() {
            using var panel = Panel();
            panel.ViewModel.AddBuiltIn(MixFxModule.Eq); panel.ViewModel.AddBuiltIn(MixFxModule.Compressor); panel.ViewModel.AddBuiltIn(MixFxModule.Eq); Pump();
            Assert.Equal(3, panel.ViewModel.Rows.Count);
            var moved = panel.ViewModel.Rows[2]; var id = moved.EffectId;
            moved.RequestMove(-2); Pump(); Assert.Equal(id, panel.ViewModel.Rows[0].EffectId);
            DocManager.Inst.Undo(); Pump(); Assert.Equal(id, panel.ViewModel.Rows[2].EffectId);
        }
        [AvaloniaFact]
        public void CardPowerAndRemovalAreIndependentUndoSteps() {
            using var panel = Panel(); panel.ViewModel.AddBuiltIn(MixFxModule.Eq); Pump();
            var row = panel.ViewModel.Rows[0]; row.RequestPower(false); Pump(); Assert.False(panel.ViewModel.Rows[0].IsPowered);
            DocManager.Inst.Undo(); Pump(); Assert.True(panel.ViewModel.Rows[0].IsPowered);
            panel.ViewModel.Rows[0].RemoveCommand.Execute(null); Pump(); Assert.Empty(panel.ViewModel.Rows);
            DocManager.Inst.Undo(); Pump(); Assert.Single(panel.ViewModel.Rows); Assert.Null(DocManager.Inst.ActiveUndoGroup);
        }
        [AvaloniaTheory]
        [InlineData(MixFxModule.Eq, false)]
        [InlineData(MixFxModule.Compressor, false)]
        [InlineData(MixFxModule.Reverb, false)]
        [InlineData(MixFxModule.Eq, true)]
        [InlineData(MixFxModule.Compressor, true)]
        [InlineData(MixFxModule.Reverb, true)]
        public void BuiltInRemovalThroughVisibleControlsIsUndoable(MixFxModule module, bool contextMenu) {
            using var panel = Panel(); panel.ViewModel.AddBuiltIn(module); Pump();
            string id = panel.ViewModel.Rows[0].EffectId;
            var window = Host(panel, 680, 320);
            try {
                var card = Assert.Single(Cards(panel));
                if (contextMenu) {
                    var menu = Assert.IsType<MenuFlyout>(card.ContextFlyout);
                    Assert.Single(menu.Items.OfType<MenuItem>()).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                } else {
                    var remove = card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "RemoveButton");
                    var point = remove.TranslatePoint(new Point(remove.Bounds.Width / 2, remove.Bounds.Height / 2), window)!.Value;
                    window.MouseDown(point, MouseButton.Left);
                    window.MouseUp(point, MouseButton.Left);
                }
                Pump(); Assert.Empty(panel.ViewModel.Rows); Assert.Empty(Track.EffectRack!);
                DocManager.Inst.Undo(); Pump();
                Assert.Equal(id, Assert.Single(panel.ViewModel.Rows).EffectId);
                Assert.Equal(id, Assert.Single(Track.EffectRack!).Id);
            } finally { window.Close(); }
        }
        [AvaloniaFact]
        public void UnknownVstKeepsItsNameStateAndDoubleClickDispatchesTheOriginalSlot() {
            Track.VstSlots.Add(new(0) { PluginUid = "rack:test:missing", StateData = new byte[] { 4, 5 } });
            using var panel = Panel(); var launcher = new Launcher(); panel.ViewModel.Launcher = launcher;
            var window = Host(panel);
            try {
                var card = Assert.Single(Cards(panel));
                Assert.Empty(card.GetVisualDescendants().OfType<Knob>());
                var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
                card.RaiseEvent(new PointerPressedEventArgs(card, pointer, card, new Point(20, 20), 0,
                    new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 2));
                Assert.Equal(1, launcher.VstCalls); Assert.Equal(0, launcher.Slot);
                var menu = Assert.IsType<MenuFlyout>(card.ContextFlyout);
                menu.Items.OfType<MenuItem>().First().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.Equal(1, launcher.BrowseCalls); Assert.Equal(0, launcher.Slot);
                Assert.Equal("rack:test:missing", panel.ViewModel.Rows[0].Name);
                Assert.Equal(new byte[] { 4, 5 }, Track.VstSlots[0].StateData);
            } finally { window.Close(); }
        }
        [AvaloniaFact]
        public void ParameterEditsKeepTheSameCardAndReachOnlyTheirOwnInstance() {
            TrackRackCommands.Add(Track, TrackEffectKind.Eq).Execute(); TrackRackCommands.Add(Track, TrackEffectKind.Eq).Execute();
            using var panel = Panel(); var window = Host(panel);
            try {
                var cards = Cards(panel); Assert.Equal(2, cards.Count);
                var view = cards[0].GetVisualDescendants().OfType<BuiltInEffectView>().Single();
                view.ViewModel.EqHighDb = 7; Pump();
                Assert.Equal(7, Track.EffectRack![0].Settings!.EqHighDb); Assert.Equal(3, Track.EffectRack[1].Settings!.EqHighDb);
                Assert.Same(cards[0], Cards(panel)[0]); Assert.Null(DocManager.Inst.ActiveUndoGroup);
                DocManager.Inst.Undo(); Pump(); Assert.Equal(3, view.ViewModel.EqHighDb);
            } finally { window.Close(); }
        }
        [AvaloniaFact]
        public void HorizontalRackScrollsInsteadOfCompressingCardsAtNarrowWidth() {
            for (int i = 0; i < 4; i++) TrackRackCommands.Add(Track, TrackEffectKind.Reverb).Execute();
            using var panel = Panel(); var window = Host(panel, 680, 320);
            try {
                var cards = Cards(panel); Assert.Equal(4, cards.Count);
                Assert.All(cards, card => Assert.InRange(card.Bounds.Width, 300, 310));
                var scroll = panel.GetVisualDescendants().OfType<ScrollViewer>().First(v => v.Name == "RackScroll");
                Assert.True(scroll.Extent.Width > scroll.Viewport.Width);
                Assert.All(cards, card => Assert.True(card.Bounds.Height > 100));
            } finally { window.Close(); }
        }
        [AvaloniaFact]
        public void DeletedInstanceEditorCannotWriteBackIntoAnotherEffect() {
            TrackRackCommands.Add(Track, TrackEffectKind.Eq).Execute();
            string id = Track.EffectRack![0].Id;
            using var model = new MixFxViewModel(Track, id);
            TrackRackCommands.Remove(Track, id).Execute(); model.EqHighDb = 10;
            Assert.Empty(Track.EffectRack!); Assert.False(model.Edits!.IsCurrent);
        }
        [AvaloniaFact]
        public void MixerDevotesOnlyTwoColumnsToChannelsAndMaster() {
            var mixer = new MixerControl(); var window = Host(mixer, 800, 450);
            try {
                var body = mixer.GetVisualDescendants().OfType<Grid>().First(g => g.Name == "MixerBody");
                Assert.Equal(2, body.ColumnDefinitions.Count);
                Assert.Empty(mixer.GetVisualDescendants().OfType<FxChainPanel>());
                Assert.Single(mixer.GetVisualDescendants().OfType<Button>().Where(b => b.Name == "EffectsButton"));
            } finally { window.Close(); mixer.Shutdown(); }
        }
        [AvaloniaFact]
        public void RackStringsResolveInEnglishAndChinese() {
            foreach (var key in new[] { "rack.add", "rack.open", "rack.signal", "rack.empty", "rack.vst.hint", "rack.drag", "rack.missing", "rack.ready", "rack.retry" })
                Assert.NotEqual(key, ThemeManager.GetString(key));
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
                    Assert.Equal(new[] { UidA, UidB }, track.VstSlots.Select(s => s.PluginUid));
                    Assert.Equal(new[] { "ReorderB", "ReorderA" }, vm.Rows.Select(r => r.Name));

                    // Rack boundary: moving the first card earlier must be a no-op.
                    vm.MoveRow(vm.Rows[0], -1);
                    Assert.Equal(new[] { UidA, UidB }, track.VstSlots.Select(s => s.PluginUid));

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
        public void BuiltInRows_CanMoveWithoutChangingPluginSlotsOrLegacyParameters() {
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

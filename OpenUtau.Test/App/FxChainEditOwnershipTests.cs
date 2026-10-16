using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Vst;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.App {
    // Model / one small panel only. Fake slot UIDs are never loaded or removed.
    [Collection("WorkspaceSession")]
    public sealed class FxChainEditOwnershipTests : IDisposable {
        readonly DocManagerTestSetup.ScopedDispatcher dispatcher =
            DocManagerTestSetup.EnterScopedDispatcher(nullChannel: true, installScheduler: false);
        readonly UProject previous = DocManager.Inst.Project;
        readonly UProject project = new();
        readonly FxChainViewModel model = new();
        UTrack A => project.tracks[0];
        UTrack B => project.tracks[1];

        public FxChainEditOwnershipTests() {
            project.tracks.Add(new UTrack { TrackNo = 1, TrackName = "B" });
            A.TrackName = "A";
            A.MixFx = new UMixFx { Enabled = true, EqEnabled = true, CompEnabled = false };
            B.MixFx = new UMixFx { Enabled = true, EqEnabled = true, CompEnabled = false };
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(project));
            model.Attach(A);
        }

        void AddFakeSlots() {
            A.VstSlots = new List<VstPluginSlot> {
                new(0) { PluginUid = "ownership:never-load-a" },
                new(1) { PluginUid = "ownership:never-load-b" },
            };
            model.Rebuild();
        }

        [AvaloniaFact]
        public void BuiltInPower_IsOneUndoAndRedoWithoutAnOpenGroup() {
            model.Rows[0].RequestPower(false);
            Assert.False(TrackEffectRack.IsPowered(A, TrackEffectRack.Read(A)[0]));
            Assert.Null(DocManager.Inst.ActiveUndoGroup);
            DocManager.Inst.Undo();
            Assert.True(A.MixFx.EqEnabled);
            Assert.False(DocManager.Inst.GetUndoState(out _));
            DocManager.Inst.Redo();
            Assert.False(TrackEffectRack.IsPowered(A, TrackEffectRack.Read(A)[0]));
            Assert.False(model.Rows[0].IsPowered);
        }

        [AvaloniaFact]
        public void ForeignGroup_AllWriteEntrypointsRejectWithoutSealingIt() {
            AddFakeSlots();
            DocManager.Inst.StartUndoGroup();
            var foreign = DocManager.Inst.ActiveUndoGroup;
            model.Rows[0].RequestPower(false);
            var vst = model.Rows.First(r => r.Kind == FxChainItemKind.Vst);
            vst.RequestPower(false);
            vst.RemoveCommand.Execute(null);
            vst.RequestMove(1);
            model.AddBuiltIn(MixFxModule.Compressor);
            model.LoadPlugin(0, "ownership:never-load-new");
            Assert.False(model.DropPayload(FxChainDragData.BuiltInPayload(MixFxModule.Compressor)));
            Assert.False(model.DropPayload("ownership:never-load-new"));
            model.Dispose();
            Assert.Same(foreign, DocManager.Inst.ActiveUndoGroup);
            Assert.Empty(foreign!.Commands);
            Assert.True(A.MixFx!.EqEnabled);
            Assert.False(A.MixFx.CompEnabled);
            Assert.All(A.VstSlots, s => Assert.False(s.Bypassed));
            Assert.Equal(new[] { "ownership:never-load-a", "ownership:never-load-b" }, A.VstSlots.Select(s => s.PluginUid));
        }

        [AvaloniaFact]
        public void OldRows_AfterSelectionChangesCannotWriteOrLaunchAnEditor() {
            AddFakeSlots();
            var builtIn = model.Rows[0];
            var vst = model.Rows.First(r => r.Kind == FxChainItemKind.Vst);
            int opened = 0;
            model.EditorRequested += (_, _) => opened++;
            model.Attach(B);
            builtIn.RequestPower(false);
            vst.RequestPower(false);
            vst.RemoveCommand.Execute(null);
            vst.RequestMove(1);
            builtIn.Activate();
            vst.Activate();
            Assert.True(A.MixFx!.EqEnabled);
            Assert.True(B.MixFx!.EqEnabled);
            Assert.All(A.VstSlots, s => Assert.False(s.Bypassed));
            Assert.Equal(0, opened);
            Assert.False(DocManager.Inst.GetUndoState(out _));
        }

        [AvaloniaFact]
        public void SlotIdentityAndPayloadChanges_InvalidateAnUnrefreshedRow() {
            AddFakeSlots();
            var row = model.Rows.First(r => r.Kind == FxChainItemKind.Vst);
            A.VstSlots[0].PluginUid = "ownership:replacement";
            row.RequestPower(false);
            row.RemoveCommand.Execute(null);
            row.RequestMove(1);
            Assert.False(A.VstSlots[0].Bypassed);
            Assert.Equal("ownership:replacement", A.VstSlots[0].PluginUid);
            Assert.False(DocManager.Inst.GetUndoState(out _));
            model.Rebuild();
            row = model.Rows.First(r => r.Kind == FxChainItemKind.Vst);
            A.VstSlots[0] = new VstPluginSlot(0) { PluginUid = "ownership:replacement" };
            row.RequestPower(false);
            Assert.False(A.VstSlots[0].Bypassed);
            Assert.False(DocManager.Inst.GetUndoState(out _));
        }

        [AvaloniaFact]
        public void CapturedMenu_UsesItsOriginalTrackAfterSelectionChanges() {
            var target = model.CaptureTarget()!;
            model.Attach(B);
            Assert.True(model.AddBuiltIn(target, MixFxModule.Compressor));
            Assert.Contains(TrackEffectRack.Read(A), e => e.Kind == TrackEffectKind.Compressor && TrackEffectRack.IsPowered(A, e));
            Assert.False(B.MixFx!.CompEnabled);
            DocManager.Inst.Undo();
            Assert.False(A.MixFx.CompEnabled);
            Assert.False(B.MixFx.CompEnabled);
        }

        [AvaloniaFact]
        public void CapturedPicker_RejectsReplacedAndNewlyOccupiedSlots() {
            AddFakeSlots();
            var replacement = model.CaptureTarget(0)!;
            var append = model.CaptureTarget(2)!;
            A.VstSlots[0].PluginUid = "ownership:replacement";
            A.VstSlots.Add(new VstPluginSlot(2) { PluginUid = "ownership:occupied" });
            model.Attach(B);
            Assert.Same(A, replacement.Track);
            Assert.False(model.LoadPlugin(replacement, 0, "ownership:never-load-new"));
            Assert.False(model.LoadPlugin(append, 2, "ownership:never-load-new"));
            Assert.Equal("ownership:replacement", A.VstSlots[0].PluginUid);
            Assert.Equal("ownership:occupied", A.VstSlots[2].PluginUid);
            Assert.Empty(B.VstSlots);
            Assert.False(DocManager.Inst.GetUndoState(out _));
        }

        [AvaloniaFact]
        public void RemovedTrackAndChangedDocument_DoNotBecomeStandalonePreviews() {
            var target = model.CaptureTarget()!;
            project.tracks.Remove(A);
            Assert.False(model.AddBuiltIn(target, MixFxModule.Compressor));
            // Restore the original object, then change document with its capture still alive.
            project.tracks.Insert(0, target.Track);
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(new UProject()));
            Assert.False(model.AddBuiltIn(target, MixFxModule.Compressor));
            model.AddBuiltIn(MixFxModule.Compressor);
            Assert.False(target.Track.MixFx!.CompEnabled);
            Assert.Null(model.Track);
            Assert.False(DocManager.Inst.GetUndoState(out _));
        }

        [AvaloniaFact]
        public void TemporaryUnsubscribe_InvalidatesOldTargetsAndRebuildsOnResubscribe() {
            var row = model.Rows[0];
            var target = model.CaptureTarget()!;
            model.Unsubscribe();
            A.MixFx!.EqEnabled = false;
            row.RequestPower(true);
            Assert.False(model.AddBuiltIn(target, MixFxModule.Compressor));
            model.Subscribe();
            model.Subscribe();
            Assert.Equal(3, model.Rows.Count);
            Assert.False(model.Rows[0].IsPowered);
            model.Rows[0].RequestPower(true);
            Assert.True(TrackEffectRack.IsPowered(A, TrackEffectRack.Read(A)[0]));
            Assert.Null(DocManager.Inst.ActiveUndoGroup);
        }

        [AvaloniaFact]
        public void FinalDispose_IgnoresQueuedRebuildAndCannotResubscribeOrAttach() {
            var target = model.CaptureTarget()!;
            Task.Run(model.Rebuild).GetAwaiter().GetResult();
            model.Dispose();
            Dispatcher.UIThread.RunJobs();
            model.Subscribe();
            model.Attach(B);
            model.Rebuild();
            Assert.Empty(model.Rows);
            Assert.Null(model.Track);
            Assert.False(model.AddBuiltIn(target, MixFxModule.Compressor));
            Assert.False(A.MixFx!.CompEnabled);
        }

        [AvaloniaFact]
        public void PanelRebindAndShutdown_IgnoreQueuedSelectionAndReleaseSubscriptions() {
            using var panel = new FxChainPanel();
            using var first = new Subject<UTrack?>();
            using var second = new Subject<UTrack?>();
            panel.BindSelection(first);
            Task.Run(() => first.OnNext(A)).GetAwaiter().GetResult();
            panel.BindSelection(second);
            second.OnNext(B);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(B, panel.ViewModel.Track);
            Assert.False(first.HasObservers);
            Task.Run(() => second.OnNext(A)).GetAwaiter().GetResult();
            int version = panel.SelectionVersion;
            panel.Shutdown();
            panel.Shutdown();
            Dispatcher.UIThread.RunJobs();
            panel.Track = A;
            panel.BindSelection(first);
            Assert.False(panel.IsSelectionCurrent(version));
            Assert.False(second.HasObservers);
            Assert.False(first.HasObservers);
            Assert.Null(panel.ViewModel.Track);
            Assert.Empty(panel.ViewModel.Rows);
        }

        [AvaloniaFact]
        public void IndependentPreview_DoesNotWriteOrSealDocumentHistory() {
            var preview = new UTrack { MixFx = new UMixFx { Enabled = true, EqEnabled = true } };
            model.Attach(preview);
            DocManager.Inst.StartUndoGroup();
            var foreign = DocManager.Inst.ActiveUndoGroup;
            model.Rows[0].RequestPower(false);
            Assert.False(TrackEffectRack.IsPowered(preview, TrackEffectRack.Read(preview)[0]));
            Assert.Same(foreign, DocManager.Inst.ActiveUndoGroup);
            Assert.Empty(foreign!.Commands);
            Assert.True(A.MixFx!.EqEnabled);
        }

        [AvaloniaFact]
        public void QueuedSelection_FromPreviousDocumentCannotAttachItAsAPreview() {
            using var panel = new FxChainPanel();
            using var selection = new Subject<UTrack?>();
            panel.BindSelection(selection);
            Task.Run(() => selection.OnNext(A)).GetAwaiter().GetResult();
            var next = new UProject();
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(next));
            panel.Track = next.tracks[0];
            Dispatcher.UIThread.RunJobs();
            Assert.Same(next.tracks[0], panel.ViewModel.Track);
            Assert.False(A.MixFx!.CompEnabled);
        }

        public void Dispose() {
            model.Dispose();
            if (DocManager.Inst.ActiveUndoGroup != null) DocManager.Inst.EndUndoGroup();
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(previous));
            dispatcher.Dispose();
        }
    }
}

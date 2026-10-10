using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using OpenUtau.App.Controls;
using OpenUtau.App.Views;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.App {
    [Collection("WorkspaceSession")]
    public class NoteGestureLifetimeTests : IDisposable {
        readonly DocManagerTestSetup.ScopedDispatcher dispatcher = DocManagerTestSetup.EnterScopedDispatcher();
        readonly UProject previous = DocManager.Inst.Project;
        readonly UProject project = new UProject();
        readonly UVoicePart part = new UVoicePart { Duration = 1920 };
        readonly Tip tip = new Tip();

        sealed class Tip : IValueTip {
            public bool Visible;
            public void ShowValueTip() => Visible = true;
            public void HideValueTip() => Visible = false;
            public void UpdateValueTip(string text) { }
        }
        sealed class Gesture : NoteEditState {
            public Gesture(Control control, Tip tip) : base(control, null!, tip) { }
            public bool Execute(UCommand cmd) => ExecuteCmd(cmd);
        }
        public NoteGestureLifetimeTests() {
            project.parts.Add(part);
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(project));
        }
        [AvaloniaFact]
        public void Cancellation_ReleasesCaptureAndTip_Idempotently_WithoutClosingForeignUndoGroup() {
            var control = new Border();
            var window = new Window { Width = 100, Height = 100, Content = control };
            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            var state = new Gesture(control, tip);
            try {
                window.Show();
                state.Begin(pointer, new Point(10, 10));
                Assert.Null(DocManager.Inst.ActiveUndoGroup); // No commands: no empty history entry.
                Assert.True(tip.Visible);
                DocManager.Inst.StartUndoGroup();
                var foreign = DocManager.Inst.ActiveUndoGroup;
                Assert.False(state.Execute(new RemoveNoteCommand(part, new UNote())));
                state.Cancel();
                state.Cancel();
                Assert.False(tip.Visible);
                Assert.Null(pointer.Captured);
                Assert.Same(foreign, DocManager.Inst.ActiveUndoGroup);
                Assert.Empty(foreign!.Commands);
            } finally { state.Cancel(); window.Close(); }
        }
        [AvaloniaFact]
        public void StaleProjectOrRemovedPart_IsRejectedBeforeMutation() {
            var control = new Border();
            var state = new Gesture(control, tip);
            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            state.Begin(pointer, new Point());
            project.parts.Remove(part);
            Assert.False(state.Execute(new AddNoteCommand(part, new UNote())));
            Assert.Empty(part.notes);
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(new UProject()));
            DocManager.Inst.StartUndoGroup();
            var foreign = DocManager.Inst.ActiveUndoGroup;
            Assert.False(state.Execute(new AddNoteCommand(part, new UNote())));
            state.Cancel();
            Assert.Same(foreign, DocManager.Inst.ActiveUndoGroup);
        }
        [AvaloniaFact]
        public void PitchPreviewCancellation_DoesNotNeedAPointerOrCreateUndoHistory() {
            var preview = new Polyline();
            var state = new PitchCurveState(new Border(), null!, tip, preview, PitchCurveState.CurveMode.Sine, false);
            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            state.Begin(pointer, new Point(10, 10));
            Assert.True(preview.IsVisible);
            Assert.Null(DocManager.Inst.ActiveUndoGroup);
            state.Cancel(null);
            state.Cancel();
            Assert.False(preview.IsVisible);
            Assert.Empty(preview.Points);
            Assert.Null(pointer.Captured);
            Assert.False(tip.Visible);
        }
        public void Dispose() {
            if (DocManager.Inst.ActiveUndoGroup != null) DocManager.Inst.EndUndoGroup();
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(previous));
            dispatcher.Dispose();
        }
    }
}

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using OpenUtau.App.Editing;
using OpenUtau.App.Views;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.App {
    [Collection("WorkspaceSession")]
    public class DocumentEditGestureTests : IDisposable {
        readonly DocManagerTestSetup.ScopedDispatcher dispatcher =
            DocManagerTestSetup.EnterScopedDispatcher(nullChannel: true, installScheduler: false);
        readonly UProject previous = DocManager.Inst.Project;
        DocManager Doc => DocManager.Inst;

        UVoicePart Load() {
            var project = new UProject { Saved = true };
            var part = new UVoicePart { Duration = 1920 };
            project.parts.Add(part);
            Doc.ExecuteCmd(new LoadProjectNotification(project));
            return part;
        }

        [Fact]
        public void MultipleUpdates_FormOneUndo_AndNoMovementCreatesNoHistory() {
            var part = Load();
            using (var empty = new DocumentEditGesture(Doc.Project, "command.part.move")) {
                Assert.Null(Doc.ActiveUndoGroup);
            }
            using (var gesture = new DocumentEditGesture(Doc.Project, "command.part.move")) {
                Assert.True(gesture.Execute(new MovePartCommand(Doc.Project, part, 480, 0)));
                Assert.True(gesture.Execute(new MovePartCommand(Doc.Project, part, 960, 0)));
            }
            Assert.Equal(960, part.position);
            Doc.Undo();
            Assert.Equal(0, part.position);
            Doc.Redo();
            Assert.Equal(960, part.position);
        }

        [Fact]
        public void SealedGroupDuringInput_CanContinueInANewOwnedGroup() {
            var part = Load();
            using var gesture = new DocumentEditGesture(Doc.Project, "command.part.move");
            gesture.Execute(new MovePartCommand(Doc.Project, part, 480, 0));
            Doc.EndUndoGroup(); // Save/Undo seals an active group while a pointer may remain pressed.
            Assert.True(gesture.Execute(new MovePartCommand(Doc.Project, part, 960, 0)));
            gesture.Dispose();
            Doc.Undo();
            Assert.Equal(480, part.position);
            Doc.Undo();
            Assert.Equal(0, part.position);
        }

        [Fact]
        public void ForeignTransaction_IsNeitherAppendedToNorEndedByGesture() {
            var part = Load();
            using var gesture = new DocumentEditGesture(Doc.Project, "command.part.move");
            gesture.Execute(new MovePartCommand(Doc.Project, part, 480, 0));
            Doc.EndUndoGroup();
            Doc.StartUndoGroup("command.track.setting");
            var foreign = Doc.ActiveUndoGroup;
            Assert.False(gesture.Execute(new MovePartCommand(Doc.Project, part, 960, 0)));
            gesture.Dispose();
            Assert.Same(foreign, Doc.ActiveUndoGroup);
            Assert.Equal(480, part.position);
            Assert.Empty(foreign!.Commands);
        }

        [Fact]
        public void ProjectSwitch_RejectsStaleCommandsAndPreservesNewTransaction() {
            var part = Load();
            var old = Doc.Project;
            using var gesture = new DocumentEditGesture(old, "command.part.move");
            gesture.Execute(new MovePartCommand(old, part, 480, 0));
            Doc.ExecuteCmd(new LoadProjectNotification(new UProject()));
            Doc.StartUndoGroup();
            var current = Doc.ActiveUndoGroup;
            Assert.False(gesture.Execute(new MovePartCommand(old, part, 960, 0)));
            gesture.Dispose();
            Assert.Same(current, Doc.ActiveUndoGroup);
            Assert.Equal(480, part.position);
        }

        [AvaloniaFact]
        public void CaptureCancellation_IsIdempotentAndDoesNotCloseOtherControlsTransaction() {
            Load();
            var control = new Border();
            var window = new Window { Width = 100, Height = 100, Content = control };
            var state = new PartEditState(control, null!); // Base input lifecycle needs no MainWindow/VM.
            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            try {
                window.Show();
                state.Begin(pointer, new Point(10, 10));
                Assert.Same(control, pointer.Captured);
                Doc.StartUndoGroup();
                var foreign = Doc.ActiveUndoGroup;
                state.Cancel();
                state.Cancel();
                Assert.Null(pointer.Captured);
                Assert.Same(foreign, Doc.ActiveUndoGroup);
            } finally { state.Cancel(); window.Close(); }
        }

        public void Dispose() {
            if (Doc.ActiveUndoGroup != null) Doc.EndUndoGroup();
            Doc.ExecuteCmd(new LoadProjectNotification(previous));
            dispatcher.Dispose();
        }
    }
}

using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using OpenUtau.App.Editing;
using OpenUtau.App.ViewModels;
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
        public void NewPartCanBeAddedOnce_AndStaleExistingPartCommandsRemainRejected() {
            Load();
            var project = Doc.Project;
            var added = new UVoicePart { Duration = 1920 };
            using (var gesture = new DocumentEditGesture(project, "command.part.add")) {
                Assert.True(gesture.Execute(new AddPartCommand(project, added)));
                Assert.False(gesture.Execute(new AddPartCommand(project, added)));
            }
            Assert.Equal(2, project.parts.Count);
            Doc.Undo();
            Assert.DoesNotContain(added, project.parts);
            using var stale = new DocumentEditGesture(project, "command.part.move");
            Assert.False(stale.Execute(new MovePartCommand(project, added, 480, 0)));
            Assert.Null(Doc.ActiveUndoGroup);
            Doc.Redo();
            Assert.Contains(added, project.parts);
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
        public void ActualPartMove_DoesNotRetargetWhenSelectionChangesDuringCapture() {
            var part = Load();
            var other = new UVoicePart { position = 2400, Duration = 1920 };
            Doc.Project.parts.Add(other);
            using var tracks = new TracksViewModel();
            var state = new PartMoveEditState(new Border(), tracks, part);
            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            try {
                state.Begin(pointer, tracks.TickTrackToPoint(0, 0));
                state.Update(pointer, tracks.TickTrackToPoint(960, 0));
                Assert.Equal(960, part.position);
                tracks.DeselectParts(); tracks.SelectPart(other);
                state.Update(pointer, tracks.TickTrackToPoint(1440, 0));
                Assert.Equal(960, part.position);
                Assert.Equal(2400, other.position);
                state.Cancel();
                Doc.Undo();
                Assert.Equal(0, part.position);
                Assert.Equal(2400, other.position);
            } finally { state.Cancel(); }
        }

        [AvaloniaFact]
        public void ActualPartMove_ReentrantSelectionChangeStopsTheRestOfTheBatch() {
            var part = Load();
            var other = new UVoicePart { position = 2400, Duration = 1920 };
            var replacement = new UVoicePart { position = 4800, Duration = 1920 };
            Doc.Project.parts.Add(other); Doc.Project.parts.Add(replacement);
            using var tracks = new TracksViewModel();
            tracks.SelectPart(part); tracks.SelectPart(other);
            Assert.Equal(2, tracks.SelectedParts.Count);
            var state = new PartMoveEditState(new Border(), tracks, part);
            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            int changes = 0;
            var subscriber = new SelectionChange(cmd => {
                if (cmd is MovePartCommand) {
                    changes++;
                    tracks.DeselectParts(); tracks.SelectPart(replacement);
                }
            });
            Doc.AddSubscriber(subscriber);
            try {
                state.Begin(pointer, tracks.TickTrackToPoint(0, 0));
                state.Update(pointer, tracks.TickTrackToPoint(960, 0));
                Assert.Equal(1, changes);
                Assert.Equal(1, new[] { part.position != 0, other.position != 2400 }.Count(changed => changed));
                Assert.Equal(4800, replacement.position);
            } finally { Doc.RemoveSubscriber(subscriber); state.Cancel(); }
        }

        sealed class SelectionChange(Action<UCommand> callback) : ICmdSubscriber {
            public void OnNext(UCommand command, bool isUndo) => callback(command);
        }

        [AvaloniaFact]
        public void ActualPartMove_RemovedTargetOrForeignTransactionCannotBeEdited() {
            var part = Load();
            using var tracks = new TracksViewModel();
            var state = new PartMoveEditState(new Border(), tracks, part);
            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            try {
                state.Begin(pointer, tracks.TickTrackToPoint(0, 0));
                Doc.Project.parts.Remove(part);
                state.Update(pointer, tracks.TickTrackToPoint(960, 0));
                Assert.Equal(0, part.position);
                Assert.Null(Doc.ActiveUndoGroup);
                Doc.Project.parts.Add(part);
                Doc.StartUndoGroup();
                var foreign = Doc.ActiveUndoGroup;
                state.Update(pointer, tracks.TickTrackToPoint(960, 0));
                state.Cancel();
                Assert.Equal(0, part.position);
                Assert.Same(foreign, Doc.ActiveUndoGroup);
                Assert.Empty(foreign!.Commands);
            } finally { state.Cancel(); }
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

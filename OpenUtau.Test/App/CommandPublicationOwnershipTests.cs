using System;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.App {
    [Collection("WorkspaceSession")]
    public class CommandPublicationOwnershipTests : IDisposable, ICmdSubscriber {
        readonly DocManagerTestSetup.ScopedDispatcher dispatcher = DocManagerTestSetup.EnterScopedDispatcher();
        readonly UProject previous = DocManager.Inst.Project;
        Action? onMove;

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SubscriberCanCloseGestureOrSwitchProject_WithoutValidatingForeignDocument(bool switchProject) {
            var project = new UProject();
            project.tracks.Add(new UTrack());
            var part = new UVoicePart { Duration = 1920 };
            project.parts.Add(part);
            var doc = DocManager.Inst;
            doc.ExecuteCmd(new LoadProjectNotification(project));
            doc.AddSubscriber(this);
            var next = new UProject();
            var nextPart = new UVoicePart { Duration = 1920 };
            next.parts.Add(nextPart);
            next.tracks.Add(new UTrack());
            long revision = nextPart.GetRenderRequest().revision;
            onMove = () => {
                doc.EndUndoGroup();
                if (switchProject) doc.ExecuteCmd(new LoadProjectNotification(next));
                doc.StartUndoGroup("command.track.setting", deferValidate: true);
            };
            doc.StartUndoGroup();
            doc.ExecuteCmd(new MovePartCommand(project, part, 480, 0));
            Assert.Equal(480, part.position);
            Assert.NotNull(doc.ActiveUndoGroup);
            Assert.Empty(doc.ActiveUndoGroup!.Commands);
            Assert.Equal(revision, nextPart.GetRenderRequest().revision);
            if (switchProject) Assert.Same(next, doc.Project);
            else {
                doc.EndUndoGroup();
                doc.Undo();
                Assert.Equal(0, part.position);
            }
        }

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is MovePartCommand && !isUndo) {
                var callback = onMove;
                onMove = null;
                callback?.Invoke();
            }
        }
        public void Dispose() {
            var doc = DocManager.Inst;
            doc.RemoveSubscriber(this);
            if (doc.ActiveUndoGroup != null) doc.EndUndoGroup();
            doc.ExecuteCmd(new LoadProjectNotification(previous));
            dispatcher.Dispose();
        }
    }
}

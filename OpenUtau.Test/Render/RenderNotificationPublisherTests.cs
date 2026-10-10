using System;
using System.Collections.Generic;
using System.Threading;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.Render {
    [Collection("WorkspaceSession")]
    public class RenderNotificationPublisherTests : IDisposable, ICmdSubscriber {
        readonly DocManagerTestSetup.ScopedDispatcher dispatcher = DocManagerTestSetup.EnterScopedDispatcher();
        readonly UProject previous = DocManager.Inst.Project;
        readonly UProject project = new();
        readonly List<ProgressBarNotification> received = new();
        public RenderNotificationPublisherTests() {
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(project));
            DocManager.Inst.AddSubscriber(this);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void QueuedProgressCannotReachAnotherProjectOrCancelledJob(bool switchProject) {
            using var source = new CancellationTokenSource();
            var progress = new Progress(1, project, source.Token);
            progress.Complete(1, "Old render");
            Assert.Equal(1, dispatcher.PendingCount);
            if (switchProject) DocManager.Inst.ExecuteCmd(new LoadProjectNotification(new UProject()));
            else source.Cancel();
            dispatcher.PumpAll();
            Assert.Empty(received);
        }

        [Fact]
        public void QueuedCompletionCannotRestoreProgressAfterClear() {
            var progress = new Progress(1, project, CancellationToken.None);
            progress.Complete(1, "Done");
            progress.Clear();
            dispatcher.PumpAll();
            var result = Assert.Single(received);
            Assert.Equal(0, result.Progress);
            Assert.Equal(string.Empty, result.Info);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 100)]
        public void EmptyOrOverCompletedProgressIsFiniteAndBounded(int total, double expected) {
            new Progress(total, project, CancellationToken.None).Complete(2, "Done");
            dispatcher.PumpAll();
            Assert.Equal(expected, Assert.Single(received).Progress);
        }

        [Fact]
        public void QueuedFailureIsDiscardedAfterCancellation() {
            using var source = new CancellationTokenSource();
            RenderNotificationPublisher.Queue(project, source.Token, new ProgressBarNotification(0, "Failed"));
            source.Cancel();
            dispatcher.PumpAll();
            Assert.Empty(received);
        }

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is ProgressBarNotification progress) received.Add(progress);
        }
        public void Dispose() {
            DocManager.Inst.RemoveSubscriber(this);
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(previous));
            dispatcher.Dispose();
        }
    }
}

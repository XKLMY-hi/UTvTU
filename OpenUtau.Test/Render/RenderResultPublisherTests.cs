using System;
using System.Threading;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Ustx;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.Render {
    [Collection("WorkspaceSession")]
    public class RenderResultPublisherTests : IDisposable, ICmdSubscriber {
        readonly DocManagerTestSetup.ScopedDispatcher dispatcher = DocManagerTestSetup.EnterScopedDispatcher();
        readonly UProject previous = DocManager.Inst.Project;
        readonly UProject project = new UProject();
        readonly UVoicePart part = new UVoicePart { Duration = 1920 };
        int notifications;

        public RenderResultPublisherTests() {
            project.parts.Add(part);
            project.tracks.Add(new UTrack());
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(project));
            DocManager.Inst.AddSubscriber(this);
        }

        RenderPartRequest Request(bool complete = true) {
            var request = part.GetRenderRequest();
            request.sources = new[] { new WaveSource(0, 1, 0, 1) };
            if (complete) request.sources[0].SetSamples(new float[] { 0.1f });
            request.mix = new WaveMix(request.sources);
            return request;
        }

        [Fact]
        public void Completion_IsQueuedAndPublishedOnce_WhenMultipleWorkersFinish() {
            var request = Request();
            RenderResultPublisher.Queue(project, request, CancellationToken.None);
            RenderResultPublisher.Queue(project, request, CancellationToken.None);
            Assert.Null(part.Mix);
            Assert.Equal(1, dispatcher.PendingCount);
            dispatcher.PumpAll();
            Assert.Same(request.mix, part.Mix);
            Assert.Equal(1, notifications);
        }

        [Fact]
        public void OldRevision_CannotOverwriteNewMix_EvenIfCompletedLast() {
            var old = Request();
            part.Validate(new ValidateOptions { SkipPhonemizer = true }, project, project.tracks[0]);
            var current = Request();
            RenderResultPublisher.Queue(project, current, CancellationToken.None);
            RenderResultPublisher.Queue(project, old, CancellationToken.None);
            dispatcher.PumpAll();
            Assert.Same(current.mix, part.Mix);
            Assert.Equal(1, notifications);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void QueuedCompletion_RechecksProjectAndPartOwnership(bool switchProject) {
            var request = Request();
            RenderResultPublisher.Queue(project, request, CancellationToken.None);
            if (switchProject) DocManager.Inst.ExecuteCmd(new LoadProjectNotification(new UProject()));
            else project.parts.Remove(part);
            dispatcher.PumpAll();
            Assert.Null(part.Mix);
            Assert.Equal(0, notifications);
        }

        [Fact]
        public void CancellationAfterQueue_DoesNotPublish() {
            using var cancellation = new CancellationTokenSource();
            RenderResultPublisher.Queue(project, Request(), cancellation.Token);
            cancellation.Cancel();
            dispatcher.PumpAll();
            Assert.Null(part.Mix);
            Assert.Equal(0, notifications);
        }

        [Fact]
        public void IncompleteSources_DoNotConsumePublicationSlot() {
            var request = Request(false);
            RenderResultPublisher.Queue(project, request, CancellationToken.None);
            Assert.Equal(0, dispatcher.PendingCount);
            request.sources[0].SetSamples(new float[] { 0.1f });
            RenderResultPublisher.Queue(project, request, CancellationToken.None);
            dispatcher.PumpAll();
            Assert.Same(request.mix, part.Mix);
            Assert.Equal(1, notifications);
        }

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is PartRenderedNotification) ++notifications;
        }
        public void Dispose() {
            DocManager.Inst.RemoveSubscriber(this);
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(previous));
            dispatcher.Dispose();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenUtau.App.Views;
using Xunit;

namespace OpenUtau.Test.App {
    // Exercises the exact job/queue helper used by RenderWindow. No window,
    // document, audio device, synthesis, native plugin or real file export.
    public sealed class RenderWindowJobOwnershipTests {
        sealed class UiQueue {
            readonly Queue<Action> actions = new();
            internal int Count => actions.Count;
            internal void Post(Action action) => actions.Enqueue(action);
            internal void Drain() {
                while (actions.TryDequeue(out var action)) action();
            }
        }

        [Fact]
        public void ClosedWindow_IgnoresQueuedProgressAndCompletionUntilWorkerExits() {
            var queue = new UiQueue();
            var owner = new RenderWindowJobOwner(queue.Post);
            var job = owner.TryBegin(new[] { "Original" })!;
            int updates = 0;
            owner.PostProgress(job, () => updates++);
            owner.Close();
            owner.Close();
            Assert.True(job.Token.IsCancellationRequested);
            Assert.True(job.IsRunning);
            Assert.Equal(job.Token, job.Source.Token); // Close cancelled, but did not dispose.
            queue.Drain();
            owner.Finish(job, null, _ => updates++);
            owner.PostProgress(job, () => updates++);
            queue.Drain();
            Assert.Equal(0, updates);
            Assert.False(job.IsRunning);
            Assert.Throws<ObjectDisposedException>(() => { _ = job.Source.Token; });
            Assert.Null(owner.TryBegin(new[] { "Late" }));
        }

        [Fact]
        public void OldFinallyAndProgress_CannotChangeAReplacementJobsUi() {
            var queue = new UiQueue();
            var owner = new RenderWindowJobOwner(queue.Post);
            var first = owner.TryBegin(new[] { "First" })!;
            bool enabled = false;
            string label = "First";
            owner.PostProgress(first, () => label = "Old progress");
            owner.Finish(first, new InvalidOperationException("Old failure"), _ => { label = "Old failure"; enabled = true; });
            var second = owner.TryBegin(new[] { "Second" })!;
            label = "Second";
            queue.Drain();
            Assert.Equal("Second", label);
            Assert.False(enabled);
            owner.PostProgress(first, () => label = "Late first");
            Assert.Equal(0, queue.Count);
            owner.PostProgress(second, () => label = "Current progress");
            queue.Drain();
            Assert.Equal("Current progress", label);
            owner.Finish(second, null, outcome => { label = outcome.ToString(); enabled = true; });
            queue.Drain();
            Assert.Equal("Completed", label);
            Assert.True(enabled);
            owner.Close();
        }

        [Fact]
        public async Task CancelBeforeWorkerStarts_StillRunsFinallyAndDisposesAfterItsExit() {
            var owner = new RenderWindowJobOwner(_ => { });
            var job = owner.TryBegin(Array.Empty<string>())!;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var worker = Task.Run(async () => {
                Exception? error = null;
                try {
                    await start.Task;
                    job.Token.ThrowIfCancellationRequested();
                } catch (OperationCanceledException ex) { error = ex; }
                finally { owner.Finish(job, error, _ => { }); }
            });
            owner.Close();
            Assert.Equal(job.Token, job.Source.Token);
            start.SetResult();
            await worker;
            Assert.False(job.IsRunning);
            Assert.Throws<ObjectDisposedException>(() => { _ = job.Source.Token; });
        }

        [Fact]
        public void CancellationHasItsOwnOutcome_AndDoesNotBecomeGenericFailure() {
            var queue = new UiQueue();
            var owner = new RenderWindowJobOwner(queue.Post);
            RenderWindowJobOwner.Outcome? outcome = null;
            var job = owner.TryBegin(Array.Empty<string>())!;
            job.Cancel();
            owner.PostProgress(job, () => throw new InvalidOperationException("Cancelled progress ran"));
            owner.Finish(job, new InvalidOperationException("Renderer failed while cancelling"), value => outcome = value);
            queue.Drain();
            Assert.Equal(RenderWindowJobOwner.Outcome.Cancelled, outcome);
            var internalCancel = owner.TryBegin(Array.Empty<string>())!;
            owner.Finish(internalCancel, new OperationCanceledException(), value => outcome = value);
            queue.Drain();
            Assert.Equal(RenderWindowJobOwner.Outcome.Cancelled, outcome);
            var failure = owner.TryBegin(Array.Empty<string>())!;
            owner.Finish(failure, new InvalidOperationException("Actual error"), value => outcome = value);
            queue.Drain();
            Assert.Equal(RenderWindowJobOwner.Outcome.Failed, outcome);
            owner.Close();
        }

        [Fact]
        public void TrackLabels_AreCapturedAndUnknownIndicesAreSafe() {
            var names = new List<string> { "A", "B" };
            var owner = new RenderWindowJobOwner(action => action());
            var job = owner.TryBegin(names)!;
            names[0] = "Renamed";
            names.Clear();
            Assert.Equal("A", job.TrackName(0));
            Assert.Equal("B", job.TrackName(1));
            Assert.Null(job.TrackName(-1));
            Assert.Null(job.TrackName(2));
            Assert.Null(job.TrackName(int.MaxValue));
            owner.Finish(job, null, _ => { });
            owner.Close();
        }

        [Fact]
        public void CancellationCallbacks_CanCompleteWorkerWithoutDisposingSourceMidCancel() {
            var owner = new RenderWindowJobOwner(_ => { });
            var job = owner.TryBegin(Array.Empty<string>())!;
            bool callbackRan = false;
            using var observer = job.Token.Register(() => {
                Assert.Equal(job.Token, job.Source.Token);
                callbackRan = true;
            });
            // Registrations run in reverse order: completion happens before observer.
            using var completes = job.Token.Register(() => owner.Finish(job, null, _ => { }));
            owner.Close();
            Assert.True(callbackRan);
            Assert.False(job.IsRunning);
            Assert.Throws<ObjectDisposedException>(() => { _ = job.Source.Token; });
        }

        [Fact]
        public void ThrowingCancellationCallback_DoesNotPreventFinalCleanup() {
            var owner = new RenderWindowJobOwner(_ => { });
            var job = owner.TryBegin(Array.Empty<string>())!;
            using var registration = job.Token.Register(() => throw new InvalidOperationException("Callback"));
            Assert.Null(Record.Exception(owner.Close));
            Assert.Equal(job.Token, job.Source.Token);
            owner.Finish(job, new OperationCanceledException(job.Token), _ => { });
            Assert.Throws<ObjectDisposedException>(() => { _ = job.Source.Token; });
        }

        [Fact]
        public void CallbackProgress_UsesOneOwnedQueueAndRejectsConcurrentStart() {
            var queue = new UiQueue();
            var owner = new RenderWindowJobOwner(queue.Post);
            var job = owner.TryBegin(Array.Empty<string>())!;
            Assert.Null(owner.TryBegin(Array.Empty<string>()));
            double value = 0;
            var progress = new RenderWindowJobOwner.CallbackProgress<double>(p => owner.PostProgress(job, () => value = p));
            progress.Report(0.5);
            Assert.Equal(1, queue.Count);
            Assert.Equal(0, value);
            queue.Drain();
            Assert.Equal(0.5, value);
            owner.Finish(job, null, _ => { });
            queue.Drain();
            progress.Report(0.9);
            Assert.Equal(0, queue.Count);
            owner.Close();
        }
    }
}

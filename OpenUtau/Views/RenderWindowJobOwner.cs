using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace OpenUtau.App.Views {
    /// <summary>One window owns one export at a time; queued UI work belongs to that job.</summary>
    internal sealed class RenderWindowJobOwner {
        readonly object sync = new();
        readonly Action<Action> post;
        Job? current;
        bool closed;

        internal RenderWindowJobOwner(Action<Action> post) => this.post = post;

        internal Job? TryBegin(IEnumerable<string> trackNames) {
            lock (sync) {
                if (closed || current?.IsRunning == true) return null;
                return current = new Job(trackNames.ToArray());
            }
        }

        bool IsCurrent(Job job, bool progress) {
            lock (sync) {
                return !closed && ReferenceEquals(current, job) &&
                    (!progress || (job.IsRunning && !job.Token.IsCancellationRequested));
            }
        }

        internal void PostProgress(Job job, Action update) {
            if (!IsCurrent(job, progress: true)) return;
            post(() => {
                if (IsCurrent(job, progress: true)) update();
            });
        }

        internal void Finish(Job job, Exception? error, Action<Outcome> update) {
            var outcome = job.Token.IsCancellationRequested || error is OperationCanceledException
                ? Outcome.Cancelled : error == null ? Outcome.Completed : Outcome.Failed;
            job.Complete();
            if (!IsCurrent(job, progress: false)) return;
            post(() => {
                if (IsCurrent(job, progress: false)) update(outcome);
            });
        }

        internal void Close() {
            Job? job;
            lock (sync) {
                if (closed) return;
                closed = true;
                job = current;
            }
            job?.Cancel();
        }

        internal enum Outcome { Completed, Cancelled, Failed }

        internal sealed class Job {
            readonly object sync = new();
            readonly string[] trackNames;
            bool finished, canceling, cancelRequested, disposed;
            internal CancellationTokenSource Source { get; } = new();
            internal CancellationToken Token { get; }
            internal bool IsRunning { get { lock (sync) return !finished; } }
            internal Job(string[] trackNames) {
                this.trackNames = trackNames;
                Token = Source.Token;
            }
            internal string? TrackName(int index) => index >= 0 && index < trackNames.Length ? trackNames[index] : null;

            internal void Cancel() {
                lock (sync) {
                    if (finished || cancelRequested) return;
                    cancelRequested = true;
                    canceling = true;
                }
                // Cancellation callbacks may themselves complete work. Keep the source
                // alive through Cancel and do not hold a lock while invoking callbacks.
                try { Source.Cancel(); }
                catch (AggregateException error) { Serilog.Log.Warning(error, "An export cancellation callback failed."); }
                finally {
                    lock (sync) {
                        canceling = false;
                        DisposeIfFinished();
                    }
                }
            }

            internal void Complete() {
                lock (sync) {
                    finished = true;
                    DisposeIfFinished();
                }
            }

            void DisposeIfFinished() {
                if (!finished || canceling || disposed) return;
                disposed = true;
                Source.Dispose();
            }
        }

        // Unlike Progress<T>, this reports synchronously to the guarded UI queue and
        // cannot introduce a second, unowned synchronization-context callback.
        internal sealed class CallbackProgress<T> : IProgress<T> {
            readonly Action<T> report;
            internal CallbackProgress(Action<T> report) => this.report = report;
            public void Report(T value) => report(value);
        }
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.Util;

namespace OpenUtau.Classic {
    /// <summary>A process-wide budget, shared by overlapping render generations and phrases.</summary>
    internal sealed class ClassicRenderScheduler {
        internal static readonly ClassicRenderScheduler Shared = new(() =>
            Math.Clamp(Preferences.Default.NumRenderThreads, 1, Math.Max(1, Math.Min(8, Environment.ProcessorCount - 1))));

        readonly Func<int> limit;
        readonly object sync = new();
        TaskCompletionSource<bool> changed = NewSignal();
        int active;

        internal ClassicRenderScheduler(Func<int> limit) => this.limit = limit;
        static TaskCompletionSource<bool> NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal async Task<T> RunAsync<T>(Func<T> work, CancellationToken token) {
            while (true) {
                token.ThrowIfCancellationRequested();
                Task wait;
                lock (sync) {
                    if (active < Math.Max(1, limit())) { active++; break; }
                    wait = changed.Task;
                }
                await wait.WaitAsync(token).ConfigureAwait(false);
            }
            try {
                token.ThrowIfCancellationRequested();
                return await Task.Run(work, token).ConfigureAwait(false);
            } finally {
                lock (sync) {
                    active--;
                    var signal = changed;
                    changed = NewSignal();
                    signal.TrySetResult(true);
                }
            }
        }
    }
}

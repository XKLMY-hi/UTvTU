using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Classic;
using Xunit;

namespace OpenUtau.Test.Classic {
    [Collection("VstShared")]
    public class ClassicRenderSchedulerTests {
        [Fact]
        public async Task OverlappingGenerationsUseOneGlobalBudget() {
            ThreadPool.GetMinThreads(out int workers, out int io);
            ThreadPool.SetMinThreads(Math.Max(workers, 4), io);
            try {
            var scheduler = new ClassicRenderScheduler(() => 2);
            int active = 0, peak = 0;
            using var release = new ManualResetEventSlim();
            using var started = new CountdownEvent(2);
            // The observer must not occupy a pool worker while the simulated native
            // jobs occupy theirs (the guarded runner can have only two processors).
            var observed = Task.Factory.StartNew(() => {
                try {
                    bool ready = started.Wait(2000);
                    // Spare pool capacity makes a missing budget observable; this is
                    // not merely asserting the runner's two-processor pool size.
                    Thread.Sleep(100);
                    return (Ready: ready, Active: Volatile.Read(ref active));
                }
                finally { release.Set(); }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            var tasks = Enumerable.Range(0, 20).Select(_ => scheduler.RunAsync(() => {
                int count = Interlocked.Increment(ref active);
                InterlockedExtensions.Max(ref peak, count);
                if (started.CurrentCount > 0) started.Signal();
                release.Wait();
                Interlocked.Decrement(ref active);
                return true;
            }, CancellationToken.None)).ToArray();
            var result = await observed.WaitAsync(TimeSpan.FromSeconds(3));
            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(result.Ready);
            Assert.Equal(2, result.Active);
            Assert.Equal(2, peak);
            } finally { ThreadPool.SetMinThreads(workers, io); }
        }

        [Fact]
        public async Task CancelledQueuedWorkDoesNotRunOrConsumeTheNextSlot() {
            var scheduler = new ClassicRenderScheduler(() => 1);
            using var release = new ManualResetEventSlim();
            using var started = new ManualResetEventSlim();
            var first = scheduler.RunAsync(() => { started.Set(); release.Wait(); return 1; }, CancellationToken.None);
            try {
                Assert.True(started.Wait(2000));
                using var cancellation = new CancellationTokenSource();
                bool executed = false;
                var queued = scheduler.RunAsync(() => { executed = true; return 2; }, cancellation.Token);
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
                Assert.False(executed);
            } finally { release.Set(); }
            Assert.Equal(1, await first);
            Assert.Equal(3, await scheduler.RunAsync(() => 3, CancellationToken.None));
        }

        static class InterlockedExtensions {
            internal static void Max(ref int location, int value) {
                int observed;
                do { observed = Volatile.Read(ref location); if (observed >= value) return; }
                while (Interlocked.CompareExchange(ref location, value, observed) != observed);
            }
        }
    }
}

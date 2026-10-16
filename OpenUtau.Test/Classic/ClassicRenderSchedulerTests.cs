using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Classic;
using Xunit;

namespace OpenUtau.Test.Classic {
    public class ClassicRenderSchedulerTests {
        [Fact]
        public async Task OverlappingGenerationsUseOneGlobalBudget() {
            var scheduler = new ClassicRenderScheduler(() => 2);
            int active = 0, peak = 0;
            using var release = new ManualResetEventSlim();
            using var started = new CountdownEvent(2);
            var tasks = Enumerable.Range(0, 20).Select(_ => scheduler.RunAsync(() => {
                int count = Interlocked.Increment(ref active);
                InterlockedExtensions.Max(ref peak, count);
                if (started.CurrentCount > 0) started.Signal();
                release.Wait();
                Interlocked.Decrement(ref active);
                return true;
            }, CancellationToken.None)).ToArray();
            try { Assert.True(started.Wait(2000)); Assert.Equal(2, Volatile.Read(ref active)); }
            finally { release.Set(); }
            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(2, peak);
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

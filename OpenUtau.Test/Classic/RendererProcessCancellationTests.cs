using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.Util;
using Serilog;
using Xunit;

namespace OpenUtau.Test.Classic {
    public class RendererProcessCancellationTests {
        [Fact]
        public async Task CancellingAnExternalRendererStopsItBeforeItsTimeout() {
            if (!OperatingSystem.IsWindows()) return;
            using var cancellation = new CancellationTokenSource();
            var elapsed = Stopwatch.StartNew();
            var work = Task.Run(() => ProcessRunner.Run(
                Path.Combine(Environment.SystemDirectory, "ping.exe"), "-n 30 127.0.0.1", Log.Logger,
                timeoutMs: 5000, cancellation: cancellation.Token, background: true));
            cancellation.CancelAfter(150);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(3));
        }

        [Fact]
        public void TimedOutRendererReportsFailureInsteadOfPublishingPartialOutput() {
            if (!OperatingSystem.IsWindows()) return;
            Assert.Throws<TimeoutException>(() => ProcessRunner.Run(
                Path.Combine(Environment.SystemDirectory, "ping.exe"), "-n 30 127.0.0.1", Log.Logger, timeoutMs: 100));
        }
    }
}

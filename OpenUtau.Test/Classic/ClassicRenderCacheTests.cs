using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using OpenUtau.Classic;
using Xunit;

namespace OpenUtau.Test.Classic {
    public class ClassicRenderCacheTests {
        static string NewPath() => Path.Combine(Path.GetTempPath(), "utvtu-cache-" + Guid.NewGuid().ToString("N") + ".wav");
        static void WriteWave(string path, float[] samples) {
            using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(44100, 1));
            writer.WriteSamples(samples, 0, samples.Length);
        }

        [Fact]
        public async Task AnotherGenerationWaitsForTheWriterInsteadOfReadingItsPartialHeader() {
            string path = NewPath();
            using var started = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var expected = new float[] { .1f, -.2f, .3f };
            int renders = 0;
            var first = Task.Run(() => ClassicRenderCache.GetOrRender(path, CancellationToken.None, () => {
                Interlocked.Increment(ref renders);
                File.WriteAllBytes(path, new byte[] { 1, 2 });
                started.Set(); release.Wait();
                WriteWave(path, expected);
                return expected;
            }));
            Task<float[]> second = null;
            try {
                Assert.True(started.Wait(2000));
                second = Task.Run(() => ClassicRenderCache.GetOrRender(path, CancellationToken.None, () => {
                    Interlocked.Increment(ref renders);
                    return Array.Empty<float>();
                }));
                release.Set();
                Assert.Equal(expected, await first.WaitAsync(TimeSpan.FromSeconds(3)));
                Assert.Equal(expected, await second.WaitAsync(TimeSpan.FromSeconds(3)));
                Assert.Equal(1, renders);
            } finally {
                release.Set();
                await first;
                if (second != null) await second;
                File.Delete(path);
            }
        }

        [Fact]
        public void CancelledWriterRemovesItsIncompleteCache() {
            string path = NewPath();
            using var cancellation = new CancellationTokenSource();
            try {
                Assert.ThrowsAny<OperationCanceledException>(() => ClassicRenderCache.GetOrRender(path, cancellation.Token, () => {
                    File.WriteAllBytes(path, new byte[] { 1, 2 });
                    cancellation.Cancel();
                    return new float[] { 1 };
                }));
                Assert.False(File.Exists(path));
            } finally { File.Delete(path); }
        }

        [Fact]
        public void ExistingIncompleteCacheIsRebuilt() {
            string path = NewPath();
            var expected = new float[] { .25f };
            try {
                File.WriteAllBytes(path, new byte[] { 1, 2 });
                Assert.Equal(expected, ClassicRenderCache.GetOrRender(path, CancellationToken.None, () => {
                    WriteWave(path, expected);
                    return expected;
                }));
                Assert.Equal(expected, ClassicRenderCache.GetOrRender(path, CancellationToken.None,
                    () => throw new InvalidOperationException("Completed cache must be reused")));
            } finally { File.Delete(path); }
        }
    }
}

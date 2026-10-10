using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using OpenUtau.Core.Export;
using OpenUtau.Core.Render;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Ustx;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.Render {
    [Collection("WorkspaceSession")]
    public class ExportOwnershipTests : IDisposable {
        readonly string directory = Path.Combine(Path.GetTempPath(), "utvtu-export-" + Guid.NewGuid().ToString("N"));
        public ExportOwnershipTests() => Directory.CreateDirectory(directory);
        string PathName => Path.Combine(directory, "result.wav");

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CancellationBeforeOrDuringWritingPreservesThePreviousDelivery(bool before) {
            File.WriteAllBytes(PathName, new byte[] { 1, 2, 3, 4 });
            using var cancellation = new CancellationTokenSource();
            var source = new TinySource(() => cancellation.Cancel());
            if (before) cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() =>
                ExportSession.WriteWaveFile(PathName, source, cancellation.Token));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(PathName));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            Assert.Equal(before ? 0 : 1, source.Calls);
        }

        [Fact]
        public void FailedAudioReadPreservesDelivery_AndSuccessfulWritePublishesCompletePcm() {
            File.WriteAllBytes(PathName, new byte[] { 9, 8, 7 });
            Assert.Throws<InvalidOperationException>(() => ExportSession.WriteWaveFile(PathName,
                new TinySource(() => throw new InvalidOperationException("Failed source")), default));
            Assert.Equal(new byte[] { 9, 8, 7 }, File.ReadAllBytes(PathName));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            ExportSession.WriteWaveFile(PathName, new TinySource(), default);
            using var reader = new WaveFileReader(PathName);
            Assert.Equal(16, reader.WaveFormat.BitsPerSample);
            Assert.Equal(WaveFormatEncoding.Pcm, reader.WaveFormat.Encoding);
            Assert.Equal(64, reader.Length); // 32 interleaved samples, 2 bytes each.
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }

        [Fact]
        public void ExportCannotReplaceThePlaybackMeter_OrLoseAnEarlyCancellation() {
            using var dispatcher = DocManagerTestSetup.EnterScopedDispatcher(installScheduler: false);
            var live = new LevelTracker(new TinySource()) { Peak = .5f };
            TrackLevels.Register(0, live);
            CancellationTokenSource slot = null!;
            try {
                var project = new UProject(); // No voice notes, wave samples, effects or plugins.
                RenderEngine.RenderMixdown(project, TaskScheduler.Default, ref slot, wait: true, applyMixFx: false);
                Assert.InRange(TrackLevels.ReadAndReset(0), -6.03f, -6.01f);
                using var cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                Assert.Throws<OperationCanceledException>(() => RenderEngine.RenderMixdown(
                    project, TaskScheduler.Default, ref slot, wait: true, cancellationToken: cancelled.Token));
            } finally {
                RenderCancellationLease.Retire(ref slot);
                TrackLevels.Clear();
            }
        }

        sealed class TinySource(Action? onRead = null) : ISignalSource {
            public int Calls;
            public bool IsReady(int position, int count) => true;
            public int Mix(int position, float[] buffer, int index, int count) {
                ++Calls;
                onRead?.Invoke();
                var samples = Math.Min(count, Math.Max(0, 32 - position));
                for (int i = 0; i < samples; ++i) buffer[index + i] += .1f;
                return position + samples;
            }
        }

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}

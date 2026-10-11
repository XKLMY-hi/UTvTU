using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using OpenUtau.Core;
using OpenUtau.Core.Export;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.Render {
    [Collection("WorkspaceSession")]
    public sealed class ConfiguredExportTests : IDisposable {
        readonly string directory = Path.Combine(Path.GetTempPath(), "utvtu-configured-export-" + Guid.NewGuid().ToString("N"));
        readonly string previousDataPath = PathManager.Inst.DataPath;
        public ConfiguredExportTests() {
            Directory.CreateDirectory(directory);
            typeof(PathManager).GetProperty(nameof(PathManager.DataPath))!.SetValue(PathManager.Inst, directory);
        }
        string Destination => Path.Combine(directory, "delivery.wav");
        UProject Project() {
            var project = new UProject();
            var part = new UWavePart { trackNo = 0, channels = 2, sampleRate = 44100 };
            typeof(UWavePart).GetProperty(nameof(UWavePart.Samples))!.SetValue(part, Enumerable.Repeat(.25f, 8820).ToArray());
            typeof(UWavePart).GetField("duration", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(part, 96);
            project.parts.Add(part);
            return project;
        }
        [Theory]
        [InlineData(ExportWaveFormat.Pcm16, 44100, 16)]
        [InlineData(ExportWaveFormat.Pcm24, 48000, 24)]
        [InlineData(ExportWaveFormat.Float32, 96000, 32)]
        public async Task RealDeliveryHonorsBitDepthRateRangeAndSilenceTail(ExportWaveFormat format, int rate, int bits) {
            using var dispatcher = DocManagerTestSetup.EnterScopedDispatcher();
            var project = Project();
            var audio = new ExportAudioOptions { Format = format, SampleRate = rate, StartTick = 24, EndTick = 72,
                TailSeconds = .025, VstEffects = false, BuiltInEffects = false, TrackVolumeAndPan = false };
            var plan = new ExportDeliveryPlan(project, Destination, false, audio, project.tracks);
            await new ConfiguredExportSession(plan).RunAsync(new Reports());
            using var reader = new WaveFileReader(Destination);
            Assert.Equal(rate, reader.WaveFormat.SampleRate);
            Assert.Equal(bits, reader.WaveFormat.BitsPerSample);
            Assert.Equal(format == ExportWaveFormat.Float32 ? WaveFormatEncoding.IeeeFloat : WaveFormatEncoding.Pcm, reader.WaveFormat.Encoding);
            Assert.InRange(reader.TotalTime.TotalSeconds, .074, .076);
            var provider = reader.ToSampleProvider();
            var buffer = new float[rate];
            int read = provider.Read(buffer, 0, buffer.Length);
            Assert.InRange(buffer[read / 4], .24f, .26f);
            Assert.InRange(Math.Abs(buffer[read - 20]), 0, .002f);
            Assert.DoesNotContain(Directory.GetFiles(directory), f => Path.GetFileName(f).StartsWith(".utvtu-render-"));
        }
        [Fact]
        public async Task SelectedStemsHaveUniqueNamesAndMasterIsExcluded() {
            using var dispatcher = DocManagerTestSetup.EnterScopedDispatcher();
            var project = Project();
            project.tracks[0].TrackName = "Lead";
            project.tracks.Add(new UTrack("Lead") { TrackNo = 1 });
            var clone = project.parts[0] as UWavePart;
            var second = new UWavePart { trackNo = 1, channels = 2, sampleRate = 44100 };
            typeof(UWavePart).GetProperty(nameof(UWavePart.Samples))!.SetValue(second, clone!.Samples);
            typeof(UWavePart).GetField("duration", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(second, 96);
            project.parts.Add(second);
            var plan = new ExportDeliveryPlan(project, Destination, true, new ExportAudioOptions { VstEffects = false,
                BuiltInEffects = false, TrackVolumeAndPan = false, MasterGain = 0 }, project.tracks);
            Assert.Equal(1, plan.Audio.MasterGain);
            Assert.NotEqual(plan.Targets[0].Path, plan.Targets[1].Path);
            await new ConfiguredExportSession(plan).RunAsync(new Reports());
            foreach (var target in plan.Targets) {
                using var reader = new WaveFileReader(target.Path);
                var sample = new float[2];
                reader.ToSampleProvider().Read(sample, 0, 2);
                Assert.InRange(sample[0], .249f, .251f);
            }
            Assert.False(File.Exists(Destination));
        }
        [Fact]
        public async Task TrackGainAndMasterGainAreAppliedOnlyWhenRequested() {
            using var dispatcher = DocManagerTestSetup.EnterScopedDispatcher();
            var project = Project();
            project.tracks[0].Volume = -6;
            var options = new ExportAudioOptions { Format = ExportWaveFormat.Float32, VstEffects = false,
                BuiltInEffects = false, TrackVolumeAndPan = true, MasterGain = .5 };
            var plan = new ExportDeliveryPlan(project, Destination, false, options, project.tracks);
            await new ConfiguredExportSession(plan).RunAsync(new Reports());
            using var reader = new WaveFileReader(Destination);
            var sample = new float[2];
            reader.ToSampleProvider().Read(sample, 0, 2);
            Assert.InRange(sample[0], .0442f, .0445f); // Existing equal-power center pan is sqrt(0.5).
        }
        [Fact]
        public async Task CancellationPreservesExistingFileAndChangedTrackCannotRedirectDelivery() {
            using var dispatcher = DocManagerTestSetup.EnterScopedDispatcher();
            var project = Project();
            var plan = new ExportDeliveryPlan(project, Destination, false, new ExportAudioOptions { VstEffects = false }, project.tracks);
            File.WriteAllBytes(Destination, new byte[] { 9, 8, 7 });
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ConfiguredExportSession(plan).RunAsync(new Reports(), cancelled.Token));
            project.tracks[0] = new UTrack("Replacement");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ConfiguredExportSession(plan).RunAsync(new Reports()));
            Assert.Equal(new byte[] { 9, 8, 7 }, File.ReadAllBytes(Destination));
        }
        [Fact]
        public void SourceBoundaryPadsTimelineWithoutFeedingLaterAudioIntoTail() {
            var inner = new ConstantSource();
            var source = new ExportRangeProvider(new ExportInputBoundary(inner, 4), 0, 8d / 88200, 1, default);
            var samples = new float[8];
            Assert.Equal(8, source.Read(samples, 0, 8));
            Assert.Equal(new float[] { .5f, .5f, .5f, .5f, 0, 0, 0, 0 }, samples);
            Assert.Equal(4, inner.Count);
            Assert.Equal(0, source.Read(samples, 0, 8));
        }
        [Fact]
        public void PcmSaturatesAndFloatPreservesHeadroom() {
            foreach (var format in new[] { ExportWaveFormat.Pcm16, ExportWaveFormat.Pcm24, ExportWaveFormat.Float32 }) {
                var source = new ExportRangeProvider(new ConstantSource(2), 0, 4d / 88200, 1, default);
                ExportWaveWriter.Write(Destination, source, new ExportAudioOptions { Format = format }, default);
                using var reader = new WaveFileReader(Destination);
                var sample = new float[2];
                reader.ToSampleProvider().Read(sample, 0, 2);
                Assert.InRange(sample[0], format == ExportWaveFormat.Float32 ? 1.999f : .999f, format == ExportWaveFormat.Float32 ? 2.001f : 1f);
            }
        }
        sealed class Reports : IProgress<ExportDeliveryProgress> { public void Report(ExportDeliveryProgress value) { Assert.InRange(value.Fraction, 0, 1); } }
        sealed class ConstantSource(float value = .5f) : ISignalSource {
            public int Count;
            public bool IsReady(int position, int count) => true;
            public int Mix(int position, float[] buffer, int index, int count) { Count += count; for (int i = 0; i < count; i++) buffer[index + i] += value; return position + count; }
        }
        public void Dispose() {
            typeof(PathManager).GetProperty(nameof(PathManager.DataPath))!.SetValue(PathManager.Inst, previousDataPath);
            Directory.Delete(directory, recursive: true);
        }
    }
}

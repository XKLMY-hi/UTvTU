using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using OpenUtau.Core;
using OpenUtau.Core.Export;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtau.Core.Vst;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.Render {
    [Collection("WorkspaceSession")]
    public sealed class OfflineVstExportTests : IDisposable {
        const string Uid = "test:offline-delivery";
        readonly string directory = Path.Combine(Path.GetTempPath(), "utvtu-offline-" + Guid.NewGuid().ToString("N"));
        readonly string previousDataPath = PathManager.Inst.DataPath;
        readonly IVstBridge previousBridge = VstPluginManager.Inst.Bridge;
        readonly ExportBridge bridge = new();
        string Destination => Path.Combine(directory, "export.wav");

        public OfflineVstExportTests() {
            Directory.CreateDirectory(directory);
            typeof(PathManager).GetProperty(nameof(PathManager.DataPath))!.SetValue(PathManager.Inst, directory);
            VstTestSetup.Register(VstTestSetup.MakeEntry(Uid));
            VstPluginManager.Inst.Bridge = bridge;
        }
        UProject Project(int tracks = 1, bool impulse = false) {
            var project = new UProject();
            for (int i = 0; i < tracks; i++) {
                if (i > 0) project.tracks.Add(new UTrack("Second") { TrackNo = i });
                project.tracks[i].VstSlots.Add(new VstPluginSlot(0) { PluginUid = Uid, StateData = new[] { (byte)(i == 0 ? 5 : 11) } });
                var samples = Enumerable.Repeat(impulse ? 0f : .25f, 8820).ToArray();
                if (impulse) { samples[0] = samples[1] = 1; samples[^2] = samples[^1] = 1; }
                var part = new UWavePart { trackNo = i, channels = 2, sampleRate = 44100 };
                typeof(UWavePart).GetProperty(nameof(UWavePart.Samples))!.SetValue(part, samples);
                typeof(UWavePart).GetField("duration", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(part, 96);
                project.parts.Add(part);
            }
            return project;
        }
        ExportDeliveryPlan Plan(UProject project, int start = 0, int end = 96) => new(project, Destination, false,
            new ExportAudioOptions { Format = ExportWaveFormat.Float32, StartTick = start, EndTick = end,
                BuiltInEffects = false, VstEffects = true, TrackVolumeAndPan = false }, project.tracks);

        [Fact]
        public async Task OfflineDeliveryCapturesLiveParametersUsesIndependentInstancesAndLeavesDeviceUntouched() {
            using var dispatcher = DocManagerTestSetup.EnterScopedDispatcher();
            var project = Project();
            var live = VstPluginManager.Inst.LoadEffect(0, project.tracks[0].VstSlots[0])!;
            bridge.EditorGain = 2;
            var device = PlaybackManager.Inst.AudioOutput;
            await new ConfiguredExportSession(Plan(project, 24, 72)).RunAsync(new Reports(_ => bridge.EditorGain = 4));
            using var reader = new WaveFileReader(Destination);
            var samples = new float[2];
            reader.ToSampleProvider().Read(samples, 0, 2);
            Assert.Equal(.5f, samples[0]);
            Assert.All(bridge.Modes, mode => Assert.True(mode));
            Assert.Same(device, PlaybackManager.Inst.AudioOutput);
            Assert.Same(live, VstPluginManager.Inst.GetEffect(0, 0));
            Assert.True(live.IsLoaded);
            Assert.DoesNotContain(live.GetBridgeHandle(), bridge.Unloaded);
            Assert.Single(bridge.Unloaded); // Only the job's independent instance.
            Assert.Equal(0, bridge.Contexts[0].SamplePosition); // Pre-roll before the selected range.
            Assert.Contains(bridge.Contexts, c => c.SamplePosition >= 1102 && c.Tempo == 120 && c.Numerator == 4);
        }

        [Fact]
        public async Task DifferentPluginLatenciesAlignTracksAndKeepTheFinalDelayedSamples() {
            using var dispatcher = DocManagerTestSetup.EnterScopedDispatcher();
            await new ConfiguredExportSession(Plan(Project(2, impulse: true))).RunAsync(new Reports());
            using var reader = new WaveFileReader(Destination);
            var samples = new float[8820];
            Assert.Equal(samples.Length, reader.ToSampleProvider().Read(samples, 0, samples.Length));
            Assert.Equal(2f, samples[0]);
            Assert.Equal(2f, samples[^2]);
            Assert.All(samples.Skip(2).Take(samples.Length - 4), s => Assert.Equal(0, s));
            Assert.Equal(2, bridge.Unloaded.Count);
        }

        [Theory]
        [InlineData("setup")]
        [InlineData("restore")]
        [InlineData("process")]
        public async Task PluginFailureDoesNotPublishDryAudioOrOverwriteThePreviousDelivery(string failure) {
            using var dispatcher = DocManagerTestSetup.EnterScopedDispatcher();
            bridge.Failure = failure;
            File.WriteAllBytes(Destination, new byte[] { 1, 9, 3 });
            await Assert.ThrowsAsync<InvalidOperationException>(() => new ConfiguredExportSession(Plan(Project())).RunAsync(new Reports()));
            Assert.Equal(new byte[] { 1, 9, 3 }, File.ReadAllBytes(Destination));
            Assert.Single(bridge.Unloaded);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }

        [Fact]
        public async Task CancelDuringPrerollRetiresOwnedPluginsAndPreservesDelivery() {
            using var dispatcher = DocManagerTestSetup.EnterScopedDispatcher();
            using var cancelled = new CancellationTokenSource();
            bridge.OnProcess = () => cancelled.Cancel();
            File.WriteAllBytes(Destination, new byte[] { 7, 3 });
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ConfiguredExportSession(Plan(Project(), 72, 96)).RunAsync(new Reports(), cancelled.Token));
            Assert.Equal(new byte[] { 7, 3 }, File.ReadAllBytes(Destination));
            Assert.Single(bridge.Unloaded);
        }

        [Fact]
        public void RealtimeCompatibilityConfiguresIndependentPluginsWithoutChangingTheRequestedMode() {
            var project = Project();
            var plan = Plan(project);
            using var owned = new ExportVstSession(ExportVstSession.Capture(plan), plan.Audio with { ProcessingMode = ExportProcessingMode.Realtime },
                project.timeAxis.Clone(), project.resolution, bridge, default);
            Assert.Single(owned.Effects[0]);
            Assert.Equal(new[] { false }, bridge.Modes);
        }

        [Fact]
        public async Task DirectMixdownMenuAlsoUsesOfflineWithoutAnAudioDevice() {
            using var dispatcher = DocManagerTestSetup.EnterScopedDispatcher();
            var device = PlaybackManager.Inst.AudioOutput;
            await PlaybackManager.Inst.RenderMixdown(Project(), Destination);
            using var reader = new WaveFileReader(Destination);
            Assert.Equal(WaveFormatEncoding.IeeeFloat, reader.WaveFormat.Encoding);
            Assert.Equal(new[] { true }, bridge.Modes);
            Assert.Same(device, PlaybackManager.Inst.AudioOutput);
            Assert.Single(bridge.Unloaded);
        }

        [Fact]
        public async Task ContextUsesCapturedTempoMapAndFractionalMusicalPositions() {
            using var dispatcher = DocManagerTestSetup.EnterScopedDispatcher();
            var project = Project();
            project.tempos.Add(new UTempo(48, 60));
            project.timeAxis.BuildSegments(project);
            var plan = Plan(project);
            await new ConfiguredExportSession(plan).RunAsync(new Reports(_ => {
                project.tempos[0].bpm = 90;
                project.timeAxis.BuildSegments(project);
            }));
            Assert.Contains(bridge.Contexts, c => c.SamplePosition > 2205 && c.Tempo == 60);
            Assert.Contains(bridge.Contexts, c => c.Tempo == 120);
            Assert.Contains(bridge.Contexts, c => Math.Abs(c.QuarterNotes * project.resolution - Math.Round(c.QuarterNotes * project.resolution)) > .01);
        }

        [Fact]
        public async Task LatencyChangeFailsInsteadOfPublishingMisalignedTracks() {
            using var dispatcher = DocManagerTestSetup.EnterScopedDispatcher();
            bridge.ChangeLatency = true;
            File.WriteAllBytes(Destination, new byte[] { 4, 7 });
            await Assert.ThrowsAsync<InvalidOperationException>(() => new ConfiguredExportSession(Plan(Project())).RunAsync(new Reports()));
            Assert.Equal(new byte[] { 4, 7 }, File.ReadAllBytes(Destination));
            Assert.Single(bridge.Unloaded);
        }

        sealed class Reports(Action<ExportDeliveryProgress>? callback = null) : IProgress<ExportDeliveryProgress> {
            public void Report(ExportDeliveryProgress value) => callback?.Invoke(value);
        }

        sealed class ExportBridge : FakeVstBridge, IVstBridge, IVstExportBridge {
            public float EditorGain = 1;
            public string Failure = "";
            public Action? OnProcess;
            public bool ChangeLatency;
            public readonly List<bool> Modes = new();
            public readonly List<VstExportContext> Contexts = new();
            readonly Dictionary<IntPtr, int> latencies = new();
            readonly Dictionary<IntPtr, float> gains = new();
            readonly Dictionary<IntPtr, Queue<float>> delays = new();
            bool IVstBridge.RestoreState(IntPtr handle, byte[] data) { latencies[handle] = data[0]; return Failure != "restore"; }
            public bool SetupExport(IntPtr handle, double rate, int block, bool offline) {
                Modes.Add(offline);
                delays[handle] = new Queue<float>(Enumerable.Repeat(0f, GetLatencySamples(handle) * 2));
                return Failure != "setup";
            }
            public int GetLatencySamples(IntPtr handle) => latencies.GetValueOrDefault(handle);
            public VstExportParameter[] CaptureExportParameters(IntPtr handle) => new[] { new VstExportParameter(12345, EditorGain) };
            public bool RestoreExportParameters(IntPtr handle, VstExportParameter[] parameters) {
                gains[handle] = parameters.Length > 0 ? parameters[0].Value : 1;
                return true;
            }
            public bool ProcessExport(IntPtr handle, float[] buffer, int frames, VstExportContext context) {
                Contexts.Add(context);
                var queue = delays[handle];
                for (int i = 0; i < frames * 2; i++) {
                    queue.Enqueue(buffer[i] * gains[handle]);
                    buffer[i] = queue.Dequeue();
                }
                if (frames > 0) {
                    OnProcess?.Invoke();
                    if (ChangeLatency) latencies[handle]++;
                }
                return frames == 0 || Failure != "process";
            }
        }
        public void Dispose() {
            VstPluginManager.Inst.RemoveTrack(0);
            VstPluginManager.Inst.RemoveTrack(1);
            VstPluginManager.Inst.TryFlushAllPendingDispose();
            VstPluginManager.Inst.Bridge = previousBridge;
            typeof(PathManager).GetProperty(nameof(PathManager.DataPath))!.SetValue(PathManager.Inst, previousDataPath);
            Directory.Delete(directory, true);
        }
    }
}

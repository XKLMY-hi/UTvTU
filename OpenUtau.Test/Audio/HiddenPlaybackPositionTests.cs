using System;
using System.Collections.Generic;
using System.Reflection;
using NAudio.Wave;
using OpenUtau.Audio;
using OpenUtau.Core;
using OpenUtau.Core.SignalChain;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.Audio {
    [Collection("VstShared")]
    public class HiddenPlaybackPositionTests {
        sealed class Output : IAudioOutput {
            public PlaybackState PlaybackState => PlaybackState.Playing;
            public int DeviceNumber => 0;
            // Match NAudioOutput.GetPosition: the device byte count is already divided by channels.
            public long Position = 44100 * sizeof(float);
            public long GetPosition() => Position;
            public void SelectDevice(Guid guid, int deviceNumber) { }
            public void Init(ISampleProvider sampleProvider) { }
            public void Pause() { }
            public void Play() { }
            public void Stop() { }
            public List<AudioOutputDevice> GetOutputDevices() => new();
        }

        sealed class BusyOutput : IAudioOutput {
            public bool ForbidSnapshot;
            public PlaybackState PlaybackState => throw new InvalidOperationException("Blocking getter was polled");
            public int DeviceNumber => 0;
            public long GetPosition() => throw new InvalidOperationException("Blocking getter was polled");
            public bool TryGetPlaybackPosition(out PlaybackState state, out long position) {
                if (ForbidSnapshot) throw new InvalidOperationException("Starting playback must not poll stopped output");
                state = PlaybackState.Stopped; position = 0; return false;
            }
            public void SelectDevice(Guid guid, int deviceNumber) { }
            public void Init(ISampleProvider sampleProvider) { }
            public void Pause() { }
            public void Play() { }
            public void Stop() { }
            public List<AudioOutputDevice> GetOutputDevices() => new();
        }

        [Fact]
        public void StartingPlaybackDoesNotPollStoppedDeviceAndRestartItsPendingLoop() {
            var playback = PlaybackManager.Inst;
            var previousOutput = playback.AudioOutput;
            var starting = typeof(PlaybackManager).GetField("<StartingToPlay>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var playing = typeof(PlaybackManager).GetField("<PlayingMaster>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var previousStarting = starting.GetValue(playback);
            var previousPlaying = playing.GetValue(playback);
            try {
                playback.AudioOutput = new BusyOutput { ForbidSnapshot = true };
                playing.SetValue(playback, true);
                starting.SetValue(playback, true);
                playback.UpdatePlayPos();
            } finally {
                starting.SetValue(playback, previousStarting);
                playing.SetValue(playback, previousPlaying);
                playback.AudioOutput = previousOutput;
            }
        }

        [Fact]
        public void BusyDeviceDoesNotEnterBlockingStateOrPositionGetters() {
            var playback = PlaybackManager.Inst;
            var previousOutput = playback.AudioOutput;
            var playing = typeof(PlaybackManager).GetField("<PlayingMaster>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var previousPlaying = playing.GetValue(playback);
            try {
                playback.AudioOutput = new BusyOutput();
                playing.SetValue(playback, true);
                playback.UpdatePlayPos();
            } finally {
                playing.SetValue(playback, previousPlaying);
                playback.AudioOutput = previousOutput;
            }
        }

        [Fact]
        public void HiddenTickSuppressesProjectionAndRestoredTickUsesCurrentDeviceTime() {
            using var dispatcher = DocManagerTestSetup.EnterScopedDispatcher();
            var playback = PlaybackManager.Inst;
            var doc = DocManager.Inst;
            var output = playback.AudioOutput;
            int previousTick = doc.playPosTick;
            var type = typeof(PlaybackManager);
            var names = new[] { "masterMix", "startMs", "loopEndTick", "<PlayingMaster>k__BackingField" };
            var fields = Array.ConvertAll(names, name => type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!);
            var values = Array.ConvertAll(fields, field => field.GetValue(playback));
            try {
                var fake = new Output();
                playback.AudioOutput = fake;
                fields[0].SetValue(playback, new MasterAdapter(new WaveSource(0, 5000, 0, 2)));
                fields[1].SetValue(playback, 0d);
                fields[2].SetValue(playback, 0);
                fields[3].SetValue(playback, true);
                doc.playPosTick = -1;
                playback.UpdatePlayPos(publish: false);
                Assert.Equal(-1, doc.playPosTick);
                fake.Position *= 2;
                playback.UpdatePlayPos();
                Assert.Equal(doc.Project.timeAxis.MsPosToTickPos(2000), doc.playPosTick);
            } finally {
                for (int i = 0; i < fields.Length; i++) fields[i].SetValue(playback, values[i]);
                playback.AudioOutput = output;
                doc.playPosTick = previousTick;
            }
        }
    }
}

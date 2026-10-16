using System;
using System.Collections.Generic;
using NAudio.Wave;

namespace OpenUtau.Audio {
    public class AudioOutputDevice {
        public string name;
        public string api;
        public int deviceNumber;
        public Guid guid;

        public override string ToString() => $"[{api}] {name}";
    }

    public interface IAudioOutput {
        PlaybackState PlaybackState { get; }
        int DeviceNumber { get; }

        void SelectDevice(Guid guid, int deviceNumber);
        void Init(ISampleProvider sampleProvider);
        void Pause();
        void Play();
        void Stop();
        long GetPosition();

        /// <summary>UI polling may skip a tick while the device is being stopped or replaced.</summary>
        bool TryGetPlaybackPosition(out PlaybackState state, out long position) {
            state = PlaybackState;
            position = state == PlaybackState.Playing ? GetPosition() : 0;
            return true;
        }

        List<AudioOutputDevice> GetOutputDevices();
    }
}

using System;
using OpenUtau.Core.SignalChain;

namespace OpenUtau.Core.Export {
    // Stop feeding audio at the range end while letting downstream effects produce their tail.
    internal sealed class ExportInputBoundary(ISignalSource source, int end) : ISignalSource {
        public int SampleRate => source.SampleRate;
        public int Channels => source.Channels;
        public bool IsReady(int position, int count) => position >= end || source.IsReady(position, Math.Min(count, end - position));
        public int Mix(int position, float[] buffer, int index, int count) {
            int active = Math.Clamp(end - position, 0, count);
            if (active > 0) source.Mix(position, buffer, index, active);
            return position + count;
        }
    }
}

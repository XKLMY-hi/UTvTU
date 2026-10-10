using System;
using NAudio.Wave;
using System.Threading;

namespace OpenUtau.Core.SignalChain {
    public class ExportAdapter : ISampleProvider {
        private readonly WaveFormat waveFormat;
        private readonly ISignalSource source;
        private int position;
        readonly CancellationToken cancellation;

        public WaveFormat WaveFormat => waveFormat;

        public ExportAdapter(ISignalSource source, CancellationToken cancellation = default) {
            this.source = source;
            this.cancellation = cancellation;
            waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.SampleRate, source.Channels);
        }

        public int Read(float[] buffer, int offset, int count) {
            cancellation.ThrowIfCancellationRequested();
            for (int i = offset; i < offset + count; ++i) {
                buffer[i] = 0;
            }
            if (!source.IsReady(position, count)) {
                throw new Exception("All sources must be ready when exporting.");
            } else {
                int pos = source.Mix(position, buffer, offset, count);
                int n = Math.Max(0, pos - position);
                position = pos;
                return n;
            }
        }
    }
}

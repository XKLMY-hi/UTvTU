using System;
using System.Threading;
using NAudio.Wave;
using OpenUtau.Core.SignalChain;

namespace OpenUtau.Core.Export {
    // Fixed, frame-aligned delivery range. Silence keeps the timeline and effect tails intact.
    internal sealed class ExportRangeProvider : ISampleProvider {
        readonly ISignalSource source;
        readonly CancellationToken cancellation;
        readonly double gain;
        readonly long end;
        long position;
        public WaveFormat WaveFormat { get; }
        public long TotalSamples { get; }
        public ExportRangeProvider(ISignalSource source, double startSeconds, double durationSeconds,
            double gain, CancellationToken cancellation) {
            this.source = source;
            this.cancellation = cancellation;
            this.gain = gain;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.SampleRate, source.Channels);
            position = checked((long)Math.Round(startSeconds * source.SampleRate)) * source.Channels;
            TotalSamples = checked((long)Math.Round(durationSeconds * source.SampleRate)) * source.Channels;
            end = checked(position + TotalSamples);
            if (position < 0 || TotalSamples <= 0 || end > int.MaxValue) throw new ArgumentException("Export range is too long.");
        }
        public int Read(float[] buffer, int offset, int count) {
            cancellation.ThrowIfCancellationRequested();
            int read = (int)Math.Min(count - count % WaveFormat.Channels, end - position);
            if (read <= 0) return 0;
            Array.Clear(buffer, offset, read);
            if (!source.IsReady((int)position, read)) throw new InvalidOperationException("Synthesis is not ready for export.");
            source.Mix((int)position, buffer, offset, read);
            for (int i = offset; i < offset + read; i++) buffer[i] = (float)(buffer[i] * gain);
            position += read;
            return read;
        }
    }
}

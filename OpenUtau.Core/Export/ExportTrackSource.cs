using System;
using System.Threading;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Vst;

namespace OpenUtau.Core.Export {
    // Pre-roll the independent chain and read ahead by its reported latency.
    // Each track then contributes samples at its musical position, including the last delayed samples.
    internal sealed class ExportTrackSource : ISignalSource {
        readonly ISignalSource source;
        readonly VstEffect[] effects;
        readonly CancellationToken token;
        readonly int latency;
        readonly float[] scratch = new float[AudioSettings.BlockSize * AudioSettings.Channels];
        int cursor;
        bool started;

        internal ExportTrackSource(ISignalSource source, VstEffect[] effects, CancellationToken token) {
            this.source = source;
            this.effects = effects;
            this.token = token;
            foreach (var effect in effects) latency = checked(latency + checked(effect.LatencySamples * AudioSettings.Channels));
            if (latency < 0) throw new InvalidOperationException("Invalid VST latency.");
        }
        public bool IsReady(int position, int count) => source.IsReady(checked(position + latency), count);
        public void Reset() { source.Reset(); cursor = 0; started = false; }
        void Read(int position, float[] buffer, int index, int count) {
            token.ThrowIfCancellationRequested();
            if (!source.IsReady(position, count)) throw new InvalidOperationException("Synthesis is not ready for export pre-roll.");
            foreach (var effect in effects) effect.SetExportPosition(position / AudioSettings.Channels);
            source.Mix(position, buffer, index, count);
        }
        public int Mix(int position, float[] buffer, int index, int count) {
            int inputPosition = checked(position + latency);
            if (!started) {
                while (cursor < inputPosition) {
                    int block = Math.Min(scratch.Length, inputPosition - cursor);
                    Array.Clear(scratch, 0, block);
                    Read(cursor, scratch, 0, block);
                    cursor += block;
                }
                started = true;
            }
            if (cursor != inputPosition) throw new InvalidOperationException("Export audio must be consumed sequentially.");
            Read(cursor, buffer, index, count);
            cursor = checked(cursor + count);
            return checked(position + count);
        }
    }
}

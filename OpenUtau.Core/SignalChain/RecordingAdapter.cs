using System;
using System.IO;
using NAudio.Wave;

namespace OpenUtau.Core.SignalChain {
    /// <summary>
    /// T-split recording adapter.  Wraps a source ISampleProvider and
    /// simultaneously (a) passes data through to the output device and
    /// (b) writes an identical copy to a WAV file on disk.
    ///
    /// When SilentOutput is true the audio goes ONLY to the file and
    /// silence is returned to the caller (speaker-safe recording).
    /// </summary>
    public class RecordingAdapter : ISampleProvider {
        private readonly ISampleProvider source;
        private readonly WaveFileWriter? writer;
        private long samplesWritten;
        public long SamplesWritten => System.Threading.Interlocked.Read(ref samplesWritten);

        /// <summary>If true, returns silence instead of real audio to the caller.</summary>
        public bool SilentOutput { get; set; }

        public WaveFormat WaveFormat => source.WaveFormat;

        /// <summary>
        /// Create a recording adapter.  pass writer=null for a dry run
        /// (silent output only, no file written).
        /// </summary>
        public RecordingAdapter(ISampleProvider source, WaveFileWriter? writer, bool silentOutput = true) {
            this.source = source;
            this.writer = writer;
            SilentOutput = silentOutput;
        }

        public int Read(float[] buffer, int offset, int count) {
            int read = source.Read(buffer, offset, count);

            // Write same data to file
            if (read > 0 && writer != null) {
                writer.WriteSamples(buffer, offset, read);
            }
            System.Threading.Interlocked.Add(ref samplesWritten, read);

            // Optionally blank out the output (silent playback)
            if (SilentOutput) {
                Array.Clear(buffer, offset, read);
            }

            return read;
        }
    }
}

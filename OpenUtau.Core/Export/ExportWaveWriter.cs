using System;
using System.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using OpenUtau.Core.Util;

namespace OpenUtau.Core.Export {
    internal static class ExportWaveWriter {
        internal static void Write(string path, ISampleProvider input, ExportAudioOptions options,
            CancellationToken cancellation, Action<double>? progress = null, double durationSeconds = 0) {
            options.Validate();
            cancellation.ThrowIfCancellationRequested();
            ISampleProvider source = input.WaveFormat.SampleRate == options.SampleRate ? input
                : new WdlResamplingSampleProvider(input, options.SampleRate);
            int channels = source.WaveFormat.Channels;
            var format = options.Format == ExportWaveFormat.Float32
                ? WaveFormat.CreateIeeeFloatWaveFormat(options.SampleRate, channels)
                : new WaveFormat(options.SampleRate, options.Format == ExportWaveFormat.Pcm24 ? 24 : 16, channels);
            AtomicFile.Write(path, temporary => {
                using var writer = new WaveFileWriter(temporary, format);
                var samples = new float[4096 * channels];
                var bytes = new byte[samples.Length * (format.BitsPerSample / 8)];
                long written = 0;
                int read;
                while ((read = source.Read(samples, 0, samples.Length)) > 0) {
                    cancellation.ThrowIfCancellationRequested();
                    if (options.Format == ExportWaveFormat.Float32) {
                        // Float WAV preserves headroom; PCM formats saturate at full scale.
                        for (int i = 0; i < read; i++) if (!float.IsFinite(samples[i])) samples[i] = 0;
                        writer.WriteSamples(samples, 0, read);
                    } else {
                        int stride = format.BitsPerSample / 8;
                        int scale = 1 << (format.BitsPerSample - 1);
                        for (int i = 0; i < read; i++) {
                            double value = float.IsFinite(samples[i]) ? samples[i] : 0;
                            int pcm = (int)Math.Clamp(Math.Round(value * scale), -scale, scale - 1);
                            for (int b = 0; b < stride; b++) bytes[i * stride + b] = (byte)(pcm >> (8 * b));
                        }
                        writer.Write(bytes, 0, read * stride);
                    }
                    written += read;
                    if (durationSeconds > 0) progress?.Invoke(Math.Min(1, written / (durationSeconds * options.SampleRate * channels)));
                }
                cancellation.ThrowIfCancellationRequested();
            });
        }
    }
}

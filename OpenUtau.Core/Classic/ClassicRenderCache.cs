using System;
using System.IO;
using System.Threading;
using NAudio.Wave;
using OpenUtau.Core.Format;
using OpenUtau.Core.Render;
using Serilog;

namespace OpenUtau.Classic {
    internal static class ClassicRenderCache {
        internal static float[] GetOrRender(string path, CancellationToken token, Func<float[]> render) {
            // An external wavtool writes its header and payload in several steps. All
            // generations must own this lock through rendering, reading and cancellation cleanup.
            lock (Renderers.GetCacheLock(path)) {
                token.ThrowIfCancellationRequested();
                if (File.Exists(path)) {
                    try {
                        using var stream = Wave.OpenFile(path);
                        return Wave.GetSamples(stream.ToSampleProvider().ToMono(1, 0));
                    } catch (Exception error) {
                        Log.Warning(error, "Rebuilding incomplete render cache {Path}", path);
                        File.Delete(path);
                    }
                }
                try {
                    var samples = render();
                    token.ThrowIfCancellationRequested();
                    return samples;
                } catch {
                    if (File.Exists(path)) File.Delete(path);
                    throw;
                }
            }
        }
    }
}

using System;
using System.IO;
using System.Text;
using Serilog;

namespace OpenUtau.Core.Util {
    /// <summary>Publish complete text in the destination directory, preserving the
    /// previous file if serialization, writing, flushing or replacement fails.</summary>
    internal static class AtomicFile {
        public static void WriteAllText(string path, string text) {
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None)) {
                    using (var writer = new StreamWriter(stream, Encoding.UTF8, 4096, leaveOpen: true)) {
                        writer.Write(text);
                        writer.Flush();
                    }
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporaryPath, path, overwrite: true);
            } finally {
                if (File.Exists(temporaryPath)) {
                    try { File.Delete(temporaryPath); }
                    catch (Exception ex) { Log.Warning(ex, "Failed to remove temporary file {Path}", temporaryPath); }
                }
            }
        }
    }
}

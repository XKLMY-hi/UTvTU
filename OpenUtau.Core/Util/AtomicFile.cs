using System;
using System.IO;
using System.Text;
using Serilog;

namespace OpenUtau.Core.Util {
    /// <summary>Publish a complete file in the destination directory, preserving the
    /// previous file if serialization, writing, flushing or replacement fails.</summary>
    internal static class AtomicFile {
        public static void WriteAllText(string path, string text) {
            Write(path, temporaryPath => {
                using var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var writer = new StreamWriter(stream, Encoding.UTF8);
                writer.Write(text);
            });
        }

        public static void Write(string path, Action<string> writeCompleteFile) {
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                writeCompleteFile(temporaryPath);
                using (var stream = new FileStream(temporaryPath, FileMode.Open,
                    FileAccess.Write, FileShare.None)) {
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

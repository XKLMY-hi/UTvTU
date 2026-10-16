using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Serilog;

namespace OpenUtau.Core.Util {
    public static class ProcessRunner {
        public static bool DebugSwitch { get; set; }
        // Keep the original CLR signature for already-compiled phonemizer/tool plugins.
        public static void Run(string file, string args, ILogger logger, string workDir, int timeoutMs) =>
            Run(file, args, logger, workDir, timeoutMs, CancellationToken.None, false);

        public static void Run(string file, string args, ILogger logger, string workDir = null, int timeoutMs = 60000,
            CancellationToken cancellation = default, bool background = false) {
            cancellation.ThrowIfCancellationRequested();
            if (!File.Exists(file)) {
                throw new FileNotFoundException($"Executable {file} not found.");
            }
            var threadId = Thread.CurrentThread.ManagedThreadId;
            using (var proc = new Process()) {
                proc.StartInfo = new ProcessStartInfo(file, args) {
                    Environment = {{"LANG", "ja_JP.utf8"}},
                    UseShellExecute = false,
                    RedirectStandardOutput = DebugSwitch,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = workDir,
                };
                if (DebugSwitch) {
                    proc.OutputDataReceived += (o, e) => {
                        if (!string.IsNullOrEmpty(e.Data)) {
                            logger.Information($"ProcessRunner >>> [thread-{threadId}] {e.Data}");
                        }
                    };
                }
                proc.ErrorDataReceived += (o, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) {
                        logger.Error($"ProcessRunner >>> [thread-{threadId}] {e.Data}");
                    }
                };
                proc.Start();
                if (background) {
                    try { proc.PriorityClass = ProcessPriorityClass.BelowNormal; }
                    catch (Exception e) { logger.Debug(e, "Could not lower renderer process priority"); }
                }
                if (DebugSwitch) {
                    proc.BeginOutputReadLine();
                }
                proc.BeginErrorReadLine();
                var elapsed = Stopwatch.StartNew();
                try {
                    while (!proc.WaitForExit(50)) {
                        cancellation.ThrowIfCancellationRequested();
                        if (timeoutMs > 0 && elapsed.ElapsedMilliseconds >= timeoutMs) {
                            throw new TimeoutException($"Renderer process timed out: {Path.GetFileName(file)}");
                        }
                    }
                    cancellation.ThrowIfCancellationRequested();
                    proc.WaitForExit(); // Drain redirected output before disposing the process.
                } catch {
                    try {
                        if (!proc.HasExited) proc.Kill(entireProcessTree: true);
                        proc.WaitForExit(5000);
                    } catch (Exception e) {
                        logger.Error(e, $"ProcessRunner >>> [thread-{threadId}] Failed to kill");
                    }
                    throw;
                }
            }
        }
    }
}

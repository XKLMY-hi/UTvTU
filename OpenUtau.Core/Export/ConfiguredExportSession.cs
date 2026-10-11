using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using OpenUtau.Audio;
using OpenUtau.Core.Render;
using OpenUtau.Core.Vst;

namespace OpenUtau.Core.Export {
    public sealed record ExportDeliveryProgress(double Fraction, string Phase, string File, int FileIndex, int FileCount);
    public sealed class ConfiguredExportSession {
        readonly ExportDeliveryPlan plan;
        public ConfiguredExportSession(ExportDeliveryPlan plan) => this.plan = plan;

        public async Task RunAsync(IProgress<ExportDeliveryProgress> progress, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            plan.ValidateTargets();
            // Capture live editor parameters once, before yielding from the start action.
            var snapshots = ExportVstSession.Capture(plan);
            var clock = plan.Project.timeAxis.Clone();
            int resolution = plan.Project.resolution;
            var bridge = VstPluginManager.Inst.Bridge;
            await Task.Run(async () => {
                int count = plan.PerTrack ? plan.Targets.Count : 1;
                for (int i = 0; i < count; i++) {
                    ct.ThrowIfCancellationRequested();
                    plan.ValidateTargets();
                    var target = plan.Targets[i];
                    var options = plan.PerTrack ? plan.Audio with { TrackIndices = Array.AsReadOnly(new[] { target.Index }) } : plan.Audio;
                    void Report(double fraction, string phase) => progress.Report(new ExportDeliveryProgress(
                        Math.Clamp((i + fraction) / count, 0, 1), phase, target.Path, i + 1, count));
                    Directory.CreateDirectory(Path.GetDirectoryName(target.Path)!);
                    Report(0, "preparing");
                    CancellationTokenSource cancellation = null;
                    string floatPath = Path.Combine(Path.GetDirectoryName(target.Path)!, ".utvtu-render-" + Guid.NewGuid().ToString("N") + ".wav");
                    try {
                        using var effects = new ExportVstSession(snapshots, options, clock, resolution, bridge, ct);
                        options = options with { OwnedVstEffects = effects.Effects, Cancellation = ct };
                        double duration = plan.DurationSeconds + options.TailSeconds;
                        if (options.ProcessingMode == ExportProcessingMode.Realtime && plan.NeedsDevice(options)) {
                            await PlaybackManager.Inst.RecordExport(plan.Project, floatPath, options, plan.StartSeconds, duration,
                                new InlineProgress<double>(p => Report(p * .9, "recording")), ct).ConfigureAwait(false);
                            ct.ThrowIfCancellationRequested();
                            plan.ValidateTargets();
                            using var input = new WaveFileReader(floatPath);
                            ExportWaveWriter.Write(target.Path, input.ToSampleProvider(), options, ct,
                                p => Report(.9 + .1 * p, "writing"), duration);
                        } else {
                            using var gate = RenderGate.Enter();
                            var mix = RenderEngine.RenderExport(plan.Project, DocManager.Inst.MainScheduler, ref cancellation, options, ct);
                            plan.ValidateTargets();
                            Report(.2, "writing");
                            var source = new ExportRangeProvider(mix, plan.StartSeconds, duration, options.MasterGain, ct);
                            ExportWaveWriter.Write(target.Path, source, options, ct, p => Report(.2 + .8 * p, "writing"), duration);
                        }
                        Report(1, "done");
                    } finally {
                        RenderCancellationLease.Retire(ref cancellation);
                        try { if (File.Exists(floatPath)) File.Delete(floatPath); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                            Serilog.Log.Warning(ex, "Could not remove export intermediate {Path}", floatPath);
                        }
                        VstPluginManager.Inst.TryFlushAllPendingDispose();
                    }
                }
            }, ct).ConfigureAwait(false);
        }

        sealed class InlineProgress<T>(Action<T> report) : IProgress<T> {
            public void Report(T value) => report(value);
        }
    }
}

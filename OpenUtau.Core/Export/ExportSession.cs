using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using OpenUtau.Audio;
using OpenUtau.Core.Render;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtau.Core.Vst;

namespace OpenUtau.Core.Export {
    /// <summary>
    /// 统一导出会话：整曲混音 / 分轨。三入口（菜单整曲、菜单分轨、RenderWindow）
    /// 共用同一渲染与写文件路径，进度经 IProgress&lt;ProgressInfo&gt; 转发。
    /// 语义（D 阶段统一后）：全部立体声 16-bit、不含主推子增益/静音、
    /// 分轨跳过静音轨、消费段计入 RenderGate（防并发 Flush）。
    /// </summary>
    public sealed class ExportSession {
        public sealed class Options {
            /// <summary>false = 整曲 mixdown；true = 逐轨导出。</summary>
            public bool PerTrack = false;
            /// <summary>
            /// 整曲导出是否含效果链（分轨恒为干轨）。true = MixFxMode.Snapshot：
            /// 建链时克隆一次 UMixFx，导出结果确定、不随导出中的参数改动漂移。
            /// </summary>
            public bool ApplyMixFx = true;
            /// <summary>分轨导出的轨道过滤（null = 全部轨道）。</summary>
            public System.Collections.Generic.IReadOnlyCollection<int>? TrackFilter;
            public int? StartTick;
            public int? EndTick;
        }

        public sealed class ProgressInfo {
            public int TrackIndex = -1;
            public int TrackCount;
            public string CurrentFile = "";
            /// <summary>0..1。</summary>
            public double Percent;
        }

        readonly UProject project;
        readonly string basePath;
        readonly Options options;

        public ExportSession(UProject project, string basePath, Options options) {
            this.project = project;
            this.basePath = basePath;
            this.options = options;
        }

        public Task RunAsync(IProgress<ProgressInfo> progress, CancellationToken ct = default) {
            return Task.Run(() => {
                // 注意：RenderEngine 的 ref CTS 会 Interlocked.Exchange 替换——不能是 using 变量。
                // 不能用 CreateLinkedTokenSource：RenderMixdown Exchange 时 Cancel 旧 CTS 会
                // **反向传播**取消用户的 token（写文件循环的 ct 检查直接 break → 文件残缺）。
                var cts = new CancellationTokenSource();
                ct.Register(() => cts.Cancel());
                int startTick = options.StartTick ?? 0;
                int endTick = options.EndTick ?? -1;
                try {
                    if (!options.PerTrack) {
                        progress.Report(new ProgressInfo { Percent = 0.2, CurrentFile = basePath });
                        var mix = RenderEngine.RenderMixdown(
                            project, DocManager.Inst.MainScheduler, ref cts,
                            wait: true, applyMixFx: options.ApplyMixFx,
                            startTick: startTick, endTick: endTick).Item1;

                        progress.Report(new ProgressInfo { Percent = 0.7, CurrentFile = basePath });
                        using (RenderGate.Enter()) {
                            CheckFileWritable(basePath);
                            WaveFileWriter.CreateWaveFile16(basePath, new ExportAdapter(mix));
                        }
                        progress.Report(new ProgressInfo { Percent = 1.0, CurrentFile = basePath });
                    } else {
                        var trackMixes = RenderEngine.RenderTracks(
                            project, DocManager.Inst.MainScheduler, ref cts,
                            startTick: startTick, endTick: endTick);
                        int total = project.tracks.Count;
                        using (RenderGate.Enter()) {
                            for (int i = 0; i < trackMixes.Count; ++i) {
                                if (ct.IsCancellationRequested) break;
                                if (trackMixes[i] == null || i >= total || project.tracks[i].Muted) continue;
                                if (options.TrackFilter != null && !options.TrackFilter.Contains(i)) continue;

                                string file = PathManager.Inst.GetExportPath(basePath, project.tracks[i]);
                                progress.Report(new ProgressInfo {
                                    TrackIndex = i, TrackCount = total,
                                    CurrentFile = file, Percent = 0.2 + 0.6 * i / Math.Max(1, total),
                                });
                                CheckFileWritable(file);
                                WaveFileWriter.CreateWaveFile16(file, new ExportAdapter(trackMixes[i]));
                            }
                        }
                        progress.Report(new ProgressInfo { TrackIndex = -1, TrackCount = total, Percent = 1.0 });
                    }
                } catch {
                    throw; // 错误处理由调用方（PlaybackManager / RenderWindow）负责
                } finally {
                    cts.Dispose();
                }
            }, ct);
        }

        private static void CheckFileWritable(string filePath) {
            if (!File.Exists(filePath)) return;
            using (FileStream fp = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) {
                return;
            }
        }
    }
}

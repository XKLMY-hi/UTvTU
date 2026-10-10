using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using OpenUtau.App.Controls;
using OpenUtau.Core;
using OpenUtau.Core.Export;
using OpenUtau.Core.Render;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.App.Views {
    public partial class RenderWindow : WindowEx {
        private readonly RenderWindowJobOwner jobs;
        private readonly Core.Ustx.UProject project;
        private bool closed;
        private int browseVersion;
        private readonly List<(CheckBox cb, Core.Ustx.UTrack track)> _trackChecks = new();

        /// <summary>
        /// 渲染进度/收尾的 UI 编组。判据用 `UiThreadAffinity`（锚定**本窗口构造时所属线程**）
        /// 而非 `CheckAccess()`（headless 会误判）或 `Dispatcher.UIThread.Invoke`（同步等待）。
        /// 后台进度和最终状态由 RenderWindowJobOwner 投递，排队前与执行时都校验所属作业。
        /// 完成、失败、取消与按钮恢复在同一个最终回调发布，旧作业不能覆盖新作业状态。
        /// </summary>
        private readonly UiThreadAffinity ui = new UiThreadAffinity();

        public RenderWindow() {
            InitializeComponent();
            jobs = new RenderWindowJobOwner(ui.Post);
            project = DocManager.Inst.Project;

            // Range radio — show/hide custom box
            RadioFullSong.IsCheckedChanged += (_, _) => CustomRangeBox.IsVisible = false;
            RadioLoop.IsCheckedChanged += (_, _) => CustomRangeBox.IsVisible = false;
            RadioCustom.IsCheckedChanged += (_, _) => CustomRangeBox.IsVisible = RadioCustom.IsChecked == true;

            // Default output path
            var proj = project;
            if (!string.IsNullOrEmpty(proj.FilePath)) {
                var dir = Path.GetDirectoryName(proj.FilePath) ?? "";
                var name = Path.GetFileNameWithoutExtension(proj.FilePath);
                OutputPathBox.Text = Path.Combine(dir, $"{name}.wav");
            }

            BuildTrackList();
        }

        // ═══════════════════════════════════════════════════════════════
        //  Track checkboxes
        // ═══════════════════════════════════════════════════════════════

        void BuildTrackList() {
            TrackCheckPanel.Children.Clear();
            _trackChecks.Clear();
            foreach (var t in project.tracks) {
                var row = new StackPanel {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 2)
                };
                var cb = new CheckBox {
                    IsChecked = true,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var label = new TextBlock {
                    Text = t.TrackName,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 0, 0),
                };
                row.Children.Add(cb);
                row.Children.Add(label);
                TrackCheckPanel.Children.Add(row);
                _trackChecks.Add((cb, t));
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  Browse output path
        // ═══════════════════════════════════════════════════════════════

        public async void OnBrowsePath(object? sender, RoutedEventArgs args) {
            if (closed) return;
            int version = ++browseVersion;
            var file = await FilePicker.SaveFile(this, "menu.file.exportmixdown", FilePicker.WAV);
            if (!closed && version == browseVersion && ReferenceEquals(project, DocManager.Inst.Project) && !string.IsNullOrEmpty(file))
                OutputPathBox.Text = file;
        }

        // ═══════════════════════════════════════════════════════════════
        //  Start render
        // ═══════════════════════════════════════════════════════════════

        public void OnStartRender(object? sender, RoutedEventArgs args) {
            if (closed) return;
            if (!ReferenceEquals(project, DocManager.Inst.Project)) {
                ProgressLabel.Text = ThemeManager.GetString("render.status.cancelled");
                ProgressSubLabel.IsVisible = false;
                return;
            }
            string path = OutputPathBox.Text ?? "";
            if (string.IsNullOrWhiteSpace(path)) {
                var proj = project;
                var dir = Path.GetDirectoryName(proj.FilePath)
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var name = Path.GetFileNameWithoutExtension(proj.FilePath) ?? "Untitled";
                path = Path.Combine(dir, $"{name}.wav");
                OutputPathBox.Text = path;
            }

            bool isMixdown = RadioMixdown.IsChecked == true;
            bool applyMixFx = (ChkVst.IsChecked == true) || (ChkBuiltinFx.IsChecked == true);

            // 渲染范围 → tick（此前范围单选是死 UI——恒整曲）
            int rangeStart = 0, rangeEnd = -1;
            if (RadioLoop.IsChecked == true) {
                rangeStart = DocManager.Inst.rangeStartTick;
                rangeEnd = DocManager.Inst.rangeEndTick;
            } else if (RadioCustom.IsChecked == true
                       && ParseRange(CustomRangeBox.Text, out double rStartMs, out double rEndMs)) {
                rangeStart = project.timeAxis.MsPosToTickPos(rStartMs);
                rangeEnd = project.timeAxis.MsPosToTickPos(rEndMs);
            }
            var selTrackNos = _trackChecks.Where(x => x.cb.IsChecked == true)
                .Where(x => project.tracks.Contains(x.track))
                .Select(x => x.track.TrackNo).ToHashSet();

            var job = jobs.TryBegin(project.tracks.Select(t => t.TrackName));
            if (job == null) return;
            browseVersion++; // A previously opened picker cannot rename the running job's destination.
            StartBtn.IsEnabled = false;
            ProgressLabel.Text = ThemeManager.GetString("render.status.preparing");
            ProgressSubLabel.IsVisible = true;
            ProgressSubLabel.Text = string.Empty;
            ProgressBarControl.Value = 0;

            Task.Run(async () => {
                Exception? error = null;
                try {
                    job.Token.ThrowIfCancellationRequested();
                    if (isMixdown) {
                        // ── 录制式混音导出：设备播放驱动，与预览完全同路径 ──
                        //（同一信号链/VST 激活时序——导出的就是预览听到的）
                        await PlaybackManager.Inst.RecordMixdown(project, path, rangeStart, rangeEnd,
                            new RenderWindowJobOwner.CallbackProgress<double>(p => jobs.PostProgress(job, () => {
                                if (!double.IsFinite(p)) return;
                                ProgressLabel.Text = p < 0.4
                                    ? ThemeManager.GetString("render.status.mixdown")
                                    : ThemeManager.GetString("render.status.writing");
                                ProgressBarControl.Value = Math.Clamp(p, 0, 1) * 100;
                            })), job.Token);
                    } else {
                        var session = new ExportSession(project, path,
                            new ExportSession.Options {
                                PerTrack = true,
                                ApplyMixFx = false,
                                TrackFilter = selTrackNos,
                                StartTick = rangeStart,
                                EndTick = rangeEnd,
                            });

                        await session.RunAsync(new RenderWindowJobOwner.CallbackProgress<ExportSession.ProgressInfo>(info => {
                            // Capture the report before queuing, including a safe immutable track label.
                            int index = info.TrackIndex, count = info.TrackCount;
                            double percent = info.Percent;
                            string? name = job.TrackName(index);
                            jobs.PostProgress(job, () => {
                                if (!double.IsFinite(percent)) return;
                                if (name != null) {
                                    ProgressLabel.Text = string.Format(ThemeManager.GetString("render.status.exporting"), index + 1, count, name);
                                    ProgressBarControl.Value = Math.Clamp(20 + (60d * index / Math.Max(1, count)), 0, 100);
                                } else {
                                    ProgressLabel.Text = percent < 0.4
                                        ? ThemeManager.GetString("render.status.mixdown")
                                        : ThemeManager.GetString("render.status.writing");
                                    ProgressBarControl.Value = Math.Clamp(percent, 0, 1) * 100;
                                }
                            });
                        }), job.Token);
                    }
                } catch (OperationCanceledException ex) {
                    error = ex;
                } catch (Exception ex) {
                    error = ex;
                    if (!job.Token.IsCancellationRequested) Log.Error(ex, "[RenderWindow] Render failed");
                } finally {
                    jobs.Finish(job, error, outcome => {
                        StartBtn.IsEnabled = true;
                        ProgressLabel.Text = ThemeManager.GetString(outcome switch {
                            RenderWindowJobOwner.Outcome.Completed => "render.status.done",
                            RenderWindowJobOwner.Outcome.Cancelled => "render.status.cancelled",
                            _ => "render.status.failed",
                        });
                        ProgressSubLabel.IsVisible = outcome == RenderWindowJobOwner.Outcome.Failed;
                        ProgressSubLabel.Text = outcome == RenderWindowJobOwner.Outcome.Failed ? error?.Message : string.Empty;
                        if (outcome == RenderWindowJobOwner.Outcome.Completed) ProgressBarControl.Value = 100;
                    });
                }
            }); // Always enter finally, including cancellation before the worker starts.
        }

        // ═══════════════════════════════════════════════════════════════
        //  Helpers
        // ═══════════════════════════════════════════════════════════════

        /// <summary>解析自定义范围 "0:00 ~ 3:45"（分:秒 或 时:分:秒）。</summary>
        static bool ParseRange(string? text, out double startMs, out double endMs) {
            startMs = 0; endMs = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var parts = text.Split('~');
            if (parts.Length != 2) return false;
            return TryParseTime(parts[0], out startMs) && TryParseTime(parts[1], out endMs) && endMs > startMs;
        }

        static bool TryParseTime(string s, out double ms) {
            ms = 0;
            var parts = s.Trim().Split(':');
            if (parts.Length == 2 && int.TryParse(parts[0], out int m) && double.TryParse(parts[1], out double sec)) {
                ms = (m * 60 + sec) * 1000;
                return true;
            }
            if (parts.Length == 3 && int.TryParse(parts[0], out int h)
                && int.TryParse(parts[1], out int mm) && double.TryParse(parts[2], out double ss)) {
                ms = ((h * 60 + mm) * 60 + ss) * 1000;
                return true;
            }
            return false;
        }

        public void OnOpenFolder(object? sender, RoutedEventArgs args) {
            string path = OutputPathBox.Text ?? "";
            if (!string.IsNullOrEmpty(path)) {
                try {
                    string? dir = Path.GetDirectoryName(path);
                    if (dir != null && Directory.Exists(dir))
                        OS.OpenFolder(dir);
                } catch { }
            }
        }

        protected override void OnClosed(EventArgs e) {
            closed = true;
            browseVersion++;
            jobs.Close();
            base.OnClosed(e);
        }
    }
}

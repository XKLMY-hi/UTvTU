using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using OpenUtau.App.Controls;
using OpenUtau.Core;
using OpenUtau.Core.Export;
using OpenUtau.Core.Render;
using OpenUtau.Core.SignalChain;
using NAudio.Wave;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.App.Views {
    public partial class RenderWindow : WindowEx {
        private CancellationTokenSource? _cts;
        private readonly List<(CheckBox cb, Core.Ustx.UTrack track)> _trackChecks = new();

        /// <summary>
        /// 渲染进度/收尾的 UI 编组。判据用 `UiThreadAffinity`（锚定**本窗口构造时所属线程**）
        /// 而非 `CheckAccess()`（headless 会误判）或 `Dispatcher.UIThread.Invoke`（同步等待）。
        /// 这四处全是**从后台渲染任务发起的单向 UI 更新**（进度文案/进度条、失败文案、
        /// finally 里重新启用开始按钮），没有任何返回值要取回、也没有后续逻辑依赖它们已完成
        /// ⇒ 一律 `Post` 即可；且同一 dispatcher 队列 FIFO 保证"先失败文案、后启用按钮"的顺序。
        /// </summary>
        private readonly UiThreadAffinity ui = new UiThreadAffinity();

        public RenderWindow() {
            InitializeComponent();

            // Range radio — show/hide custom box
            RadioFullSong.IsCheckedChanged += (_, _) => CustomRangeBox.IsVisible = false;
            RadioLoop.IsCheckedChanged += (_, _) => CustomRangeBox.IsVisible = false;
            RadioCustom.IsCheckedChanged += (_, _) => CustomRangeBox.IsVisible = RadioCustom.IsChecked == true;

            // Default output path
            var proj = DocManager.Inst.Project;
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
            foreach (var t in DocManager.Inst.Project.tracks) {
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
            var file = await FilePicker.SaveFile(this, "menu.file.exportmixdown", FilePicker.WAV);
            if (!string.IsNullOrEmpty(file))
                OutputPathBox.Text = file;
        }

        // ═══════════════════════════════════════════════════════════════
        //  Start render
        // ═══════════════════════════════════════════════════════════════

        public void OnStartRender(object? sender, RoutedEventArgs args) {
            string path = OutputPathBox.Text ?? "";
            if (string.IsNullOrWhiteSpace(path)) {
                var proj = DocManager.Inst.Project;
                var dir = Path.GetDirectoryName(proj.FilePath)
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var name = Path.GetFileNameWithoutExtension(proj.FilePath) ?? "Untitled";
                path = Path.Combine(dir, $"{name}.wav");
                OutputPathBox.Text = path;
            }

            StartBtn.IsEnabled = false;
            ProgressLabel.Text = ThemeManager.GetString("render.status.preparing");
            ProgressSubLabel.IsVisible = true;
            ProgressBarControl.Value = 0;

            bool isMixdown = RadioMixdown.IsChecked == true;
            bool applyMixFx = (ChkVst.IsChecked == true) || (ChkBuiltinFx.IsChecked == true);
            var project = DocManager.Inst.Project;

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
                .Select(x => x.track.TrackNo).ToHashSet();

            _cts = new CancellationTokenSource();
            var ctx = _cts;

            Task.Run(async () => {
                try {
                    if (isMixdown) {
                        // ── 录制式混音导出：设备播放驱动，与预览完全同路径 ──
                        //（同一信号链/VST 激活时序——导出的就是预览听到的）
                        await PlaybackManager.Inst.RecordMixdown(project, path, rangeStart, rangeEnd,
                            new Progress<double>(p => ui.Post(() => {
                                if (p >= 1) {
                                    ProgressLabel.Text = ThemeManager.GetString("render.status.done");
                                    ProgressSubLabel.IsVisible = false;
                                    ProgressBarControl.Value = 100;
                                } else {
                                    ProgressLabel.Text = p < 0.4
                                        ? ThemeManager.GetString("render.status.mixdown")
                                        : ThemeManager.GetString("render.status.writing");
                                    ProgressBarControl.Value = p * 100;
                                }
                            })), ctx.Token);
                    } else {
                        var session = new ExportSession(project, path,
                            new ExportSession.Options {
                                PerTrack = true,
                                ApplyMixFx = false,
                                TrackFilter = selTrackNos,
                                StartTick = rangeStart,
                                EndTick = rangeEnd,
                            });

                        session.RunAsync(new Progress<ExportSession.ProgressInfo>(info => {
                            ui.Post(() => {
                                if (info.Percent >= 1) {
                                    ProgressLabel.Text = ThemeManager.GetString("render.status.done");
                                    ProgressSubLabel.IsVisible = false;
                                    ProgressBarControl.Value = 100;
                                } else if (info.TrackIndex >= 0) {
                                    var track = project.tracks[info.TrackIndex];
                                    ProgressLabel.Text = string.Format(ThemeManager.GetString("render.status.exporting"), info.TrackIndex + 1, info.TrackCount, track.TrackName);
                                    ProgressBarControl.Value = 20 + (60 * info.TrackIndex / Math.Max(1, info.TrackCount));
                                } else {
                                    ProgressLabel.Text = info.Percent < 0.4
                                        ? ThemeManager.GetString("render.status.mixdown")
                                        : ThemeManager.GetString("render.status.writing");
                                    ProgressBarControl.Value = info.Percent * 100;
                                }
                            });
                        }), ctx.Token).GetAwaiter().GetResult();
                    }
                } catch (Exception ex) {
                    Log.Error(ex, "[RenderWindow] Render failed");
                    ui.Post(() => {
                        ProgressLabel.Text = ThemeManager.GetString("render.status.failed");
                        ProgressSubLabel.Text = ex.Message;
                    });
                } finally {
                    ui.Post(() => StartBtn.IsEnabled = true);
                }
            }, _cts.Token);
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
            base.OnClosed(e);
            try { _cts?.Cancel(); } catch { }
        }
    }
}

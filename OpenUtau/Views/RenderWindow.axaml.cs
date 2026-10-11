using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using OpenUtau.App.Controls;
using OpenUtau.Core;
using OpenUtau.Core.Export;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.App.Views {
    public partial class RenderWindow : WindowEx, ICmdSubscriber {
        readonly UiThreadAffinity ui = new();
        readonly RenderWindowJobOwner jobs;
        readonly UProject project;
        readonly List<(CheckBox Check, UTrack Track)> tracks = new();
        bool ready, refreshing, rendering, closed, showingMixdown = true;
        int browseVersion;
        string? deliveredDirectory;

        public RenderWindow() {
            InitializeComponent();
            jobs = new RenderWindowJobOwner(ui.Post);
            project = DocManager.Inst.Project;
            string directory = string.IsNullOrWhiteSpace(project.FilePath) ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : Path.GetDirectoryName(project.FilePath)!;
            string name = string.IsNullOrWhiteSpace(project.FilePath) ? "Untitled" : Path.GetFileNameWithoutExtension(project.FilePath);
            OutputPathBox.Text = Path.Combine(directory, name + ".wav");
            CustomStartSecondsBox.Text = "0";
            CustomEndSecondsBox.Text = (project.timeAxis.TickPosToMsPos(project.EndTick) / 1000).ToString("0.###", CultureInfo.CurrentCulture);
            TailSecondsBox.Text = "0";
            SyncTracks();
            DocManager.Inst.AddSubscriber(this);
            ready = true;
            RefreshOptions();
        }

        public void OnOptionsChanged(object? sender, RoutedEventArgs args) {
            if (ready && !refreshing && !rendering && !closed) RefreshOptions();
        }
        void SyncTracks() {
            if (!ReferenceEquals(project, DocManager.Inst.Project)) return;
            if (!tracks.Select(t => t.Track).SequenceEqual(project.tracks)) {
                var selected = tracks.ToDictionary(t => t.Track, t => t.Check.IsChecked == true);
                tracks.Clear();
                TrackCheckPanel.Children.Clear();
                foreach (var track in project.tracks) {
                    var check = new CheckBox { Content = track.TrackName, IsChecked = !track.Muted && (!selected.TryGetValue(track, out bool value) || value), MinHeight = 32 };
                    check.Classes.Add("renderCheck");
                    check.IsCheckedChanged += OnOptionsChanged;
                    ToolTip.SetTip(check, track.TrackName);
                    TrackCheckPanel.Children.Add(check);
                    tracks.Add((check, track));
                }
            }
            foreach (var (check, track) in tracks) {
                check.Content = track.TrackName;
                check.IsEnabled = !track.Muted;
                if (track.Muted) check.IsChecked = false;
            }
        }
        void RefreshOptions() {
            if (!ready || closed || refreshing) return;
            refreshing = true;
            try {
                if (!rendering) SyncTracks();
                bool mixdown = RadioMixdown.IsChecked == true;
                if (showingMixdown != mixdown) {
                    showingMixdown = mixdown;
                    ChkVst.IsChecked = ChkBuiltinFx.IsChecked = ChkVolume.IsChecked = ChkMaster.IsChecked = mixdown;
                }
                ChkMaster.IsEnabled = mixdown;
                MasterOptionsHost.Opacity = mixdown ? 1 : .45;
                CustomSecondsPanel.IsVisible = RadioCustom.IsChecked == true;
                InputPanel.IsEnabled = !rendering;
                CancelBtn.IsEnabled = rendering;
                ModeContractHint[!TextBlock.TextProperty] = new DynamicResourceExtension(mixdown ? "render.mode.mixhint" : "render.mode.stemhint");
                if (rendering) return;
                if (TryPlan(out var plan, out var error)) {
                    ValidationText.IsVisible = false;
                    StartBtn.IsEnabled = true;
                    string format = plan!.Audio.Format switch { ExportWaveFormat.Pcm16 => "WAV 16-bit", ExportWaveFormat.Pcm24 => "WAV 24-bit", _ => "WAV 32-bit float" };
                    OutputSummaryText.Text = string.Format(ThemeManager.GetString("render.summary.detail"), plan.PerTrack ? plan.Targets.Count : 1,
                        plan.Targets.Count, plan.StartSeconds.ToString("0.###"), (plan.StartSeconds + plan.DurationSeconds).ToString("0.###"),
                        plan.Audio.TailSeconds.ToString("0.###"), format, plan.Audio.SampleRate,
                        ThemeManager.GetString(plan.Audio.ProcessingMode == ExportProcessingMode.Realtime
                            ? "render.processing.realtime" : "render.processing.offline")) + Environment.NewLine +
                        string.Join(Environment.NewLine, (plan.PerTrack ? plan.Targets : plan.Targets.Take(1)).Select(t => t.Path));
                } else {
                    ValidationText.IsVisible = true;
                    ValidationText.Text = error;
                    StartBtn.IsEnabled = false;
                    OutputSummaryText.Text = ThemeManager.GetString("render.summary.placeholder");
                }
            } finally { refreshing = false; }
        }
        bool TryPlan(out ExportDeliveryPlan? plan, out string error) {
            plan = null;
            error = string.Empty;
            try {
                if (!ReferenceEquals(project, DocManager.Inst.Project)) { error = ThemeManager.GetString("render.validation.project"); return false; }
                int mode = RadioCustom.IsChecked == true ? 2 : RadioLoop.IsChecked == true ? 1 : 0;
                if (!RenderInputParser.TryRange(project, mode, CustomStartSecondsBox.Text, CustomEndSecondsBox.Text,
                    DocManager.Inst.rangeStartTick, DocManager.Inst.rangeEndTick, out int start, out int end)) {
                    error = ThemeManager.GetString("render.validation.range"); return false;
                }
                if (!RenderInputParser.TrySeconds(TailSecondsBox.Text, out double tail) || tail > 30) { error = ThemeManager.GetString("render.validation.tail"); return false; }
                if (string.IsNullOrWhiteSpace(OutputPathBox.Text)) { error = ThemeManager.GetString("render.validation.path"); return false; }
                var selected = tracks.Where(x => x.Check.IsChecked == true).Select(x => x.Track).ToArray();
                if (!selected.Any(t => !t.Muted && project.parts.Any(p => p.trackNo == project.tracks.IndexOf(t)))) { error = ThemeManager.GetString("render.validation.tracks"); return false; }
                string tag = (FormatCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Wav24";
                string rate = (SampleRateCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "44100";
                string processing = (ProcessingModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Offline";
                var audio = new ExportAudioOptions {
                    ProcessingMode = processing == "Realtime" ? ExportProcessingMode.Realtime : ExportProcessingMode.Offline,
                    Format = tag switch { "Wav16" => ExportWaveFormat.Pcm16, "Wav32" => ExportWaveFormat.Float32, _ => ExportWaveFormat.Pcm24 },
                    SampleRate = int.Parse(rate, CultureInfo.InvariantCulture), StartTick = start, EndTick = end, TailSeconds = tail,
                    VstEffects = ChkVst.IsChecked == true, BuiltInEffects = ChkBuiltinFx.IsChecked == true,
                    TrackVolumeAndPan = ChkVolume.IsChecked == true,
                    MasterGain = RadioMixdown.IsChecked == true && ChkMaster.IsChecked == true ? PlaybackManager.Inst.ExportMasterGain : 1,
                };
                plan = new ExportDeliveryPlan(project, OutputPathBox.Text!, RadioPerTrack.IsChecked == true, audio, selected);
                return true;
            } catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or OverflowException) {
                error = ThemeManager.GetString("render.validation.path") + " " + ex.Message;
                return false;
            }
        }
        public void OnSelectAllTracks(object? sender, RoutedEventArgs args) => SelectTracks(true);
        public void OnSelectNoTracks(object? sender, RoutedEventArgs args) => SelectTracks(false);
        void SelectTracks(bool value) {
            if (rendering || closed) return;
            refreshing = true;
            foreach (var item in tracks) item.Check.IsChecked = value;
            refreshing = false;
            RefreshOptions();
        }
        public async void OnBrowsePath(object? sender, RoutedEventArgs args) {
            if (closed || rendering) return;
            int version = ++browseVersion;
            var file = await FilePicker.SaveFile(this, "menu.file.exportmixdown", FilePicker.WAV);
            if (!closed && !rendering && version == browseVersion && ReferenceEquals(project, DocManager.Inst.Project) && !string.IsNullOrWhiteSpace(file)) OutputPathBox.Text = file;
        }
        public async void OnStartRender(object? sender, RoutedEventArgs args) {
            if (closed || rendering || !TryPlan(out var plan, out _)) { RefreshOptions(); return; }
            var job = jobs.TryBegin(project.tracks.Select(t => t.TrackName));
            if (job == null) return;
            rendering = true;
            browseVersion++;
            InputPanel.IsEnabled = false;
            StartBtn.IsEnabled = false;
            CancelBtn.IsEnabled = true;
            ValidationText.IsVisible = false;
            ProgressLabel.Text = ThemeManager.GetString("render.phase.preparing");
            ProgressSubLabel.IsVisible = true;
            ProgressSubLabel.Classes.Remove("renderError");
            ProgressSubLabel.Text = string.Empty;
            ProgressBarControl.Value = 0;
            ProgressPercentText.Text = "0%";
            Exception? failure = null;
            try {
                await new ConfiguredExportSession(plan!).RunAsync(new RenderWindowJobOwner.CallbackProgress<ExportDeliveryProgress>(p => jobs.PostProgress(job, () => {
                    ProgressLabel.Text = ThemeManager.GetString("render.phase." + p.Phase);
                    ProgressSubLabel.Text = $"{p.FileIndex}/{p.FileCount} · {Path.GetFileName(p.File)}";
                    ProgressBarControl.Value = p.Fraction * 100;
                    ProgressPercentText.Text = (p.Fraction * 100).ToString("0", CultureInfo.CurrentCulture) + "%";
                })), job.Token);
            } catch (OperationCanceledException ex) { failure = ex; }
            catch (Exception ex) { failure = ex; if (!job.Token.IsCancellationRequested) Log.Error(ex, "Render delivery failed"); }
            finally {
                jobs.Finish(job, failure, outcome => {
                    rendering = false;
                    CancelBtn.IsEnabled = false;
                    RefreshOptions();
                    ProgressLabel.Text = ThemeManager.GetString(outcome switch {
                        RenderWindowJobOwner.Outcome.Completed => "render.status.done",
                        RenderWindowJobOwner.Outcome.Cancelled => "render.status.cancelled",
                        _ => "render.status.failed",
                    });
                    ProgressSubLabel.IsVisible = true;
                    if (outcome == RenderWindowJobOwner.Outcome.Failed) ProgressSubLabel.Classes.Add("renderError");
                    ProgressSubLabel.Text = outcome == RenderWindowJobOwner.Outcome.Completed ? ThemeManager.GetString("render.delivery.complete")
                        : outcome == RenderWindowJobOwner.Outcome.Cancelled ? ThemeManager.GetString("render.delivery.cancelled") : failure?.Message;
                    if (outcome == RenderWindowJobOwner.Outcome.Completed) {
                        deliveredDirectory = Path.GetDirectoryName(plan!.Targets[0].Path);
                        ProgressBarControl.Value = 100;
                        ProgressPercentText.Text = "100%";
                    }
                });
            }
        }
        public void OnCancelRender(object? sender, RoutedEventArgs args) {
            if (closed || !rendering) return;
            CancelBtn.IsEnabled = false;
            ProgressLabel.Text = ThemeManager.GetString("render.status.cancelling");
            jobs.CancelCurrent();
        }
        public void OnOpenFolder(object? sender, RoutedEventArgs args) {
            try {
                string? directory = deliveredDirectory ?? Path.GetDirectoryName(OutputPathBox.Text);
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)) OS.OpenFolder(directory);
            } catch (Exception ex) { Log.Warning(ex, "Could not open export directory"); }
        }
        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is ProgressBarNotification) return;
            ui.Post(() => {
                if (closed) return;
                if (!ReferenceEquals(project, DocManager.Inst.Project)) jobs.CancelCurrent();
                if (!rendering) RefreshOptions();
            });
        }
        protected override void OnClosed(EventArgs e) {
            closed = true;
            browseVersion++;
            DocManager.Inst.RemoveSubscriber(this);
            jobs.Close();
            base.OnClosed(e);
        }
    }
}

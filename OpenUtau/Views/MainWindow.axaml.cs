using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.App.Commands;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Analysis;
using OpenUtau.Core.DiffSinger;
using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtau.Core.Theming;
using OpenUtau.Theming;
using ReactiveUI;
using Serilog;
using SharpCompress;
using Point = Avalonia.Point;

namespace OpenUtau.App.Views {
    public partial class MainWindow : WindowEx, ICmdSubscriber {
        private readonly KeyModifiers cmdKey =
            OS.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        private readonly MainWindowViewModel viewModel;

        /// <summary>命令层（W28/M09）：命令处理体统一经注册表作用在窗口上，需要视图模型入口。</summary>
        internal MainWindowViewModel ViewModel => viewModel;

        // 编排区平滑滚动/缩放（上游 1c43dc2b 的"轨道视图那半"；卷帘侧由 W15 落地同一套
        // Controls/SmoothViewport）。滚轮一次步进不直接跳到位，而是在 0.18s 内滑过去；
        // 滑动途中继续滚轮会从当前位置与当前速度续接 ⇒ 连续滚轮是一段连续运动。
        // 精密触控板的小数 delta 立即生效不滑动；Preferences.ReduceMotion 打开时全部即时到位。
        private readonly ValueGlide hScroll;
        private readonly ValueGlide vScroll;
        private readonly ZoomGlide xZoom;
        private readonly ValueGlide trackHeight;

        private PianoRollDetachedWindow? pianoRollWindow;
        private PianoRoll? pianoRoll;
        private MixerControl? mixerControl;
        private MixerWindow? mixerWindow;

        private PartEditState? partEditState;

        // Time range selection state
        private bool isSelectingRange;
        private Point rangeSelectStartPoint = default;
        private const double RangeSelectThreshold = 5; // pixels

        private readonly DispatcherTimer timer;
        private readonly DispatcherTimer autosaveTimer;
        private bool forceClose;

        private bool shouldOpenPartsContextMenu;

        private readonly ReactiveCommand<UPart, Unit> PartRenameCommand;
        private readonly ReactiveCommand<UPart, Unit> PartGotoFileCommand;
        private readonly ReactiveCommand<UPart, Unit> PartReplaceAudioCommand;
        private readonly ReactiveCommand<UPart, Unit> PartTranscribeCommand;
        private readonly ReactiveCommand<UPart, Unit> PartMergeCommand;
        private readonly ReactiveCommand<UPart, Unit> PartSplitCommand;

        // 阶段 E4：侧栏素材库（Avalonia 12 进程内自定义拖拽格式）
        static readonly DataFormat<USinger> SingerDragFormat = DataFormat.CreateInProcessFormat<USinger>("OpenUtau.Singer");
        static readonly DataFormat<string> AudioDragFormat = DataFormat.CreateInProcessFormat<string>("OpenUtau.Audio");
        private readonly SidebarViewModel sidebarViewModel;
        private Point sidebarDragStart;
        private PointerPressedEventArgs? sidebarPressedArgs;
        private SingerItem? sidebarDragSinger;
        private SampleItem? sidebarDragSample;

        public MainWindow() {
            Log.Information("Creating main window.");
            InitializeComponent();
            Log.Information("Initialized main window component.");
            DataContext = viewModel = new MainWindowViewModel {
                // give the viewmodel a way to prompt/save using the view's existing method
                AskIfSaveAndContinue = AskIfSaveAndContinue
            };
            // 阶段 E4：侧栏素材库 VM（歌手列表 + 伴奏库）
            sidebarViewModel = new SidebarViewModel();
            SingersPanel.DataContext = sidebarViewModel;
            SamplesPanel.DataContext = sidebarViewModel;

            // 编排区平滑滚动/缩放（上游 1c43dc2b）：与卷帘同一套时长（0.18s）与续接语义。
            // 轨道高按 TrackHeightDelta 逐格步进、格间滑动（上游口径）。
            var smoothViewport = new SmoothViewport(this);
            hScroll = smoothViewport.Scroll(HScrollBar);
            vScroll = smoothViewport.Scroll(VScrollBar);
            xZoom = smoothViewport.Zoom((position, delta) => viewModel.TracksViewModel.OnXZoomed(position, delta));
            trackHeight = smoothViewport.Value(
                () => viewModel.TracksViewModel.TrackHeight,
                height => viewModel.TracksViewModel.SetTrackHeight(height),
                () => ViewConstants.TrackHeightMin,
                () => ViewConstants.TrackHeightMax);

            // W11：首次运行把平台标准 VST3 目录播种进扫描路径（幂等 + 只此一次；
            // 用户之后删掉不会被复活）。素材库「效果器」页签与偏好设置 VST 页共用这份数据。
            Preferences.SeedStandardVstScanPathsOnce();

            viewModel.NewProject();
            viewModel.AddTempoChangeCmd = ReactiveCommand.Create<int>(tick => AddTempoChange(tick));
            viewModel.DelTempoChangeCmd = ReactiveCommand.Create<int>(tick => DelTempoChange(tick));
            viewModel.AddTimeSigChangeCmd = ReactiveCommand.Create<int>(bar => AddTimeSigChange(bar));
            viewModel.DelTimeSigChangeCmd = ReactiveCommand.Create<int>(bar => DelTimeSigChange(bar));

            timer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(15),
                DispatcherPriority.Normal,
                (sender, args) => {
                    PlaybackManager.Inst.UpdatePlayPos();
                    var pvm = viewModel.PlaybackViewModel;
                    pvm.RaisePropertyChanged(nameof(pvm.IsPlaying));
                    pvm.RaisePropertyChanged(nameof(pvm.ShowPlayPosHighlight));
                });
            timer.Start();

            autosaveTimer = new DispatcherTimer(
                TimeSpan.FromSeconds(30),
                DispatcherPriority.Normal,
                (sender, args) => DocManager.Inst.AutoSave());
            autosaveTimer.Start();

            PartRenameCommand = ReactiveCommand.Create<UPart>(part => RenamePart(part));
            PartGotoFileCommand = ReactiveCommand.Create<UPart>(part => GotoFile(part));
            PartReplaceAudioCommand = ReactiveCommand.Create<UPart>(part => ReplaceAudio(part));
            PartTranscribeCommand = ReactiveCommand.Create<UPart>(part => Transcribe(part));
            PartMergeCommand = ReactiveCommand.Create<UPart>(part => MergePart(part));
            PartSplitCommand = ReactiveCommand.Create<UPart>(async part =>  await SplitPart(part));

            AddHandler(DragDrop.DropEvent, OnDrop);

            // Global keyboard handler — catches keys even when child controls have focus
            AddHandler(KeyDownEvent, OnWindowKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);

            if (Preferences.Default.MainWindowSize.TryGetPosition(out int x, out int y)) {
                Position = new PixelPoint(x, y);
            }
            WindowState = (WindowState)Preferences.Default.MainWindowSize.State;

            DocManager.Inst.AddSubscriber(this);

            // 视图切换（S5）：三个视图同格叠放，切换只翻可见性 + 走一次淡入（不参与布局 ⇒ 无抖动）
            viewModel.ViewSwitcher.PropertyChanged += (_, args) => {
                if (args.PropertyName == nameof(ViewSwitcherState.CurrentView)) {
                    OnCurrentViewChanged(viewModel.ViewSwitcher.CurrentView);
                }
            };

            // 欢迎视图：命令行带工程文件则直接打开，否则以欢迎页作为初始视图
            var cmdArgs = Environment.GetCommandLineArgs();
            if (cmdArgs.Length == 2 && File.Exists(cmdArgs[1])) {
                string path = cmdArgs[1];
                Opened += (_, _) => viewModel.OpenProject(new[] { path });
            } else {
                ShowWelcome();
            }

            Log.Information("Main window checking Update.");
            UpdaterDialog.CheckForUpdate(
                dialog => ShowOverlayContent(dialog),
                () => (Application.Current?.ApplicationLifetime as IControlledApplicationLifetime)?.Shutdown(),
                TaskScheduler.FromCurrentSynchronizationContext());
            Log.Information("Created main window.");
            this.Cursor = null;
        }

        public void InitProject() {
            viewModel.InitProject();
        }

        // ── 欢迎视图（内嵌初始视图）────────────────────────────────
        // 说明：欢迎页原为独立 WelcomeWindow（阶段 E3）；本次回归主窗口，作为单窗口内的初始视图，
        // 打开/新建工程后隐藏。窗口级动作（偏好设置/包管理/链接）仍由本窗口承载。

        /// <summary>是否处于欢迎视图。</summary>
        public bool IsWelcomeVisible => WelcomeHost.IsVisible;

        /// <summary>声明式过渡时长（与 Styles/Md3Transitions.axaml 的 0.2s 对齐；减少动效时为 0）。</summary>
        private static int TransitionMs => Core.Util.Preferences.Default.ReduceMotion ? 0 : 200;

        /// <summary>切换过渡状态类（.md3-fade / .md3-pop 的 shown 态由样式层插值，不手写动画）。</summary>
        private static void SetShown(Control control, bool shown) => control.Classes.Set("shown", shown);

        private void ShowWelcome() {
            WelcomeHost.Host = this;
            WelcomeHost.DataContext = viewModel;
            viewModel.InitProject();          // 恢复状态（HasRecovery/RecoveryString）
            WelcomeHost.IsVisible = true;
            SetChromeForView(ViewSwitcherPolicy.ChromeFor(AppSurface.Welcome));
            SetShown(WelcomeHost, true);
        }

        /// <summary>欢迎视图退场后隐藏，并让编辑器入场（页面级过渡）。</summary>
        private async void HideWelcome() {
            if (!WelcomeHost.IsVisible) {
                return;
            }
            SetShown(WelcomeHost, false);
            await Task.Delay(TransitionMs);
            WelcomeHost.IsVisible = false;
            // 进编辑器 = 回工作台视图（A6 启动流程不变；欢迎页期间可能已被快捷键切过视图）
            viewModel.ViewSwitcher.SwitchToWorkspace();
            SetChromeForView(viewModel.ViewSwitcher.Chrome);
        }

        /// <summary>欢迎视图：新建工程。</summary>
        public void WelcomeNewProject() {
            HideWelcome();
            viewModel.NewProject();           // 已存在默认工程时为重置：直接进入编辑器
        }

        /// <summary>欢迎视图：打开工程（文件选择）。</summary>
        public async Task WelcomeOpenProject() {
            var files = await FilePicker.OpenFilesAboutProject(
                this, "menu.file.open",
                FilePicker.ProjectFiles,
                FilePicker.USTX,
                FilePicker.VSQX,
                FilePicker.UST,
                FilePicker.MIDI,
                FilePicker.UFDATA,
                FilePicker.MUSICXML);
            if (files == null || files.Length == 0) {
                return;
            }
            HideWelcome();
            viewModel.OpenProject(files);
        }

        /// <summary>欢迎视图：打开最近工程 / 恢复工程。</summary>
        public void WelcomeOpenRecent(string path) {
            HideWelcome();
            viewModel.OpenRecent(path);
        }

        /// <summary>欢迎视图：从模板新建。</summary>
        public void WelcomeOpenTemplate(string path) {
            HideWelcome();
            viewModel.OpenTemplate(path);
        }

        /// <summary>打开外部链接（欢迎视图快捷入口）。</summary>
        public void OpenUrl(string url) {
            try {
                OS.OpenWeb(url);
            } catch (Exception e) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        // ── 阶段 E3：WelcomeWindow 打开工程后的对接入口 ──
        public void OpenProjectFiles(string[] files) => viewModel.OpenProject(files);
        public void OpenTemplateFile(string file) => viewModel.OpenTemplate(file);
        public void ImportAudio() => OnMenuImportAudio(this, new RoutedEventArgs());
        public void ShowPackageManager() => OnMenuPackageManager(this, new RoutedEventArgs());

        void OnEditTimeSignature(object sender, PointerPressedEventArgs args) {
            var project = DocManager.Inst.Project;
            var timeSig = project.timeSignatures[0];
            var dialog = new TimeSignatureDialog(timeSig.beatPerBar, timeSig.beatUnit);
            dialog.OnOk = (beatPerBar, beatUnit) => {
                viewModel.PlaybackViewModel.SetTimeSignature(beatPerBar, beatUnit);
            };
            dialog.ShowDialog(this);
            // Workaround for https://github.com/AvaloniaUI/Avalonia/issues/3986
            args.Pointer.Capture(null);
        }

        void OnEditBpm(object sender, PointerPressedEventArgs args) {
            var project = DocManager.Inst.Project;
            var dialog = new TypeInDialog();
            dialog.Title = "BPM";
            dialog.SetText(project.tempos[0].bpm.ToString());
            dialog.onFinish = s => {
                if (double.TryParse(s, out double bpm)) {
                    viewModel.PlaybackViewModel.SetBpm(bpm);
                }
            };
            dialog.ShowDialog(this);
            // Workaround for https://github.com/AvaloniaUI/Avalonia/issues/3986
            args.Pointer.Capture(null);
        }

        private void AddTempoChange(int tick) {
            var project = DocManager.Inst.Project;
            var dialog = new TypeInDialog {
                Title = "BPM"
            };
            dialog.SetText(project.tempos[0].bpm.ToString());
            dialog.onFinish = s => {
                if (double.TryParse(s, out double bpm)) {
                    DocManager.Inst.StartUndoGroup("command.project.tempo");
                    DocManager.Inst.ExecuteCmd(new AddTempoChangeCommand(
                        project, tick, bpm));
                    DocManager.Inst.EndUndoGroup();
                }
            };
            dialog.ShowDialog(this);
        }

        private void DelTempoChange(int tick) {
            var project = DocManager.Inst.Project;
            DocManager.Inst.StartUndoGroup("command.project.tempo");
            DocManager.Inst.ExecuteCmd(new DelTempoChangeCommand(project, tick));
            DocManager.Inst.EndUndoGroup();
        }

        void OnMenuRemapTimeaxis(object sender, RoutedEventArgs e) {
            var project = DocManager.Inst.Project;
            var dialog = new TypeInDialog {
                Title = ThemeManager.GetString("menu.project.remaptimeaxis")
            };
            dialog.Height = 200;
            dialog.SetPrompt(ThemeManager.GetString("dialogs.remaptimeaxis.message"));
            dialog.SetText(project.tempos[0].bpm.ToString());
            dialog.onFinish = s => {
                try {
                    if (double.TryParse(s, out double bpm)) {
                        DocManager.Inst.StartUndoGroup("command.project.tempo");
                        var oldTimeAxis = project.timeAxis.Clone();
                        DocManager.Inst.ExecuteCmd(new BpmCommand(
                            project, bpm));
                        foreach (var tempo in project.tempos.Skip(1)) {
                            DocManager.Inst.ExecuteCmd(new DelTempoChangeCommand(
                                project, tempo.position));
                        }
                        viewModel.RemapTimeAxis(oldTimeAxis, project.timeAxis.Clone());
                        DocManager.Inst.EndUndoGroup();
                    }
                } catch (Exception e) {
                    Log.Error(e, "Failed to open project location");
                    MessageBox.ShowError(this, new MessageCustomizableException("Failed to open project location", "<translate:errors.failed.openlocation>: project location", e));
                }
            };
            dialog.ShowDialog(this);
        }

        private void AddTimeSigChange(int bar) {
            var project = DocManager.Inst.Project;
            var timeSig = project.timeAxis.TimeSignatureAtBar(bar);
            var dialog = new TimeSignatureDialog(timeSig.beatPerBar, timeSig.beatUnit);
            dialog.OnOk = (beatPerBar, beatUnit) => {
                DocManager.Inst.StartUndoGroup("command.project.timesignature");
                DocManager.Inst.ExecuteCmd(new AddTimeSigCommand(
                    project, bar, dialog.BeatPerBar, dialog.BeatUnit));
                DocManager.Inst.EndUndoGroup();
            };
            dialog.ShowDialog(this);
        }

        private void DelTimeSigChange(int bar) {
            var project = DocManager.Inst.Project;
            DocManager.Inst.StartUndoGroup("command.project.timesignature");
            DocManager.Inst.ExecuteCmd(new DelTimeSigCommand(project, bar));
            DocManager.Inst.EndUndoGroup();
        }

        void OnMenuNew(object sender, RoutedEventArgs args) => NewProject();
        internal async void NewProject() {
            if (!DocManager.Inst.ChangesSaved && !await AskIfSaveAndContinue()) {
                return;
            }
            viewModel.NewProject();
        }

        void OnMenuOpen(object sender, RoutedEventArgs args) => Open();
        internal async void Open() {
            if (!DocManager.Inst.ChangesSaved && !await AskIfSaveAndContinue()) {
                return;
            }
            var files = await FilePicker.OpenFilesAboutProject(
                this, "menu.file.open",
                FilePicker.ProjectFiles,
                FilePicker.USTX,
                FilePicker.VSQX,
                FilePicker.UST,
                FilePicker.MIDI,
                FilePicker.UFDATA,
                FilePicker.MUSICXML);
            if (files == null || files.Length == 0) {
                return;
            }
            try {
                viewModel.OpenProject(files);
                } catch (Exception e) {
                Log.Error(e, $"Failed to open files {string.Join("\n", files)}");
                _ = await MessageBox.ShowError(this, new MessageCustomizableException($"Failed to open files {string.Join("\n", files)}", $"<translate:errors.failed.openfile>:\n{string.Join("\n", files)}", e));
            }
        }

        void OnMainMenuOpened(object sender, RoutedEventArgs args) {
            viewModel.RefreshOpenRecent();
            viewModel.RefreshTemplates();
            viewModel.RefreshCacheSize();
        }

        /// <summary>品牌按钮 = 应用菜单（MenuFlyout）：点开前刷新「最近打开 / 模板 / 缓存」列表。</summary>
        void OnAppMenuClicked(object? sender, RoutedEventArgs args) {
            OnMainMenuOpened(sender!, args);
        }

        void OnMenuOpenProjectLocation(object sender, RoutedEventArgs args) {
            var project = DocManager.Inst.Project;
            if (string.IsNullOrEmpty(project.FilePath) || !project.Saved) {
                MessageBox.Show(
                    this,
                    ThemeManager.GetString("dialogs.export.savefirst"),
                    ThemeManager.GetString("errors.caption"),
                    MessageBox.MessageBoxButtons.Ok);
            }
            try {
                var dir = Path.GetDirectoryName(project.FilePath);
                if (dir != null) {
                    OS.OpenFolder(dir);
                } else {
                    Log.Error($"Failed to get project location from {dir}.");
                }
            } catch (Exception e) {
                Log.Error(e, "Failed to open project location.");
                MessageBox.ShowError(this, new MessageCustomizableException("Failed to open project location.", "<translate:errors.failed.openlocation>: project location", e));
            }
        }

        async void OnMenuSave(object sender, RoutedEventArgs args) => await Save();
        public async Task Save() {
            if (!viewModel.ProjectSaved) {
                await SaveAs();
            } else {
                viewModel.SaveProject();
                string message = ThemeManager.GetString("progress.saved");
                message = string.Format(message, DateTime.Now);
                DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, message));
            }
        }

        async void OnMenuSaveAs(object sender, RoutedEventArgs args) => await SaveAs();
        internal async Task SaveAs() {
            var file = await FilePicker.SaveFileAboutProject(
                this, "menu.file.saveas", FilePicker.USTXP);
            if (!string.IsNullOrEmpty(file)) {
                viewModel.SaveProject(file);
            }
        }

        void OnMenuSaveTemplate(object sender, RoutedEventArgs args) {
            var project = DocManager.Inst.Project;
            var dialog = new TypeInDialog();
            dialog.Title = ThemeManager.GetString("menu.file.savetemplate");
            dialog.SetText("default");
            dialog.onFinish = file => {
                if (string.IsNullOrEmpty(file)) {
                    return;
                }
                file = Path.GetFileNameWithoutExtension(file);
                file = $"{file}.ustxp";
                file = Path.Combine(PathManager.Inst.TemplatesPath, file);
                Ustxp.Save(file, project.CloneAsTemplate());
            };
            dialog.ShowDialog(this);
        }

        async void OnMenuImportTracks(object sender, RoutedEventArgs args) {
            var files = await FilePicker.OpenFilesAboutProject(
                this, "menu.file.importtracks",
                FilePicker.ProjectFiles,
                FilePicker.USTX,
                FilePicker.VSQX,
                FilePicker.UST,
                FilePicker.MIDI,
                FilePicker.UFDATA,
                FilePicker.MUSICXML);
            if (files == null || files.Length == 0) {
                return;
            }
            try {
                var loadedProjects = Formats.ReadProjects(files);
                if (loadedProjects == null || loadedProjects.Length == 0) {
                    return;
                }
                // Imports tempo for new projects, otherwise asks the user.
                bool importTempo = DocManager.Inst.Project.parts.Count == 0;
                if (!importTempo && loadedProjects[0].tempos.Count > 0) {
                    var tempoString = string.Join("\n",
                        loadedProjects[0].tempos
                            .Select(tempo => $"position: {tempo.position}, tempo: {tempo.bpm}")
                        );
                    // Ask the user
                    var result = await MessageBox.Show(
                        this,
                        ThemeManager.GetString("dialogs.importtracks.importtempo") + "\n" + tempoString,
                        ThemeManager.GetString("dialogs.importtracks.caption"),
                        MessageBox.MessageBoxButtons.YesNo);
                    importTempo = result == MessageBox.MessageBoxResult.Yes;
                }
                viewModel.ImportTracks(loadedProjects, importTempo);
            } catch (Exception e) {
                Log.Error(e, $"Failed to import files");
                _ = await MessageBox.ShowError(this, new MessageCustomizableException("Failed to import files", "<translate:errors.failed.importfiles>", e));
            }
            ValidateTracksVoiceColor();
        }

        async void OnMenuImportAudio(object sender, RoutedEventArgs args) {
            var files = await FilePicker.OpenFilesAboutProject(
                this, "menu.file.importaudio", FilePicker.AudioFiles);
            if (files == null || files.Length == 0) {
                return;
            }
            foreach (var file in files) {
                try {
                    viewModel.ImportAudio(file);
                } catch (Exception e) {
                    Log.Error(e, "Failed to import audio");
                    _ = await MessageBox.ShowError(this, new MessageCustomizableException("Failed to import audio", "<translate:errors.failed.importaudio>", e));
                }
            }
        }

        internal void OnMenuRender(object sender, RoutedEventArgs args) {
            var renderWindow = new RenderWindow();
            renderWindow.ShowDialog(this);
        }

        async void OnMenuExportMixdown(object sender, RoutedEventArgs args) {
            var project = DocManager.Inst.Project;
            var file = await FilePicker.SaveFileAboutProject(
                this, "menu.file.exportmixdown", FilePicker.WAV);
            if (!string.IsNullOrEmpty(file)) {
                await PlaybackManager.Inst.RenderMixdown(project, file);
            }
        }

        async void OnMenuExportWav(object sender, RoutedEventArgs args) {
            var project = DocManager.Inst.Project;
            if (await WarnToSave(project)) {
                var name = Path.GetFileNameWithoutExtension(project.FilePath);
                var path = Path.GetDirectoryName(project.FilePath);
                path = Path.Combine(path!, "Export", $"{name}.wav");
                await PlaybackManager.Inst.RenderToFiles(project, path);
            }
        }

        async void OnMenuExportWavTo(object sender, RoutedEventArgs args) {
            var project = DocManager.Inst.Project;
            var file = await FilePicker.SaveFileAboutProject(
                this, "menu.file.exportwavto", FilePicker.WAV);
            if (!string.IsNullOrEmpty(file)) {
                await PlaybackManager.Inst.RenderToFiles(project, file);
            }
        }

        async void OnMenuExportDsTo(object sender, RoutedEventArgs e) {
            var project = DocManager.Inst.Project;
            bool allRendered = project.parts
                .OfType<UVoicePart>()
                .All(part => part.renderPhrases.Count > 0 &&
                    part.renderPhrases.All(phrase => {
                        var hashStr = $"{phrase.hash:x16}";
                        return Directory.EnumerateFiles(
                            PathManager.Inst.CachePath, $"*{hashStr}*.wav").Any();
                    }));
            if (!allRendered) {
                await MessageBox.Show(
                    this,
                    ThemeManager.GetString("dialogs.exportds.notrendered"),
                    ThemeManager.GetString("errors.caption"),
                    MessageBox.MessageBoxButtons.Ok);
                return;
            }
            var vm = new DsScriptExportViewModel();
            var dialog = new DsScriptExportDialog { DataContext = vm };
            await dialog.ShowDialog(this);
            if (!dialog.Confirmed) {
                return;
            }
            var options = vm.BuildOptions();
            var file = await FilePicker.SaveFileAboutProject(
                this, "menu.file.exportds", FilePicker.DS);
            if (!string.IsNullOrEmpty(file)) {
                for (var i = 0; i < project.parts.Count; i++) {
                    var part = project.parts[i];
                    if (part is UVoicePart voicePart) {
                        var savePath = PathManager.Inst.GetPartSavePath(file, voicePart.DisplayName, i)[..^4] + ".ds";
                        DiffSingerScript.SavePart(project, voicePart, savePath, options);
                        DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, $"{savePath}."));
                    }
                }
            }
        }

        /// <summary>
        /// W27：另存为**纯净 .ustx** —— 写出原版 OpenUTAU 可直接读、且不含 Plus 专有字段的文件
        /// （`ustxp_version` / `vst_slots` / `mix_fx` 全部剥离）。这是目前唯一能安全跨软件交付的路径。
        /// **它不解决"原版重存丢 Plus 字段"**：原版保存时依然会丢掉它不认识的键，
        /// 那需要侧车文件或容器化格式（审计 A+/B 方案）。
        /// </summary>
        async void OnMenuExportCleanUstx(object sender, RoutedEventArgs args) {
            var project = DocManager.Inst.Project;
            var file = await FilePicker.SaveFileAboutProject(this, "menu.file.exportcleanustx", FilePicker.USTX);
            if (string.IsNullOrEmpty(file)) {
                return;
            }
            try {
                Ustxp.ExportCleanUstx(file, project);
                DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0,
                    $"{ThemeManager.GetString("ustxp.exportclean.done")} {file}"));
            } catch (Exception ex) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
            }
        }
        async void OnMenuExportUst(object sender, RoutedEventArgs e) {
            var project = DocManager.Inst.Project;
            if (await WarnToSave(project)) {
                var name = Path.GetFileNameWithoutExtension(project.FilePath);
                var path = Path.GetDirectoryName(project.FilePath);
                path = Path.Combine(path!, "Export", $"{name}.ust");
                for (var i = 0; i < project.parts.Count; i++) {
                    var part = project.parts[i];
                    if (part is UVoicePart voicePart) {
                        var savePath = PathManager.Inst.GetPartSavePath(path, voicePart.DisplayName, i);
                        Ust.SavePart(project, voicePart, savePath);
                        DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, $"{savePath}."));
                    }
                }
            }
        }

        async void OnMenuExportUstTo(object sender, RoutedEventArgs e) {
            var project = DocManager.Inst.Project;
            var file = await FilePicker.SaveFileAboutProject(
                this, "menu.file.exportustto", FilePicker.UST);
            if (!string.IsNullOrEmpty(file)) {
                for (var i = 0; i < project.parts.Count; i++) {
                    var part = project.parts[i];
                    if (part is UVoicePart voicePart) {
                        var savePath = PathManager.Inst.GetPartSavePath(file, voicePart.DisplayName, i);
                        Ust.SavePart(project, voicePart, savePath);
                        DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, $"{savePath}."));
                    }
                }
            }
        }

        async void OnMenuExportMidi(object sender, RoutedEventArgs e) {
            var project = DocManager.Inst.Project;
            var file = await FilePicker.SaveFileAboutProject(
                this, "menu.file.exportmidi", FilePicker.MIDI);
            if (!string.IsNullOrEmpty(file)) {
                MidiWriter.Save(file, project);
            }
        }

        private async Task<bool> WarnToSave(UProject project) {
            if (string.IsNullOrEmpty(project.FilePath)) {
                await MessageBox.Show(
                    this,
                    ThemeManager.GetString("dialogs.export.savefirst"),
                    ThemeManager.GetString("dialogs.export.caption"),
                    MessageBox.MessageBoxButtons.Ok);
                return false;
            }
            return true;
        }

        void OnMenuUndo(object sender, RoutedEventArgs args) => viewModel.Undo();
        void OnMenuRedo(object sender, RoutedEventArgs args) => viewModel.Redo();

        void OnMenuExpressionss(object sender, RoutedEventArgs args) {
            var dialog = new ExpressionsDialog() {
                DataContext = new ExpressionsViewModel(),
            };
            dialog.ShowDialog(this);
            if (dialog.Position.Y < 0) {
                dialog.Position = dialog.Position.WithY(0);
            }
        }

        async void OnMenuSingers(object sender, RoutedEventArgs args) {
            await OpenSingersWindowAsync();
        }

        /// <summary>
        /// Check if a track has a singer and if it exists.
        /// If the user haven't selected a singer for the track, or the singer specified in ustx project doesn't exist, return null.
        /// Otherwise, return the singer.
        /// </summary>
        public USinger? TrackSingerIfFound(UTrack track) {
            if (track.Singer?.Found ?? false) {
                return track.Singer;
            }
            return null;
        }

        public async Task OpenSingersWindowAsync() {
            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (lifetime == null) {
                return;
            }

            LoadingWindow.BeginLoadingImmediate(this);
            var dialog = await Task.Run(() => lifetime.Windows.FirstOrDefault(w => w is SingersDialog));
            try {
                if (dialog == null) {
                    SingersViewModel vm = await Task.Run<SingersViewModel>(() => {
                        USinger? singer = null;
                        if (viewModel.TracksViewModel.SelectedParts.Count > 0) {
                            singer = TrackSingerIfFound(viewModel.TracksViewModel.Tracks[viewModel.TracksViewModel.SelectedParts.First().trackNo]);
                        }
                        if (singer == null && viewModel.TracksViewModel.Tracks.Count > 0) {
                            singer = TrackSingerIfFound(viewModel.TracksViewModel.Tracks.First());
                        }
                        var vm = new SingersViewModel();

                        if (singer != null) {
                            vm.Singer = singer;
                        }

                        return vm;
                    });

                    dialog = new SingersDialog() { DataContext = vm };
                    dialog.Show();
                }
                if (dialog.Position.Y < 0) {
                    dialog.Position = dialog.Position.WithY(0);
                }
            } catch (Exception e) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            } finally {
                LoadingWindow.EndLoading();
            }
            if (dialog != null) {
                dialog.Activate();
            }
        }

        async void OnMenuInstallSinger(object sender, RoutedEventArgs args) {
            var file = await FilePicker.OpenFileAboutSinger(
                this, "menu.tools.singer.install", FilePicker.ArchiveFiles);
            if (file == null) {
                return;
            }
            try {
                if (file.EndsWith(Core.Vogen.VogenSingerInstaller.FileExt)) {
                    Core.Vogen.VogenSingerInstaller.Install(file);
                    return;
                }
                if (file.EndsWith(PackageManager.OudepExt)) {
                    await PackageManager.Inst.InstallFromFileAsync(file);
                    return;
                }

                var setup = new SingerSetupDialog() {
                    DataContext = new SingerSetupViewModel() {
                        ArchiveFilePath = file,
                    },
                };
                _ = setup.ShowDialog(this);
                if (setup.Position.Y < 0) {
                    setup.Position = setup.Position.WithY(0);
                }
            } catch (Exception e) {
                Log.Error(e, $"Failed to install singer {file}");
                _ = await MessageBox.ShowError(this, new MessageCustomizableException($"Failed to install singer {file}", $"<translate:errors.failed.installsinger>: {file}", e));
            }
        }

        void OnMenuPackageManager(object sender, RoutedEventArgs args) {
            try {
                var dialog = new PackageManagerDialog() { DataContext = new PackageManagerViewModel() };
                dialog.Show();
                if (dialog.Position.Y < 0) dialog.Position = dialog.Position.WithY(0);
            } catch (Exception e) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        async void OnMenuInstallWavtoolResampler(object sender, RoutedEventArgs args) {
            var filter = OS.IsWindows()
                ? new[] { FilePicker.EXE }
                : new[] { FilePicker.EXE, FilePicker.UnixExecutable };

            var file = await FilePicker.OpenFile(
                this, "menu.tools.dependency.install", filter);
            if (file == null) {
                return;
            }

            if (file.EndsWith(".exe")) {
                var setup = new ExeSetupDialog() {
                    DataContext = new ExeSetupViewModel(file)
                };
                _ = setup.ShowDialog(this);
                if (setup.Position.Y < 0) {
                    setup.Position = setup.Position.WithY(0);
                }
            }
        }

        void OnMenuPreferences(object sender, RoutedEventArgs args) => ShowPreferences();

        /// <summary>
        /// 顶栏按视图切内容（工作台 / 卷帘 / 混音台共用同一条 56px 顶栏，与设计稿一致）：
        /// 欢迎页 / 偏好页只留品牌 + 屏名；三个工作视图显示运输组 + 右侧组 + 胶囊；
        /// 「分离」只在卷帘 / 混音台出场。策略全在 <see cref="ViewSwitcherPolicy.ChromeFor"/>（可单测）。
        /// </summary>
        private void SetChromeForView(ViewChrome chrome) {
            ScreenTitle[!TextBlock.TextProperty] = new DynamicResourceExtension(chrome.TitleKey);
            TransportGroup.IsVisible = chrome.ShowTransport;
            TopRightCluster.IsVisible = chrome.ShowRightCluster;
            ViewSwitcher.IsVisible = chrome.ShowViewSwitcher;
            DetachViewButton.IsVisible = chrome.ShowDetachButton;
        }

        // ── S5 视图切换（工作台 / 钢琴卷帘 / 混音台）────────────────────────
        // 语义：工作台 = 工作区三列；卷帘 / 混音台 = 铺满整行的视图宿主（设计稿里两者都是整屏）。
        // 切换 = 翻 IsVisible + 一次淡入 + 换顶栏 chrome；列宽与行高都不动 ⇒ 无布局抖动。

        /// <summary>切到某个工作视图（卷帘 / 混音台的宿主控件由调用方保证已就绪）。</summary>
        private void SwitchToView(AppSurface view) {
            viewModel.ViewSwitcher.SwitchTo(view);   // 改变 → OnCurrentViewChanged
        }

        /// <summary>视图变化后的副作用：淡入态、宿主控件就位、焦点、顶栏 chrome。</summary>
        private void OnCurrentViewChanged(AppSurface view) {
            // 进入的视图淡入；离开的视图复位（下次再进来才有一次淡入）。工作台是三列，不做淡入。
            SetShown(PianoRollContainer, view == AppSurface.PianoRoll);
            SetShown(MixerContainer, view == AppSurface.Mixer);
            switch (view) {
                case AppSurface.PianoRoll:
                    HostInView(PianoRollContainer, pianoRoll, pianoRollWindow != null);
                    pianoRoll?.Focus();
                    break;
                case AppSurface.Mixer:
                    HostInView(MixerContainer, mixerControl, mixerWindow != null);
                    break;
            }
            // 焦点铁律：卷帘隐藏后焦点可能仍留在它内部的控件上，会把全局快捷键一起吞掉
            // （OnWindowKeyDown 有 IsKeyboardFocusWithin 早退），故离开卷帘时把焦点收回窗口。
            if (view != AppSurface.PianoRoll) {
                Focus();
            }
            SetChromeForView(viewModel.ViewSwitcher.Chrome);
        }

        /// <summary>
        /// 把控件挂进视图区（幂等）：只有「控件存在 + 未分离 + 还不在这个容器里」才挂。
        /// 所有进入视图的路径（胶囊 / Ctrl+M / 双击片段 / 关分离窗口）都过这里，避免漏挂空视图。
        /// </summary>
        private static void HostInView(ContentControl container, Control? control, bool detached) {
            if (control == null || detached || ReferenceEquals(container.Content, control)) {
                return;
            }
            container.Content = control;
        }

        /// <summary>
        /// 摘下一个视图宿主（分离/收回的唯一出口），并把**旧窗口**的挂起布局就地跑完。
        ///
        /// 为什么必须冲洗（task-21，Avalonia 12.1.0 实测复现 + 源码核实 `LayoutManager.cs`）：
        /// `Content = null` 摘除子树时，Avalonia 会把整棵子树入队到**旧窗口**的 measure 队列并调度一次
        /// 布局 pass（`_toMeasure=[子树…], _queued=true`）。若随后把这棵子树挂到另一个窗口，旧窗那次
        /// pass 再跑时 `ExecuteArrangePass` 会走到 `_toArrangeAfterMeasure → InvalidateArrange(control)`，
        /// 而该控件的 layout root 已经是新窗口 ⇒
        /// `ArgumentException: Attempt to call InvalidateArrange on wrong LayoutManager`（未处理 → 程序退出）。
        /// 摘树后立刻 `UpdateLayout()`：此刻子树还没有新家，布局遍历对它是
        /// `!IsAttachedToVisualTree ⇒ NotVisible`，直接跳过（不会触碰 Invalidate*）⇒ 队列被安全消化，
        /// 旧窗随后也没有待处理的 pass 了。
        /// </summary>
        public static void DetachAndFlush(ContentControl host) {
            var oldRoot = TopLevel.GetTopLevel(host);
            host.Content = null;
            oldRoot?.UpdateLayout();
        }

        /// <summary>顶栏胶囊：工作台 / 钢琴卷帘 / 混音台（Tag = AppSurface 名）。</summary>
        private void OnViewTabClicked(object? sender, RoutedEventArgs args) {
            if (sender is not Control control || control.Tag is not string tag ||
                !Enum.TryParse<AppSurface>(tag, ignoreCase: true, out var view)) {
                return;
            }
            RequestView(view);
        }

        /// <summary>
        /// 进入某个视图（胶囊 / Ctrl+M / 菜单共用）：
        /// 卷帘沿用 A5「双击片段进入」，未创建过则胶囊不作用；已分离则把独立窗口置前。
        /// </summary>
        private void RequestView(AppSurface view) {
            switch (view) {
                case AppSurface.PianoRoll:
                    if (pianoRoll == null) {
                        return;                     // A5：入口仍是双击片段（胶囊只负责回切）
                    }
                    if (pianoRollWindow != null) {
                        pianoRollWindow.Show();
                        pianoRollWindow.Activate();
                        return;
                    }
                    SwitchToView(AppSurface.PianoRoll);
                    break;
                case AppSurface.Mixer:
                    if (mixerWindow != null) {
                        mixerWindow.Show();
                        mixerWindow.Activate();
                        return;
                    }
                    EnsureMixerControl();
                    SwitchToView(AppSurface.Mixer);
                    break;
                default:
                    SwitchToView(AppSurface.Workspace);
                    break;
            }
        }

        /// <summary>顶栏「分离」（A2/A5）：把当前视图弹成独立窗口，主窗口落回工作台。</summary>
        private void OnDetachViewClicked(object? sender, RoutedEventArgs args) {
            switch (viewModel.ViewSwitcher.CurrentView) {
                case AppSurface.PianoRoll:
                    DetachPianoRollView();
                    break;
                case AppSurface.Mixer:
                    DetachMixerView();
                    break;
            }
        }

        /// <summary>被分离的视图正显示着时，视图区落回工作台（别的视图不受打扰）。</summary>
        private void SwitchAwayIfShowing(AppSurface detachedView) {
            var fallback = ViewSwitcherPolicy.ViewAfterDetach(detachedView, viewModel.ViewSwitcher.CurrentView);
            if (fallback != viewModel.ViewSwitcher.CurrentView) {
                SwitchToView(fallback);
            }
        }

        /// <summary>偏好设置（全屏视图，设计稿 6-Preferences）。</summary>
        public void ShowPreferences() {
            PreferencesViewModel dataContext;
            try {
                dataContext = new PreferencesViewModel();
            } catch (Exception e) {
                Log.Error(e, "Failed to load prefs. Initialize it.");
                MessageBox.ShowError(this, new MessageCustomizableException("Failed to load prefs. Initialize it.", "<translate:errors.failed.loadprefs>", e));
                Preferences.Reset();
                dataContext = new PreferencesViewModel();
            }
            PreferencesHost.Host = this;
            PreferencesHost.DataContext = dataContext;
            PreferencesHost.SingersDataContext = sidebarViewModel;
            PreferencesHost.ShowDefaultPage();
            PreferencesHost.IsVisible = true;
            // 顶栏屏名切成「偏好设置」，并亮出「完成」按钮（顶栏在偏好视图之上，不被遮）
            SetChromeForView(ViewSwitcherPolicy.ChromeFor(AppSurface.Preferences));
            SetShown(PreferencesHost, true);
        }

        /// <summary>退出偏好设置（淡出后隐藏）。</summary>
        public async void HidePreferences() {
            if (!PreferencesHost.IsVisible) {
                return;
            }
            SetShown(PreferencesHost, false);
            await Task.Delay(TransitionMs);
            PreferencesHost.IsVisible = false;
            bool backToWelcome = WelcomeHost.IsVisible;
            SetChromeForView(backToWelcome
                ? ViewSwitcherPolicy.ChromeFor(AppSurface.Welcome)
                : viewModel.ViewSwitcher.Chrome);
        }

        /// <summary>恢复默认设置（二次确认 → 重置 → 重开偏好页）。</summary>
        public async void ResetAllPreferences() {
            var result = await MessageBox.Show(this,
                ThemeManager.GetString("prefs.reset.all"),
                ThemeManager.GetString("prefs.caption"),
                MessageBox.MessageBoxButtons.OkCancel);
            if (result != MessageBox.MessageBoxResult.Ok) {
                return;
            }
            Preferences.Reset();
            App.SetTheme();
            ShowPreferences();
        }

        /// <summary>选择文件夹（偏好页的「更改」按钮）。</summary>
        public Task<string?> PickFolder(string titleKey) => FilePicker.OpenFolderAboutSinger(this, titleKey);

        internal void OnMenuFullScreen(object sender, RoutedEventArgs args) {
            this.WindowState = this.WindowState == WindowState.FullScreen
                ? WindowState.Normal
                : WindowState.FullScreen;
        }

        void OnMenuClearCache(object sender, RoutedEventArgs args) {
            Task.Run(() => {
                DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, ThemeManager.GetString("progress.clearingcache")));
                PathManager.Inst.ClearCache();
                DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, ThemeManager.GetString("progress.cachecleared")));
            });
        }

        internal void OnMenuMixer(object sender, RoutedEventArgs args) {
            OpenOrToggleMixer();
        }

        /// <summary>Ctrl+M / 工具菜单 / 顶栏按钮 = 「开/关混音台」（决策表见 ViewSwitcherPolicy）。</summary>
        void OpenOrToggleMixer() {
            var action = ViewSwitcherPolicy.DecideMixerOpen(
                hasControl: mixerControl != null,
                hasDetachedWindow: mixerWindow != null,
                detachPreferred: Preferences.Default.DetachMixer,
                mixerViewActive: viewModel.ViewSwitcher.CurrentView == AppSurface.Mixer);
            switch (action) {
                case MixerOpenAction.CreateEmbedded:
                    EnsureMixerControl();
                    SwitchToView(AppSurface.Mixer);
                    break;
                case MixerOpenAction.CreateDetached:
                    EnsureMixerControl();
                    DetachMixerView();
                    break;
                case MixerOpenAction.ActivateDetached:
                    mixerWindow!.Show();
                    mixerWindow.Activate();
                    break;
                case MixerOpenAction.SwitchToMixerView:
                    SwitchToView(AppSurface.Mixer);
                    break;
                case MixerOpenAction.BackToWorkspace:
                    SwitchToView(AppSurface.Workspace);
                    break;
            }
        }

        /// <summary>首次使用时才建混音台控件（避免启动开销；与 Ctrl+M 原语义一致）。</summary>
        private void EnsureMixerControl() {
            if (mixerControl == null) {
                mixerControl = new MixerControl();
            }
        }

        /// <summary>
        /// 分离混音台（A2/A5）：控件交给独立窗口，偏好置位并落盘；当前正显示混音台视图时让位回工作台。
        /// </summary>
        private void DetachMixerView() {
            EnsureMixerControl();
            if (mixerWindow != null) {
                mixerWindow.Show();
                mixerWindow.Activate();
                return;
            }
            DetachAndFlush(MixerContainer);
            mixerWindow = new MixerWindow(mixerControl!);
            // 用户关掉分离窗口 = 收回视图区（生命周期：控件不随窗口销毁，见 MixerWindow 注释）
            mixerWindow.ReturnToHost = () => AttachMixerView();
            mixerWindow.Show();
            Preferences.Default.DetachMixer = true;
            Preferences.Save();
            SwitchAwayIfShowing(AppSurface.Mixer);
        }

        /// <summary>收回混音台：控件放回视图区容器并切到混音台视图（分离窗口关闭 / 偏好翻转共用）。</summary>
        private void AttachMixerView() {
            if (mixerControl == null) {
                return;
            }
            var window = mixerWindow;
            mixerWindow = null;
            if (window != null) {
                window.ReturnToHost = null;
                window.ReleaseControl();   // 摘 Content → 关窗：**不** Shutdown（控件继续存活）
            }
            MixerContainer.Content = mixerControl;
            Preferences.Default.DetachMixer = false;
            Preferences.Save();
            SwitchToView(AppSurface.Mixer);
        }

        /// <summary>按偏好翻转混音台归属（内部/外部共用的一处收口）。</summary>
        public void SetMixerAttachment() {
            if (Preferences.Default.DetachMixer) {
                AttachMixerView();
            } else {
                DetachMixerView();
            }
        }

        internal void ToggleMixerWindow() {
            // Ctrl+W：切换混音台的贴合/分离（未建控件时先建，并遵循偏好）
            if (mixerControl == null) {
                EnsureMixerControl();
                if (Preferences.Default.DetachMixer) {
                    DetachMixerView();
                } else {
                    SwitchToView(AppSurface.Mixer);
                }
                return;
            }
            if (mixerWindow != null) {
                AttachMixerView();
            } else {
                DetachMixerView();
            }
        }

        // ── 素材库（右侧 296：音源 / 音频 / MIDI / 效果器） ──────────────────

        /// <summary>表头「＋」：新增轨道。</summary>
        private void OnAddTrack(object? sender, RoutedEventArgs e) {
            viewModel.TracksViewModel.AddTrack();
        }

        /// <summary>页签选中态（同组内互斥）。</summary>
        private static void SetTabSelected(Button selected, Button[] group) {
            foreach (var button in group) {
                button.Classes.Set("selected", button == selected);
            }
        }

        /// <summary>双击歌手卡片 → 新建轨道添加歌手。</summary>
        private void OnSingerDoubleTap(object? sender, TappedEventArgs e) {
            if (sender is Border { DataContext: SingerItem item }) {
                viewModel.AddSingerTrack(item.Singer);
            }
        }

        /// <summary>侧栏卡片按下：记录起点、按下事件与拖拽载荷（歌手/伴奏）。</summary>
        private void OnSidebarPointerPressed(object? sender, PointerPressedEventArgs args) {
            if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
                sender is not Border { DataContext: { } data }) {
                return;
            }
            sidebarDragStart = args.GetPosition(this);
            sidebarPressedArgs = args;
            sidebarDragSinger = data as SingerItem;
            sidebarDragSample = data as SampleItem;
        }

        /// <summary>侧栏卡片移动超过阈值 → 发起拖拽（进程内自定义格式）。</summary>
        private async void OnSidebarPointerMoved(object? sender, PointerEventArgs args) {
            if (sidebarPressedArgs == null || (sidebarDragSinger == null && sidebarDragSample == null)) {
                return;
            }
            var delta = args.GetPosition(this) - sidebarDragStart;
            if (Math.Abs(delta.X) < 5 && Math.Abs(delta.Y) < 5) {
                return;
            }
            var data = new DataTransfer();
            if (sidebarDragSinger != null) {
                data.Add(DataTransferItem.Create(SingerDragFormat, sidebarDragSinger.Singer));
            } else {
                data.Add(DataTransferItem.Create(AudioDragFormat, sidebarDragSample!.Path));
            }
            var pressed = sidebarPressedArgs;
            sidebarPressedArgs = null;
            sidebarDragSinger = null;
            sidebarDragSample = null;
            await DragDrop.DoDragDropAsync(pressed, data, DragDropEffects.Copy);
        }

        /// <summary>刷新歌手列表（重扫歌手库 → 通知侧栏刷新）。</summary>
        private async void OnRefreshSingers(object? sender, RoutedEventArgs e) {
            LoadingWindow.BeginLoading(this);
            await Task.Run(() => SingerManager.Inst.SearchAllSingers());
            DocManager.Inst.ExecuteCmd(new SingersRefreshedNotification());
            LoadingWindow.EndLoading();
        }

        /// <summary>双击伴奏卡片 → 新建轨道添加音频。</summary>
        private void OnSampleDoubleTap(object? sender, TappedEventArgs e) {
            if (sender is Border { DataContext: SampleItem item }) {
                ImportAudioSafely(item.Path);
            }
        }

        /// <summary>试听/停止伴奏（独立通道，不中断工程播放；再次点击停止）。</summary>
        private void OnSamplePlay(object? sender, RoutedEventArgs e) {
            if (sender is Button { DataContext: SampleItem item }) {
                if (PlaybackManager.Inst.PreviewPath == item.Path) {
                    PlaybackManager.Inst.StopPreview();
                } else {
                    PlaybackManager.Inst.PlayPreview(item.Path);
                }
            }
        }

        /// <summary>重扫伴奏目录（仅枚举文件名，不读元数据）。</summary>
        private void OnRefreshSamples(object? sender, RoutedEventArgs e) {
            sidebarViewModel.RefreshSamples();
        }

        private void ImportAudioSafely(string path) {
            try {
                viewModel.ImportAudio(path);
            } catch (Exception e) {
                Log.Error(e, $"Failed to import audio {path}");
                _ = MessageBox.ShowError(this, new MessageCustomizableException("Failed to import audio", "<translate:errors.failed.importaudio>", e));
            }
        }

        /// <summary>素材库页签：音源（歌手）。</summary>
        private void OnShowSingers(object? sender, RoutedEventArgs e) {
            ShowLibraryPage(SingersPanel, SingersTab);
        }

        /// <summary>素材库页签：音频（伴奏库）。</summary>
        private void OnShowSamples(object? sender, RoutedEventArgs e) {
            ShowLibraryPage(SamplesPanel, SamplesTab);
        }

        /// <summary>素材库页签：MIDI（占位）。</summary>
        private void OnShowMidi(object? sender, RoutedEventArgs e) {
            ShowLibraryPage(MidiPanel, MidiTab);
        }

        /// <summary>素材库页签：效果器（W4 浏览器；W11 首次展开会播种标准路径 + 空表自动首扫）。</summary>
        private void OnShowVst(object? sender, RoutedEventArgs e) {
            viewModel.PluginBrowser.RefreshPlugins();
            viewModel.PluginBrowser.EnsureFirstScan();   // 幂等：只在首次展开时播种/首扫（返回的任务由后台自己跑完）
            ShowLibraryPage(VstPanel, EffectsTab);
        }

        // ── W4：素材库「效果器」页签（插件浏览器）──────────────────────────────
        // 拖拽源与歌手/伴奏卡片同款：按下记起点，移动超阈值发起拖拽；
        // 负载走 W3 冻结契约 FxChainDragData（格式 OpenUtau.FxChainItem），落点由链面板 DropPayload 消费。
        private Point pluginDragStart;
        private PointerPressedEventArgs? pluginPressedArgs;
        private VstPluginItem? pluginDragItem;

        /// <summary>插件行按下：记录起点与待拖拽项。</summary>
        private void OnPluginPointerPressed(object? sender, PointerPressedEventArgs args) {
            if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
                sender is not Border { DataContext: VstPluginItem item }) {
                return;
            }
            pluginDragStart = args.GetPosition(this);
            pluginPressedArgs = args;
            pluginDragItem = item;
        }

        /// <summary>插件行移动超过 5px → 拖出（负载 = 插件 UID）。</summary>
        private async void OnPluginPointerMoved(object? sender, PointerEventArgs args) {
            if (pluginPressedArgs == null || pluginDragItem == null) {
                return;
            }
            var delta = args.GetPosition(this) - pluginDragStart;
            if (Math.Abs(delta.X) < 5 && Math.Abs(delta.Y) < 5) {
                return;
            }
            var pressed = pluginPressedArgs;
            var data = PluginBrowserViewModel.CreatePluginDragData(pluginDragItem);
            pluginPressedArgs = null;
            pluginDragItem = null;
            await DragDrop.DoDragDropAsync(pressed, data, DragDropEffects.Copy);
        }

        /// <summary>重新扫描插件（后台线程扫描；完成后广播，偏好设置侧计数同步刷新）。</summary>
        private async void OnRescanVstFromLibrary(object? sender, RoutedEventArgs e) {
            await viewModel.PluginBrowser.RescanInBackgroundAsync();
        }

        /// <summary>展开/收起「插件扫描路径」面板。</summary>
        private void OnToggleVstPathManager(object? sender, RoutedEventArgs e) {
            VstPathManager.IsVisible = !VstPathManager.IsVisible;
        }

        /// <summary>
        /// 空态按钮：把本机标准 VST3 目录加进扫描路径并立即重扫（W11）。
        /// 用户主动触发 ⇒ 不受"已播种"标记限制；标准目录都不存在时只重扫（0 条新增）。
        /// </summary>
        private async void OnAddStandardVstPathsFromLibrary(object? sender, RoutedEventArgs e) {
            await viewModel.PluginBrowser.AddStandardPathsAndScanAsync();
        }

        /// <summary>添加扫描路径（写 Preferences.Default.VstScanPaths + 广播，与偏好设置即时同步）。</summary>
        private void OnAddVstPathFromLibrary(object? sender, RoutedEventArgs e) {
            viewModel.PluginBrowser.AddPathFromInput();
        }

        /// <summary>移除选中的扫描路径（同上）。</summary>
        private void OnRemoveVstPathFromLibrary(object? sender, RoutedEventArgs e) {
            if (VstPathsList.SelectedItem is string path) {
                viewModel.PluginBrowser.RemoveScanPath(path);
            }
        }

        private void ShowLibraryPage(Control page, Button tab) {
            SingersPanel.IsVisible = page == SingersPanel;
            SamplesPanel.IsVisible = page == SamplesPanel;
            MidiPanel.IsVisible = page == MidiPanel;
            VstPanel.IsVisible = page == VstPanel;
            SetTabSelected(tab, new[] { SingersTab, SamplesTab, MidiTab, EffectsTab });
        }

        void OnMenuDebugWindow(object sender, RoutedEventArgs args) {
            var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (desktop == null) {
                return;
            }
            var window = desktop.Windows.FirstOrDefault(w => w is DebugWindow);
            if (window == null) {
                window = new DebugWindow();
            }
            window.Show();
        }

        void OnMenuControlGallery(object sender, RoutedEventArgs args) {
            var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (desktop == null) {
                return;
            }
            var window = desktop.Windows.FirstOrDefault(w => w is ControlGalleryWindow);
            if (window == null) {
                window = new ControlGalleryWindow();
            }
            window.Show();
        }

        void OnMenuPhoneticAssistant(object sender, RoutedEventArgs args) {
            var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (desktop == null) {
                return;
            }
            var window = desktop.Windows.FirstOrDefault(w => w is PhoneticAssistant);
            if (window == null) {
                window = new PhoneticAssistant();
            }
            window.Show();
        }

        void OnMenuCheckUpdate(object sender, RoutedEventArgs args) {
            var dialog = new UpdaterDialog();
            dialog.ViewModel.CloseApplication =
                () => (Application.Current?.ApplicationLifetime as IControlledApplicationLifetime)?.Shutdown();
            ShowOverlayContent(dialog);
        }

        void OnMenuLogsLocation(object sender, RoutedEventArgs args) {
            try {
                OS.OpenFolder(PathManager.Inst.LogsPath);
            } catch (Exception e) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        void OnMenuReportIssue(object sender, RoutedEventArgs args) {
            try {
                OS.OpenWeb("https://github.com/stakira/OpenUtau/issues");
            } catch (Exception e) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        void OnMenuWiki(object sender, RoutedEventArgs args) {
            try {
                OS.OpenWeb("https://github.com/stakira/OpenUtau/wiki/Getting-Started");
            } catch (Exception e) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        void OnMenuLayoutReset(object sender, RoutedEventArgs args) {
            WindowState = WindowState.Normal;
            Position = new PixelPoint(0, 0);
            Width = 1024;
            Height = 576;
            if (pianoRollWindow != null) {
                pianoRollWindow.WindowState = WindowState.Normal;
                pianoRollWindow.Position = new PixelPoint(100, 100);
                pianoRollWindow.Width = 1024;
                pianoRollWindow.Height = 576;
            }
        }

        // ── W16 面板系统：面板 chrome 的交互入口 ─────────────────────────────
        // 拖拽/双击复位由 PanelSplitter 控件自己处理（一处实现，所有面板共用）；
        // 这里只做"落盘"和"折叠"两件事 —— 接入方按配方加自己的同名处理器即可。

        /// <summary>面板头部 chevron：折叠轨头列。</summary>
        private void OnCollapseTracksPanel(object? sender, RoutedEventArgs e) {
            viewModel.TracksPanel.ToggleCollapse();
        }

        /// <summary>面板头部 chevron：折叠素材库列。</summary>
        private void OnCollapseLibraryPanel(object? sender, RoutedEventArgs e) {
            viewModel.LibraryPanel.ToggleCollapse();
        }

        /// <summary>拖拽结束 / 双击复位 → 落盘（拖动过程中只更新内存，不写文件）。</summary>
        private void OnPanelSplitterDragCompleted(object? sender, EventArgs e) {
            viewModel.PersistPanelLayout();
        }

        /// <summary>「重置面板布局」：宽度回默认 + 全部展开 + 落盘。</summary>
        private void OnMenuResetPanelLayout(object sender, RoutedEventArgs args) {
            viewModel.ResetPanelLayout();
        }

        void OnMenuLayoutVSplit11(object sender, RoutedEventArgs args) => LayoutSplit(null, 1.0 / 2);
        void OnMenuLayoutVSplit12(object sender, RoutedEventArgs args) => LayoutSplit(null, 1.0 / 3);
        void OnMenuLayoutVSplit13(object sender, RoutedEventArgs args) => LayoutSplit(null, 1.0 / 4);
        void OnMenuLayoutHSplit11(object sender, RoutedEventArgs args) => LayoutSplit(1.0 / 2, null);
        void OnMenuLayoutHSplit12(object sender, RoutedEventArgs args) => LayoutSplit(1.0 / 3, null);
        void OnMenuLayoutHSplit13(object sender, RoutedEventArgs args) => LayoutSplit(1.0 / 4, null);

        private void LayoutSplit(double? x, double? y) {
            var mainScreen = Screens.Primary ?? Screens.All[0];
            if (mainScreen == null) {
                return;
            }
            double screenWidth = mainScreen.WorkingArea.Size.Width / mainScreen.Scaling;
            double screenHeight = mainScreen.WorkingArea.Size.Height / mainScreen.Scaling;
            double borderThickness = 0;
            double titleBarHeight = 20;
            if (FrameSize != null) {
                if (OS.IsWindows()) {
                    borderThickness = (FrameSize.Value.Width - ClientSize.Width) / 2;
                }
                titleBarHeight = FrameSize.Value.Height - ClientSize.Height - borderThickness;
            }
            int startX = mainScreen.WorkingArea.Position.X - (int)(borderThickness * mainScreen.Scaling);
            int startY = mainScreen.WorkingArea.Position.Y;

            WindowState = WindowState.Normal;
            // Position in physical pixels
            Position = new PixelPoint(startX, startY);
            // Size in logical pixels (DIPs)
            Width = x != null ? screenWidth * x.Value : screenWidth;
            Height = (y != null ? screenHeight * y.Value : screenHeight) - titleBarHeight;
            if (pianoRollWindow != null) {
                pianoRollWindow.WindowState = WindowState.Normal;
                double offsetX = x != null ? this.Width : 0;
                double offsetY = y != null ? (this.Height + titleBarHeight) : 0;
                int physX = startX + (int)(offsetX * mainScreen.Scaling);
                int physY = startY + (int)(offsetY * mainScreen.Scaling);
                pianoRollWindow.Position = new PixelPoint(physX, physY);
                pianoRollWindow.Width = x != null ? screenWidth - Width : screenWidth;
                pianoRollWindow.Height = (y != null ? screenHeight - offsetY : screenHeight) - titleBarHeight;
            }
        }

        /// <summary>
        /// Global key handler registered via AddHandler(handledEventsToo:true).
        /// Catches Ctrl+M etc. even when piano roll or other children have consumed the event.
        /// 与 <see cref="OnKeyDown"/> 共用一个映射表 + 同一事件去重（隧道/冒泡两路只会动作一次）。
        /// </summary>
        void OnWindowKeyDown(object? sender, KeyEventArgs args) {
            HandleGlobalShortcut(args);
        }

        /// <summary>Ctrl 组合的全局快捷键（顶栏之前先处理）。</summary>
        public enum GlobalShortcut {
            None,
            /// <summary>Ctrl+M：开 / 关混音台（见 <see cref="ViewSwitcherPolicy.DecideMixerOpen"/>）。</summary>
            ToggleMixer,
            /// <summary>Ctrl+W：切换混音台的贴合 / 分离。</summary>
            ToggleMixerAttachment,
            Save,
        }

        /// <summary>
        /// 快捷键映射表（纯函数，便于契约测试断言 Ctrl+M 路径）。
        /// 只认单修饰键等于 <paramref name="cmdKey"/> 的组合。
        /// </summary>
        public static GlobalShortcut MapGlobalShortcut(Key key, KeyModifiers modifiers, KeyModifiers cmdKey) {
            if (modifiers != cmdKey) {
                return GlobalShortcut.None;
            }
            // 由注册表投影（不再手写第二张表）
            return CommandRegistry.Match(key, modifiers, cmdKey)?.Id switch {
                "tools.mixer" => GlobalShortcut.ToggleMixer,
                "tools.mixerattach" => GlobalShortcut.ToggleMixerAttachment,
                "file.save" => GlobalShortcut.Save,
                _ => GlobalShortcut.None,
            };
        }

        // 同一次按键从隧道 + 冒泡两路到达时只动作一次（AddHandler 两路注册，事件实例相同）
        private KeyEventArgs? handledShortcutArgs;

        private void HandleGlobalShortcut(KeyEventArgs args) {
            // 注册表分发（含隧道/冒泡去重），不再走手写 switch
            if (TryExecuteShortcut(args, preFocus: true)) {
                args.Handled = true;
            }
        }

        // ── 命令层（W28 / M09）窗口级处理体 ────────────────────────────────────
        // 原先是只写在 OnKeyDown 的 switch 里的内联分支；现抽成方法 ⇒ 注册表的处理体
        // 与键盘分发共用同一份实现（消除"显示一套 / 行为另一套"）。

        /// <summary>播放头跳到工程末尾（原 Key.End 分支，语义不变）。</summary>
        internal void MovePlayPosToEnd() {
            var parts = viewModel.TracksViewModel.Parts;
            if (parts.Count > 0) {
                viewModel.PlaybackViewModel.MovePlayPos(parts.Max(part => part.End));
            }
        }

        /// <summary>独奏当前选中片段所在轨道（原 Shift+S 分支，语义不变）。</summary>
        internal void SoloSelectedPart() {
            var selected = viewModel.TracksViewModel.SelectedParts;
            if (selected.Count == 0 || DocManager.Inst.Project == null) {
                return;
            }
            var part = selected.First();
            var track = DocManager.Inst.Project.tracks[part.trackNo];
            MessageBus.Current.SendMessage(new TracksSoloEvent(part.trackNo, !track.Solo, false));
        }

        /// <summary>静音当前选中片段所在轨道（原 Shift+M 分支，语义不变）。</summary>
        internal void MuteSelectedPart() {
            var selected = viewModel.TracksViewModel.SelectedParts;
            if (selected.Count == 0) {
                return;
            }
            var part = selected.First();
            MessageBus.Current.SendMessage(new TracksMuteEvent(part.trackNo, false));
        }

        /// <summary>快捷键总览（W28/M09）：数据源 = 命令注册表，只读。</summary>
        internal void OnMenuShortcutOverview(object? sender, RoutedEventArgs args) {
            ShortcutOverviewWindow.Open(this);
        }

        /// <summary>退出应用（原 Alt+F4 分支，语义不变）。</summary>
        internal void QuitApplication() {
            (Application.Current?.ApplicationLifetime as IControlledApplicationLifetime)?.Shutdown();
        }

        void OnKeyDown(object sender, KeyEventArgs args) {
            // Modal overlay open — block shortcuts, Esc dismisses
            if (OverlayLayer.IsVisible) {
                if (args.Key == Key.Escape) {
                    CloseOverlay();
                }
                args.Handled = true;
                return;
            }

            // ① 全局快捷键（先于焦点检查；隧道 + 冒泡双路只动作一次）——唯一来源：CommandRegistry
            if (TryExecuteShortcut(args, preFocus: true)) {
                args.Handled = true;
                return;
            }

            // 焦点铁律：只有卷帘**正在显示**时才把键盘让给它；切到别的视图后它可能仍持有焦点
            if (viewModel.ViewSwitcher.ShowPianoRoll && PianoRollContainer.IsKeyboardFocusWithin) {
                args.Handled = false;
                return;
            }

            // ② 其余窗口级命令（手势 → 注册表 → 处理体；未命中一律 false ⇒ 不吞按键，与原语义一致）
            args.Handled = TryExecuteShortcut(args, preFocus: false);
        }

        /// <summary>
        /// 命令层分发（W28 / M09）：手势 → <see cref="CommandRegistry"/> → 处理体。
        /// 命中即执行并返回 true（调用方据此置 Handled）；未命中返回 false。
        /// </summary>
        /// <param name="preFocus">true = 只走"先于焦点检查"的全局命令（Ctrl+M / Ctrl+W / Ctrl+S）。</param>
        bool TryExecuteShortcut(KeyEventArgs args, bool preFocus) {
            var def = CommandRegistry.All.FirstOrDefault(c =>
                c.PreFocus == preFocus &&
                c.Scope == CommandScope.Window &&
                MatchesGesture(c, args.Key, args.KeyModifiers));
            if (def == null) {
                return false;
            }
            if (preFocus) {
                // 同一次按键从隧道 + 冒泡两路到达时只动作一次（AddHandler 两路注册，事件实例相同）
                if (ReferenceEquals(handledShortcutArgs, args)) {
                    return true;
                }
                handledShortcutArgs = args;
            }
            if (def.CanExecute?.Invoke(this) == false) {
                return true;   // 命中但当前不可用：按键仍算已处理（与菜单 IsEnabled=false 的观感一致）
            }
            def.Execute(this);
            return true;
        }

        /// <summary>注册表手势（按平台解析主修饰键后）是否等于本次按键。</summary>
        bool MatchesGesture(CommandDefinition def, Key key, KeyModifiers modifiers) {
            var g = CommandRegistry.Resolve(def.Gesture, cmdKey);
            return g != null && g.Key == key && g.KeyModifiers == modifiers;
        }

        void OnPointerPressed(object? sender, PointerPressedEventArgs args) {
            // 卷帘正在显示时点它内部不该抢走焦点；其余视图点空白一律把焦点收回窗口
            bool pianoRollShown = viewModel.ViewSwitcher.ShowPianoRoll && PianoRollContainer.IsPointerOver;
            if (!pianoRollShown && !args.Handled && args.ClickCount == 1) {
                this.Focus();
            }
        }

        async void OnDrop(object? sender, DragEventArgs args) {
            // 阶段 E4：侧栏素材库拖拽——自定义数据格式优先（歌手 / 伴奏）
            var dragSinger = args.DataTransfer.TryGetValue(SingerDragFormat);
            if (dragSinger != null) {
                viewModel.AddSingerTrack(dragSinger);
                return;
            }
            var dragAudio = args.DataTransfer.TryGetValue(AudioDragFormat);
            if (dragAudio != null && File.Exists(dragAudio)) {
                try {
                    viewModel.ImportAudio(dragAudio);
                } catch (Exception e) {
                    Log.Error(e, "Failed to import audio");
                    _ = await MessageBox.ShowError(this, new MessageCustomizableException("Failed to import audio", "<translate:errors.failed.importaudio>", e));
                }
                return;
            }
            string[] ProjectExts = { ".ustxp", ".ustx", ".ust", ".vsqx", ".ufdata", ".musicxml", ".mid", ".midi" };
            string[] ArchiveExts = { ".zip", ".rar", ".uar" };
            string[] AudioExts = { ".mp3", ".wav", ".ogg", ".flac" };
            string[] SupportedExts = ProjectExts
                .Concat(ArchiveExts)
                .Concat(AudioExts)
                .Append(".dll")
                .Append(".exe")
                .Append(Core.Vogen.VogenSingerInstaller.FileExt)
                .Append(PackageManager.OudepExt)
                .ToArray();
            var files = args.DataTransfer.TryGetFiles()?.Select(i => i.Path.LocalPath).ToArray() ?? new string[] { };
            if (files.Length == 0) {
                return;
            }
            var supportedFiles = files.Where(file => SupportedExts.Contains(Path.GetExtension(file).ToLower())).ToArray();
            if (supportedFiles.Length == 0) {
                _ = await MessageBox.Show(
                    this,
                    ThemeManager.GetString("dialogs.unsupportedfile.message") + Path.GetExtension(files[0]),
                    ThemeManager.GetString("dialogs.unsupportedfile.caption"),
                    MessageBox.MessageBoxButtons.Ok);
                return;
            }
            string FirstExt = Path.GetExtension(supportedFiles[0]).ToLower();
            // 欢迎视图下拖入工程 / 音频：直接进入编辑器
            if (ProjectExts.Contains(FirstExt) || AudioExts.Contains(FirstExt)) {
                HideWelcome();
            }
            //If multiple project/audio files are dropped, open/import them all.
            if (ProjectExts.Contains(FirstExt) || AudioExts.Contains(FirstExt)) {
                var projectFiles = supportedFiles.Where(file => ProjectExts.Contains(Path.GetExtension(file).ToLower())).ToArray();
                    if (projectFiles.Length > 0) {
                    try {
                        var loadedProjects = Formats.ReadProjects(files);
                        // Imports tempo for new projects, otherwise asks the user.
                        bool importTempo = DocManager.Inst.Project.parts.Count == 0;
                        if (!importTempo && loadedProjects[0].tempos.Count > 0) {
                            var tempoString = string.Join("\n",
                                loadedProjects[0].tempos
                                    .Select(tempo => $"position: {tempo.position}, tempo: {tempo.bpm}")
                                );
                            // Ask the user
                            var result = await MessageBox.Show(
                                this,
                                ThemeManager.GetString("dialogs.importtracks.importtempo") + "\n" + tempoString,
                                ThemeManager.GetString("dialogs.importtracks.caption"),
                                MessageBox.MessageBoxButtons.YesNo);
                            importTempo = result == MessageBox.MessageBoxResult.Yes;
                        }
                        viewModel.ImportTracks(loadedProjects, importTempo);
                    } catch (Exception e) {
                        Log.Error(e, "Failed to import project");
                        _ = await MessageBox.ShowError(this, new MessageCustomizableException("Failed to import files", "<translate:errors.failed.importfiles>", e));
                    }
                }
                var audioFiles = supportedFiles.Where(file => AudioExts.Contains(Path.GetExtension(file).ToLower())).ToArray();
                foreach (var audioFile in audioFiles) {
                    try {
                        viewModel.ImportAudio(audioFile);
                    } catch (Exception e) {
                        Log.Error(e, "Failed to import audio");
                        _ = await MessageBox.ShowError(this, new MessageCustomizableException("Failed to import audio", "<translate:errors.failed.importaudio>", e));
                    }
                }
                return;
            }
            // Otherwise, only one installer file is handled at a time.
            string file = supportedFiles[0];
            var ext = Path.GetExtension(file).ToLower();
            if (ext == ".zip" || ext == ".rar" || ext == ".uar") {
                try {
                    var setup = new SingerSetupDialog() {
                        DataContext = new SingerSetupViewModel() {
                            ArchiveFilePath = file,
                        },
                    };
                    _ = setup.ShowDialog(this);
                    if (setup.Position.Y < 0) {
                        setup.Position = setup.Position.WithY(0);
                    }
                } catch (Exception e) {
                    Log.Error(e, $"Failed to install singer {file}");
                    _ = await MessageBox.ShowError(this, new MessageCustomizableException($"Failed to install singer {file}", $"<translate:errors.failed.installsinger>: {file}", e));
                }
            } else if (ext == Core.Vogen.VogenSingerInstaller.FileExt) {
                Core.Vogen.VogenSingerInstaller.Install(file);
            } else if (ext == ".dll") {
                var result = await MessageBox.Show(
                    this,
                    ThemeManager.GetString("dialogs.installdll.message") + file,
                    ThemeManager.GetString("dialogs.installdll.caption"),
                    MessageBox.MessageBoxButtons.OkCancel);
                if (result == MessageBox.MessageBoxResult.Ok) {
                    Core.Api.PhonemizerInstaller.Install(file);
                }
            } else if (ext == ".exe") {
                var setup = new ExeSetupDialog() {
                    DataContext = new ExeSetupViewModel(file)
                };
                _ = setup.ShowDialog(this);
                if (setup.Position.Y < 0) {
                    setup.Position = setup.Position.WithY(0);
                }
            } else if (ext == PackageManager.OudepExt) {
                var result = await MessageBox.Show(
                    this,
                    ThemeManager.GetString("dialogs.installdependency.message") + file,
                    ThemeManager.GetString("dialogs.installdependency.caption"),
                    MessageBox.MessageBoxButtons.OkCancel);
                if (result == MessageBox.MessageBoxResult.Ok) {
                    try {
                        await PackageManager.Inst.InstallFromFileAsync(file);
                    } catch (Exception e) {
                        Log.Error(e, $"Failed to install dependency {file}");
                        _ = await MessageBox.ShowError(this, new MessageCustomizableException(
                            $"Failed to install dependency {file}",
                            $"<translate:errors.failed.installdependency>: {file}", e));
                    }
                }
            }
        }

        void OnPlayOrPause(object sender, RoutedEventArgs args) {
            PlayOrPause();
        }

        internal void PlayOrPause() {
            viewModel.PlaybackViewModel.PlayOrPause();
        }

        public void HScrollPointerWheelChanged(object sender, PointerWheelEventArgs args) {
            hScroll.By(-HScrollBar.SmallChange * args.Delta.Y, SmoothViewport.IsWheelStep(args.Delta.Y));
        }

        public void VScrollPointerWheelChanged(object sender, PointerWheelEventArgs args) {
            vScroll.By(-VScrollBar.SmallChange * args.Delta.Y, SmoothViewport.IsWheelStep(args.Delta.Y));
        }

        public void TimelinePointerWheelChanged(object sender, PointerWheelEventArgs args) {
            var control = (Control)sender;
            var position = args.GetCurrentPoint((Visual)sender).Position;
            var size = control.Bounds.Size;
            position = position.WithX(position.X / size.Width).WithY(position.Y / size.Height);
            xZoom.By(position, 0.1 * args.Delta.Y, SmoothViewport.IsWheelStep(args.Delta.Y));
        }

        public void ViewScalerPointerWheelChanged(object sender, PointerWheelEventArgs args) {
            trackHeight.By(Math.Sign(args.Delta.Y) * ViewConstants.TrackHeightDelta, SmoothViewport.IsWheelStep(args.Delta.Y));
        }

        public void TimelinePointerPressed(object sender, PointerPressedEventArgs args) {
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            if (point.Properties.IsLeftButtonPressed) {
                args.Pointer.Capture(control);
                viewModel.TracksViewModel.PointToLineTick(point.Position, out int left, out int right);
                viewModel.PlaybackViewModel.MovePlayPos(left);
            } else if (point.Properties.IsRightButtonPressed) {
                isSelectingRange = true;
                rangeSelectStartPoint = point.Position;
                viewModel.RefreshTimelineContextMenu(viewModel.TracksViewModel.PointToTick(point.Position));
            }
        }

        public void TimelinePointerMoved(object sender, PointerEventArgs args) {
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            if (point.Properties.IsLeftButtonPressed) {
                viewModel.TracksViewModel.PointToLineTick(point.Position, out int left, out int right);
                viewModel.PlaybackViewModel.MovePlayPos(left);
            } else if (point.Properties.IsRightButtonPressed && isSelectingRange) {
                double dx = Math.Abs(point.Position.X - rangeSelectStartPoint.X);
                if (dx >= RangeSelectThreshold) {
                    UpdateRangeSelection(point.Position);
                }
            }
            Cursor = null;
        }

        public void TimelinePointerReleased(object sender, PointerReleasedEventArgs args) {
            if (isSelectingRange && args.InitialPressMouseButton == MouseButton.Right) {
                isSelectingRange = false;
                var control = (Control)sender;
                var point = args.GetCurrentPoint(control);
                double dx = Math.Abs(point.Position.X - rangeSelectStartPoint.X);
                if (dx >= RangeSelectThreshold) {
                    UpdateRangeSelection(point.Position);
                }
            }
            args.Pointer.Capture(null);
        }

        public void TimelineDoubleTapped(object sender, TappedEventArgs args) {
            DocManager.Inst.ExecuteCmd(new SetRangeSelectionNotification(0, 0));
        }

        private void UpdateRangeSelection(Point currentPoint) {
            var tracksVm = viewModel.TracksViewModel;
            tracksVm.PointToLineTick(rangeSelectStartPoint, out int startLeft, out int startRight);
            tracksVm.PointToLineTick(currentPoint, out int endLeft, out int endRight);
            int left = Math.Min(startLeft, endLeft);
            int right = Math.Max(startRight, endRight);
            DocManager.Inst.ExecuteCmd(new SetRangeSelectionNotification(left, right));
        }

        public void PartsCanvasPointerPressed(object sender, PointerPressedEventArgs args) {
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            var hitControl = control.InputHitTest(point.Position);
            if (partEditState != null) {
                return;
            }
            if (point.Properties.IsLeftButtonPressed) {
                if (args.KeyModifiers == cmdKey) {
                    partEditState = new PartSelectionEditState(control, viewModel, SelectionBox);
                    Cursor = ViewConstants.cursorCross;
                } else if (hitControl == control) {
                    viewModel.TracksViewModel.DeselectParts();
                    var part = viewModel.TracksViewModel.MaybeAddPart(point.Position);
                    if (part != null) {
                        // Start moving right away
                        partEditState = new PartMoveEditState(control, viewModel, part);
                        Cursor = ViewConstants.cursorSizeAll;
                    }
                } else if (hitControl is PartControl partControl) {
                    bool fadein = false;
                    bool fadeout = false;
                    if (partControl.part is UWavePart wavePart && point.Position.Y < partControl.Bounds.Top + 6) {
                        var fadePos = partControl.Bounds.Left + partControl.FadeIn;
                        fadein = fadePos < point.Position.X && point.Position.X < fadePos + 6;
                        fadePos = partControl.Bounds.Left + partControl.FadeOut;
                        fadeout = fadePos - 6 < point.Position.X && point.Position.X < fadePos;
                    }
                    bool skip = point.Position.X < partControl.Bounds.Left + ViewConstants.ResizeMargin;
                    bool trim = point.Position.X > partControl.Bounds.Right - ViewConstants.ResizeMargin;
                    if (fadein) {
                        partEditState = new PartFadeInState(control, viewModel, (UWavePart)partControl.part);
                        Cursor = ViewConstants.cursorSizeWE;
                    } else if (fadeout) {
                        partEditState = new PartFadeOutState(control, viewModel, (UWavePart)partControl.part);
                        Cursor = ViewConstants.cursorSizeWE;
                    } else if (skip) {
                        partEditState = new PartResizeEditState(control, viewModel, partControl.part, true);
                        Cursor = ViewConstants.cursorSizeWE;
                    } else if (trim) {
                        partEditState = new PartResizeEditState(control, viewModel, partControl.part);
                        Cursor = ViewConstants.cursorSizeWE;
                    } else {
                        partEditState = new PartMoveEditState(control, viewModel, partControl.part);
                        Cursor = ViewConstants.cursorSizeAll;
                    }
                }
            } else if (point.Properties.IsRightButtonPressed) {
                if (hitControl is PartControl partControl) {
                    if (!viewModel.TracksViewModel.SelectedParts.Contains(partControl.part)) {
                        viewModel.TracksViewModel.DeselectParts();
                        viewModel.TracksViewModel.SelectPart(partControl.part);
                    }
                    if (PartsContextMenu != null && viewModel.TracksViewModel.SelectedParts.Count > 0) {
                        var menuArgs = new PartsContextMenuArgs {
                            Part = partControl.part,
                            PartDeleteCommand = viewModel.PartDeleteCommand,
                            PartGotoFileCommand = PartGotoFileCommand,
                            PartReplaceAudioCommand = PartReplaceAudioCommand,
                            PartRenameCommand = PartRenameCommand,
                            PartTranscribeCommand = PartTranscribeCommand,
                            PartMergeCommand = PartMergeCommand,
                            PartSplitCommand = PartSplitCommand
                        };
                        if (partControl.part is UVoicePart voicePart) {
                            menuArgs.PartApplyPitchMenuItems = DocManager.Inst.Project.parts
                                .OfType<UWavePart>()
                                .OrderBy(p => p.trackNo)
                                .ThenBy(p => p.position)
                                .Select(p => new MenuItemViewModel {
                                    Header = $"{DocManager.Inst.Project.tracks[p.trackNo].TrackName} - {p.DisplayName}",
                                    Command = ReactiveCommand.CreateFromTask(async () => await ApplyPitchFrom(voicePart, p))
                                })
                                .DefaultIfEmpty(new MenuItemViewModel {
                                    Header = ThemeManager.GetString("context.part.nopitchsource"),
                                    IsEnabled = false
                                })
                                .ToList();
                        }
                        PartsContextMenu.DataContext = menuArgs;
                        shouldOpenPartsContextMenu = true;
                    }
                } else {
                    viewModel.TracksViewModel.DeselectParts();
                }
            } else if (point.Properties.IsMiddleButtonPressed) {
                partEditState = new PartPanningState(control, viewModel);
                Cursor = ViewConstants.cursorHand;
            }
            if (partEditState != null) {
                partEditState.Begin(point.Pointer, point.Position);
                partEditState.Update(point.Pointer, point.Position);
            }
        }

        public void PartsCanvasPointerMoved(object sender, PointerEventArgs args) {
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            if (partEditState != null) {
                partEditState.Update(point.Pointer, point.Position);
                return;
            }
            var hitControl = control.InputHitTest(point.Position);
            if (hitControl is PartControl partControl) {
                bool fadein = false;
                bool fadeout = false;
                if (partControl.part is UWavePart wavePart && point.Position.Y < partControl.Bounds.Top + 6) {
                    var fadePos = partControl.Bounds.Left + partControl.FadeIn;
                    fadein = fadePos < point.Position.X && point.Position.X < fadePos + 6;
                    fadePos = partControl.Bounds.Left + partControl.FadeOut;
                    fadeout = fadePos - 6 < point.Position.X && point.Position.X < fadePos;
                }
                bool skip = point.Position.X < partControl.Bounds.Left + ViewConstants.ResizeMargin;
                bool trim = point.Position.X > partControl.Bounds.Right - ViewConstants.ResizeMargin;
                if (fadein || fadeout) {
                    Cursor = ViewConstants.cursorHand;
                } else if (skip || trim) {
                    Cursor = ViewConstants.cursorSizeWE;
                } else {
                    Cursor = null;
                }
            } else {
                Cursor = null;
            }
        }

        public void PartsCanvasPointerReleased(object sender, PointerReleasedEventArgs args) {
            if (partEditState?.MouseButton != args.InitialPressMouseButton) {
                return;
            }
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            partEditState.Update(point.Pointer, point.Position);
            partEditState.End(point.Pointer, point.Position);
            partEditState = null;
            Cursor = null;
        }

        public async void PartsCanvasDoubleTapped(object sender, TappedEventArgs args) {
            if (sender is not Canvas canvas) {
                return;
            }
            var control = canvas.InputHitTest(args.GetPosition(canvas));
            if (control is PartControl partControl && partControl.part is UVoicePart) {
                if (pianoRoll == null) {
                    LoadingWindow.BeginLoading(this);

                    var model = await Task.Run<PianoRollViewModel>(() => new PianoRollViewModel());

                    // Let's attach when needed to avoid startup slowdowns
                    pianoRoll = new PianoRoll(model) {
                        MainWindow = this
                    };

                    if (Preferences.Default.DetachPianoRoll) {
                        pianoRollWindow = CreatePianoRollWindow();
                    } else {
                        PianoRollContainer.Content = pianoRoll;
                    }

                    await Task.Run(() =>
                        pianoRoll.InitializePianoRollWindowAsync()
                    );
                    LoadingWindow.EndLoading();

                    pianoRoll.ViewModel.PlaybackViewModel = viewModel.PlaybackViewModel;
                }
                if (pianoRollWindow != null) {
                    pianoRollWindow.Show();
                    pianoRollWindow.Activate();
                } else {
                    // S5：卷帘是工作区的一个视图（A5 进入方式不变 = 双击片段）
                    SwitchToView(AppSurface.PianoRoll);
                    pianoRoll.Focus();
                }
                int tick = viewModel.TracksViewModel.PointToTick(args.GetPosition(canvas));
                DocManager.Inst.ExecuteCmd(new LoadPartNotification(partControl.part, DocManager.Inst.Project, tick));
                pianoRoll.AttachExpressions();
            }
        }

        /// <summary>建卷帘分离窗口：用户关窗 = 收回视图区（控件不随窗口销毁）。</summary>
        private PianoRollDetachedWindow CreatePianoRollWindow() {
            var window = new PianoRollDetachedWindow(pianoRoll!);
            window.ReturnToHost = () => AttachPianoRollView();
            pianoRollWindow = window;
            return window;
        }

        /// <summary>分离钢琴卷帘（A2/A5）：控件交给独立窗口，偏好置位并落盘。</summary>
        private void DetachPianoRollView() {
            if (pianoRoll == null || pianoRollWindow != null) {
                return;
            }
            DetachAndFlush(PianoRollContainer);
            CreatePianoRollWindow().Show();
            Preferences.Default.DetachPianoRoll = true;
            Preferences.Save();
            SwitchAwayIfShowing(AppSurface.PianoRoll);   // 正显示卷帘时视图让位回工作台
        }

        /// <summary>收回钢琴卷帘：控件放回视图区容器并切到卷帘视图（关窗 / 偏好翻转共用）。</summary>
        private void AttachPianoRollView() {
            if (pianoRoll == null) {
                return;
            }
            var window = pianoRollWindow;
            pianoRollWindow = null;
            if (window != null) {
                window.ReturnToHost = null;
                window.ReleaseControl();   // 摘 Content → 关窗（不 Shutdown：PianoRoll 的订阅随控件存活）
            }
            PianoRollContainer.Content = pianoRoll;
            Preferences.Default.DetachPianoRoll = false;
            Preferences.Save();
            SwitchToView(AppSurface.PianoRoll);
        }

        /// <summary>
        /// 按偏好当前值附着/分离钢琴窗（偏好翻转与保存由调用方处理，上游 #2230 语义）：
        /// 偏好为真 → 弹独立窗口（视图区让位）；为假 → 收回视图区并切到卷帘视图。
        /// </summary>
        public void SetPianoRollAttachment() {
            if (pianoRoll == null) {
                return;
            }
            if (Preferences.Default.DetachPianoRoll) {
                DetachPianoRollView();
            } else {
                AttachPianoRollView();
            }
        }

        public void MainPagePointerWheelChanged(object sender, PointerWheelEventArgs args) {
            var delta = args.Delta;
            if (args.KeyModifiers == KeyModifiers.None || args.KeyModifiers == KeyModifiers.Shift) {
                if (args.KeyModifiers == KeyModifiers.Shift) {
                    delta = new Vector(delta.Y, delta.X);
                }
                if (delta.X != 0) {
                    hScroll.By(-HScrollBar.SmallChange * delta.X, SmoothViewport.IsWheelStep(delta.X));
                }
                if (delta.Y != 0) {
                    vScroll.By(-VScrollBar.SmallChange * delta.Y, SmoothViewport.IsWheelStep(delta.Y));
                }
            } else if (args.KeyModifiers == KeyModifiers.Alt) {
                ViewScalerPointerWheelChanged(VScaler, args);
            } else if (args.KeyModifiers == cmdKey) {
                TimelinePointerWheelChanged(TimelineCanvas, args);
            }
            if (partEditState != null) {
                var point = args.GetCurrentPoint(partEditState.control);
                partEditState.Update(point.Pointer, point.Position);
            }
        }

        public void PartsContextMenuOpening(object sender, CancelEventArgs args) {
            if (shouldOpenPartsContextMenu) {
                shouldOpenPartsContextMenu = false;
            } else {
                args.Cancel = true;
            }
        }

        async Task ApplyPitchFrom(UVoicePart target, UWavePart source) {
            if (!RmvpeTranscriber.IsInstalled()) {
                await MessageBox.Show(
                    this,
                    ThemeManager.GetString("dialogs.transcribe.rmvpe.notfound"),
                    ThemeManager.GetString("errors.caption"),
                    MessageBox.MessageBoxButtons.Ok);
                return;
            }
            var project = DocManager.Inst.Project;
            if (!project.expressions.ContainsKey(Ustx.PITD)) {
                await MessageBox.Show(
                    this,
                    $"Expression '{Ustx.PITD}' not found.",
                    ThemeManager.GetString("errors.caption"),
                    MessageBox.MessageBoxButtons.Ok);
                return;
            }
            if (target.notes.Count == 0) {
                await MessageBox.Show(
                    this,
                    ThemeManager.GetString("lyrics.nonote"),
                    ThemeManager.GetString("errors.caption"),
                    MessageBox.MessageBoxButtons.Ok);
                return;
            }

            bool cancelled = false;
            using var cts = new CancellationTokenSource();
            MessageBox? msgbox = null;
            EventHandler? closedHandler = null;
            try {
                string text = ThemeManager.GetString("context.part.extractingpitch");
                msgbox = MessageBox.ShowModal(this, $"{text} {source.DisplayName}", text);
                closedHandler = (_, __) => {
                    cancelled = true;
                    cts.Cancel();
                };
                msgbox.Closed += closedHandler;

                double srcStartMs = project.timeAxis.TickPosToMsPos(source.position);
                double srcSkipMs = source.GetSkipMs(project);
                double targetStartMs = project.timeAxis.TickPosToMsPos(target.position);
                double targetEndMs = project.timeAxis.TickPosToMsPos(target.End);
                double targetDurMs = targetEndMs - targetStartMs;

                double startSrcFileMs = Math.Max(0, targetStartMs - srcStartMs + srcSkipMs - 1000);
                double endSrcFileMs = Math.Min(source.fileDurationMs, targetEndMs - srcStartMs + srcSkipMs + 1000);

                if (endSrcFileMs <= startSrcFileMs) {
                    await MessageBox.Show(
                        this,
                        ThemeManager.GetString("context.part.nopitchregion"),
                        ThemeManager.GetString("errors.caption"),
                        MessageBox.MessageBoxButtons.Ok);
                    return;
                }
                endSrcFileMs = Math.Max(0, endSrcFileMs);

                RmvpeResult? srcResult = await Task.Run(() => {
                    using var rmvpe = new RmvpeTranscriber();
                    using (cts.Token.Register(() => rmvpe.Interrupt())) {
                        if (cts.Token.IsCancellationRequested) {
                            return null;
                        }
                        return rmvpe.Infer(source, startSrcFileMs, endSrcFileMs);
                    }
                });

                if (srcResult != null && !cancelled) {
                    var frameMs = srcResult.TimeStepSeconds * 1000.0;
                    int targetFrames = (int)Math.Ceiling(targetDurMs / frameMs) + 1;
                    var targetMidi = new float[targetFrames];

                    for (int i = 0; i < targetFrames; i++) {
                        double currentTargetMs = i * frameMs;
                        double absMs = targetStartMs + currentTargetMs;
                        double srcFileMs = absMs - srcStartMs + srcSkipMs;

                        int srcIdx = (int)Math.Round((srcFileMs - startSrcFileMs) / frameMs);
                        if (srcIdx >= 0 && srcIdx < srcResult.MidiPitch.Length) {
                            targetMidi[i] = srcResult.MidiPitch[srcIdx];
                        } else {
                            targetMidi[i] = float.NaN;
                        }
                    }

                    if (targetMidi.All(float.IsNaN)) {
                        await Dispatcher.UIThread.InvokeAsync(() => MessageBox.Show(
                            this,
                            ThemeManager.GetString("context.part.nopitchdetected"),
                            ThemeManager.GetString("errors.caption"),
                            MessageBox.MessageBoxButtons.Ok));
                    } else {
                        var targetResult = new RmvpeResult {
                            TimeStepSeconds = srcResult.TimeStepSeconds,
                            MidiPitch = targetMidi
                        };

                        DocManager.Inst.StartUndoGroup("context.part.applypitch");
                        targetResult.ApplyToPart(project, target);
                        DocManager.Inst.EndUndoGroup();
                    }
                }
            } catch (Exception e) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            } finally {
                if (msgbox != null) {
                    if (closedHandler != null) {
                        msgbox.Closed -= closedHandler;
                    }
                    msgbox.Close();
                }
            }
        }

        public void PartsContextMenuClosing(object sender, CancelEventArgs args) {
            if (PartsContextMenu != null) {
                PartsContextMenu.DataContext = null;
            }
        }

        void RenamePart(UPart part) {
            var dialog = new TypeInDialog();
            dialog.Title = ThemeManager.GetString("context.part.rename");
            dialog.SetText(part.name);
            dialog.onFinish = name => {
                if (!string.IsNullOrWhiteSpace(name) && name != part.name) {
                    if (!string.IsNullOrWhiteSpace(name) && name != part.name) {
                        DocManager.Inst.StartUndoGroup("command.part.edit");
                        DocManager.Inst.ExecuteCmd(new RenamePartCommand(DocManager.Inst.Project, part, name));
                        DocManager.Inst.EndUndoGroup();
                    }
                }
            };
            dialog.ShowDialog(this);
        }

        void GotoFile(UPart part) {
            //View the location of the audio file in explorer if the part is a wave part
            if (part is UWavePart wavePart) {
                try {
                    OS.GotoFile(wavePart.FilePath);
                } catch (Exception e) {
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
                }
            }
        }

        async void ReplaceAudio(UPart part) {
            var file = await FilePicker.OpenFileAboutProject(
                this, "context.part.replaceaudio", FilePicker.AudioFiles);
            if (file == null) {
                return;
            }
            UWavePart newPart = new UWavePart() {
                FilePath = file,
                trackNo = part.trackNo,
                position = part.position
            };
            newPart.Load(DocManager.Inst.Project);
            DocManager.Inst.StartUndoGroup("command.import.audio");
            DocManager.Inst.ExecuteCmd(new ReplacePartCommand(DocManager.Inst.Project, part, newPart));
            DocManager.Inst.EndUndoGroup();
        }

        async void Transcribe(UPart part) {
            //Convert audio to notes
            if (part is UWavePart wavePart) {
                // Show algorithm selection dialog first
                var transcribeVm = new TranscribeViewModel();
                if (transcribeVm.NoneAvailable) {
                    await MessageBox.Show(this,
                        String.Format(ThemeManager.GetString("dialogs.transcribe.allnotfound"),
                            Game.DownloadUrl),
                        ThemeManager.GetString("dialogs.transcribe.caption"),
                        MessageBox.MessageBoxButtons.Ok);
                    return;
                }
                var transcribeDialog = new TranscribeDialog { DataContext = transcribeVm };
                await transcribeDialog.ShowDialog(this);
                if (!transcribeDialog.Confirmed) {
                    return;
                }

                bool cancelled = false;
                using var cts = new CancellationTokenSource();
                MessageBox? msgbox = null;
                EventHandler? closedHandler = null;
                try {
                    string midiText = ThemeManager.GetString("context.part.transcribing");
                    string pitchText =  ThemeManager.GetString("context.part.extractingpitch");
                    msgbox = MessageBox.ShowModal(this, $"{midiText} {part.name}", midiText);
                    closedHandler = (_, __) => {
                        cancelled = true;
                        cts.Cancel();
                    };
                    msgbox.Closed += closedHandler;
                    Func<bool> confirmLongChunk = () => {
                        return Dispatcher.UIThread.InvokeAsync(async () => {
                            var result = await MessageBox.Show(
                                this,
                                ThemeManager.GetString("dialogs.transcribe.longchunk.message"),
                                ThemeManager.GetString("dialogs.transcribe.caption"),
                                MessageBox.MessageBoxButtons.YesNo);
                            return result == MessageBox.MessageBoxResult.Yes;
                        }).GetAwaiter().GetResult();
                    };
                    UVoicePart? voicePart;
                    if (transcribeVm.SelectedAlgorithm == TranscribeAlgorithm.SOME) {
                        voicePart = await Task.Run(() => {
                            using (var some = new Some()) {
                                using (cts.Token.Register(() => some.Interrupt())) {
                                    if (cts.Token.IsCancellationRequested) {
                                        return null;
                                    }
                                    return some.Transcribe(DocManager.Inst.Project, wavePart,
                                        null, null,
                                        confirmLongChunk,
                                        (processedS, totalS) => {
                                            msgbox.SetText(string.Format("{0} {1}\n{2}s / {3}s", midiText, part.name, processedS, totalS));
                                        });
                                }
                            }
                        });
                    } else {
                        var gameOptions = transcribeVm.BuildGameOptions();
                        var batchingStrategy = transcribeVm.BuildBatchingStrategy();
                        voicePart = await Task.Run(() => {
                            using (var game = new Game()) {
                                using (cts.Token.Register(() => game.Interrupt())) {
                                    if (cts.Token.IsCancellationRequested) {
                                        return null;
                                    }
                                    return game.Transcribe(DocManager.Inst.Project, wavePart,
                                        gameOptions, batchingStrategy,
                                        confirmLongChunk,
                                        (processedS, totalS) => {
                                            msgbox.SetText(string.Format("{0} {1}\n{2}s / {3}s", midiText, part.name, processedS, totalS));
                                        });
                                }
                            }
                        });
                    }
                    RmvpeResult? rmvpeResult = null;
                    if (voicePart != null && transcribeVm.PredictPitd && !cancelled) {
                        msgbox.SetText($"{pitchText} {part.name}");
                        rmvpeResult = await Task.Run(() => {
                            using var rmvpe = new RmvpeTranscriber();
                            using (cts.Token.Register(() => rmvpe.Interrupt())) {
                                if (cts.Token.IsCancellationRequested) {
                                    return null;
                                }
                                return rmvpe.Infer(wavePart);
                            }
                        });
                    }
                    if (voicePart != null && !cancelled) {
                        var project = DocManager.Inst.Project;
                        var track = new UTrack(project);
                        track.TrackNo = project.tracks.Count;
                        voicePart.trackNo = track.TrackNo;
                        DocManager.Inst.StartUndoGroup("command.part.transcribe");
                        DocManager.Inst.ExecuteCmd(new AddTrackCommand(project, track));
                        DocManager.Inst.ExecuteCmd(new AddPartCommand(project, voicePart));
                        if (rmvpeResult != null) {
                            var wavePosMs = project.timeAxis.TickPosToMsPos(wavePart.position);
                            var voicePosMs = project.timeAxis.TickPosToMsPos(voicePart.position);
                            var skipMs = wavePart.GetSkipMs(project);
                            rmvpeResult.ApplyToPart(project, voicePart, wavePosMs - voicePosMs - skipMs);
                        }
                        DocManager.Inst.EndUndoGroup();
                    }
                } catch (Exception e) {
                    if (cancelled) {
                        return;
                    }
                    Log.Error(e, $"Failed to transcribe part {part.name}");
                    _ = MessageBox.ShowError(this, e);
                } finally {
                    if (msgbox != null) {
                        if (closedHandler != null) {
                            msgbox.Closed -= closedHandler;
                        }
                        msgbox.Close();
                    }
                }
            }
        }

        void MergePart(UPart part) {
            List<UPart> selectedParts = viewModel.TracksViewModel.SelectedParts;
            if (!selectedParts.All(p => p.trackNo.Equals(part.trackNo))) {
                _ = MessageBox.Show(
                    this,
                    ThemeManager.GetString("dialogs.merge.multitracks"),
                    ThemeManager.GetString("dialogs.merge.caption"),
                    MessageBox.MessageBoxButtons.Ok);
                return;
            }
            if (selectedParts.Count() <= 1) { return; }
            List<UVoicePart> voiceParts = [];
            foreach (UPart p in selectedParts) {
                if (p is UVoicePart vp) {
                    voiceParts.Add(vp);
                } else {
                    return;
                }
            }
            UVoicePart mergedPart = voiceParts.Aggregate((merging, nextup) => {
                string newComment = merging.comment + nextup.comment; // Not sure how comments are used
                var (leftPart, rightPart) = (merging.position < nextup.position) ? (merging, nextup) : (nextup, merging);
                int newPosition = leftPart.position;
                int newDuration = Math.Max(leftPart.End, rightPart.End) - newPosition;
                int deltaPos = rightPart.position - leftPart.position;
                UVoicePart shiftPart = new UVoicePart();
                foreach (var note in rightPart.notes) {
                    UNote shiftNote = note.Clone();
                    shiftNote.position += deltaPos;
                    shiftPart.notes.Add(shiftNote);
                }
                foreach (var curve in rightPart.curves) {
                    UCurve shiftCurve = curve.Clone();
                    for (var i = 0; i < shiftCurve.xs.Count; i++) {
                        shiftCurve.xs[i] += deltaPos;
                    }
                    shiftPart.curves.Add(shiftCurve);
                }
                SortedSet<UNote> newNotes = [.. leftPart.notes, .. shiftPart.notes];
                List<UCurve> newCurves = UCurve.MergeCurves(leftPart.curves, shiftPart.curves);
                return new UVoicePart() {
                    name = part.name,
                    comment = newComment,
                    trackNo = part.trackNo,
                    position = newPosition,
                    notes = newNotes,
                    curves = newCurves,
                    Duration = newDuration,
                };
            });
            ValidateOptions options = new ValidateOptions() {
                SkipTiming = true,
                Part = mergedPart,
                SkipPhoneme = false,
                SkipPhonemizer = false
            };
            mergedPart.Validate(options, DocManager.Inst.Project, DocManager.Inst.Project.tracks[part.trackNo]);
            DocManager.Inst.StartUndoGroup("command.part.edit");
            for (int i = selectedParts.Count - 1; i >= 0; i--) {
                // The index will shift by removing a part on each loop
                // Workaround by removing backwards from the largest index and going down
                DocManager.Inst.ExecuteCmd(new RemovePartCommand(DocManager.Inst.Project, selectedParts[i]));
            }
            DocManager.Inst.ExecuteCmd(new AddPartCommand(DocManager.Inst.Project, mergedPart));
            DocManager.Inst.EndUndoGroup();
        }
        async Task SplitPart(UPart part) {
            int tick = DocManager.Inst.playPosTick;
            if (part.position >= tick || part.End <= tick) return;
            if (part is not UVoicePart vp) return;
            var notesInTheWay = vp.notes.Where(n => (n.position < tick - vp.position) && (n.End > tick - vp.position));
            if (notesInTheWay.Any()) {
                var res = await MessageBox.Show(
                    this,
                    ThemeManager.GetString("dialogs.splitpart.intheway"),
                    ThemeManager.GetString("dialogs.splitpart.caption"),
                    MessageBox.MessageBoxButtons.YesNo);
                if (res == MessageBox.MessageBoxResult.No) { return; }
                do {
                    tick = vp.position + notesInTheWay.Max(n => n.End);
                    notesInTheWay = vp.notes.Where(n => (n.position < tick - vp.position) && (n.End > tick - vp.position));
                } while (notesInTheWay.Any());
            }

            static SortedSet<UNote> GetNotes(IEnumerable<UNote> notes, int relTick, bool after) => after
                ? [.. notes.Where(n => n.position >= relTick).Select(n => { var m = n.Clone(); m.position -= relTick; return m; })]
                : [.. notes.Where(n => n.position < relTick).Select(n => n.Clone())];
            static List<UCurve> GetCurves(IEnumerable<UCurve> curves, int relTick, bool after) =>
                curves.Select(c => {
                    var cloned = c.Clone();
                    var zipped = cloned.xs.Zip(cloned.ys, (x, y) => (x, y));
                    var filtered = after
                        ? zipped.Where(z => z.x >= relTick).Select(z => (x: z.x - relTick, z.y))
                        : zipped.Where(z => z.x < relTick);
                    cloned.xs = [.. filtered.Select(z => z.x)];
                    cloned.ys = [.. filtered.Select(z => z.y)];
                    return cloned;
                }).ToList();

            var notesAfter = GetNotes(vp.notes, tick - vp.position, after: true);
            var notesBefore = GetNotes(vp.notes, tick - vp.position, after: false);
            var curvesAfter = GetCurves(vp.curves, tick - vp.position, after: true);
            var curvesBefore = GetCurves(vp.curves, tick - vp.position, after: false);

            var firstPart = new UVoicePart {
                name = vp.name + "-1",
                comment = vp.comment,
                trackNo = vp.trackNo,
                position = vp.position,
                notes = notesBefore,
                curves = curvesBefore,
                Duration = tick - vp.position
            };
            var secondPart = new UVoicePart {
                name = vp.name + "-2",
                comment = vp.comment,
                trackNo = vp.trackNo,
                position = tick,
                notes = notesAfter,
                curves = curvesAfter,
                Duration = vp.End - tick
            };

            DocManager.Inst.StartUndoGroup();
            DocManager.Inst.ExecuteCmd(new RemovePartCommand(DocManager.Inst.Project, vp));
            DocManager.Inst.ExecuteCmd(new AddPartCommand(DocManager.Inst.Project, firstPart));
            DocManager.Inst.ExecuteCmd(new AddPartCommand(DocManager.Inst.Project, secondPart));
            DocManager.Inst.EndUndoGroup();
        }
        public async void OnWelcomeRecent(object sender, PointerPressedEventArgs args) {
            if (sender is StyledElement el &&
                el.DataContext is RecentFileInfo fileInfo) {
                if (!DocManager.Inst.ChangesSaved && !await AskIfSaveAndContinue()) {
                    return;
                }
                viewModel.OpenRecent(fileInfo.PathName);
            }
        }

        public async void OnWelcomeTemplate(object sender, PointerPressedEventArgs args) {
            if (sender is StyledElement el &&
                el.DataContext is RecentFileInfo fileInfo) {
                if (!DocManager.Inst.ChangesSaved && !await AskIfSaveAndContinue()) {
                    return;
                }
                viewModel.OpenTemplate(fileInfo.PathName);
            }
        }

        // ── Welcome page quick links ──────────────────────────────
        void OnWelcomeTeto(object sender, RoutedEventArgs args) {
            try { OS.OpenWeb("https://kasaneteto.jp/"); }
            catch (Exception e) { DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e)); }
        }
        void OnWelcomePlus(object sender, RoutedEventArgs args) {
            // Placeholder — official site coming soon.
        }

        async void ValidateTracksVoiceColor() {
            DocManager.Inst.StartUndoGroup("command.track.remapvc");
            foreach (var track in DocManager.Inst.Project.tracks) {
                if (track.ValidateVoiceColor(out var oldColors, out var newColors)) {
                    await VoiceColorRemappingAsync(track, oldColors, newColors);
                }
            }
            DocManager.Inst.EndUndoGroup();
        }
        async Task VoiceColorRemappingAsync(UTrack track, string[] oldColors, string[] newColors) {
            var parts = DocManager.Inst.Project.parts
                .Where(part => part.trackNo == track.TrackNo && part is UVoicePart)
                .Cast<UVoicePart>()
                .Where(vpart => vpart.notes.Count > 0);
            if (parts.Any()) {
                var dialog = new VoiceColorMappingDialog();
                VoiceColorMappingViewModel vm = new VoiceColorMappingViewModel(oldColors, newColors, track.TrackName);
                dialog.DataContext = vm;
                await dialog.ShowDialog(this);

                if (dialog.Apply) {
                    SetVoiceColorRemapping(track, parts, vm);
                }
            }
        }
        void VoiceColorRemapping(UTrack track, string[] oldColors, string[] newColors, bool manually = false) {
            var parts = DocManager.Inst.Project.parts
                .Where(part => part.trackNo == track.TrackNo && part is UVoicePart)
                .Cast<UVoicePart>()
                .Where(vpart => vpart.notes.Count > 0);
            if (parts.Any()) {
                var dialog = new VoiceColorMappingDialog();
                VoiceColorMappingViewModel vm = new VoiceColorMappingViewModel(oldColors, newColors, track.TrackName);
                dialog.DataContext = vm;
                dialog.onFinish = () => {
                    DocManager.Inst.StartUndoGroup("command.track.remapvc");
                    SetVoiceColorRemapping(track, parts, vm);
                    DocManager.Inst.EndUndoGroup();
                };
                dialog.ShowDialog(this);
            } else if (manually) {
                MessageBox.Show(this, ThemeManager.GetString("lyrics.nonote"), ThemeManager.GetString("errors.caption"), MessageBox.MessageBoxButtons.Ok);
            }
        }
        void SetVoiceColorRemapping(UTrack track, IEnumerable<UVoicePart> parts, VoiceColorMappingViewModel vm) {
            foreach (var part in parts) {
                foreach (var phoneme in part.phonemes) {
                    var tuple = phoneme.GetExpression(DocManager.Inst.Project, track, Ustx.CLR);
                    if (vm.ColorMappings.Any(m => m.OldIndex == tuple.Item1)) {
                        var mapping = vm.ColorMappings.First(m => m.OldIndex == tuple.Item1);
                        if (mapping.OldIndex != mapping.SelectedIndex) {
                            if (mapping.SelectedIndex == 0) {
                                DocManager.Inst.ExecuteCmd(new SetPhonemeExpressionCommand(DocManager.Inst.Project, track, part, phoneme, Ustx.CLR, null));
                            } else {
                                DocManager.Inst.ExecuteCmd(new SetPhonemeExpressionCommand(DocManager.Inst.Project, track, part, phoneme, Ustx.CLR, mapping.SelectedIndex));
                            }
                        }
                    } else {
                        DocManager.Inst.ExecuteCmd(new SetPhonemeExpressionCommand(DocManager.Inst.Project, track, part, phoneme, Ustx.CLR, null));
                    }
                }
            }
        }

        public void WindowClosing(object? sender, WindowClosingEventArgs e) {
            if (forceClose || DocManager.Inst.ChangesSaved) {
                if (Preferences.Default.ClearCacheOnQuit) {
                    Log.Information("Clearing cache...");
                    PathManager.Inst.ClearCache();
                    Log.Information("Cache cleared.");
                }
                // 关闭前停播（上游 e34dbb43）：否则退出过程中音频回调仍在消费信号链，
                // 缓存目录刚被清空 / 延迟销毁的 VST handle 可能正被读取 → 退出期崩溃。
                PlaybackManager.Inst.StopPlayback();
                ShutdownDetachedViews();
                Preferences.Default.MainWindowSize.Set(Width, Height, Position.X, Position.Y, (int)WindowState);
                Preferences.Default.RecoveryPath = string.Empty;
                Preferences.Save();
                return;
            }
            e.Cancel = true;
            AskIfSaveAndContinue().ContinueWith(t => {
                if (!t.Result) {
                    return;
                }
                // 分离窗口的收尾统一在 ShutdownDetachedViews（forceClose 分支）里做
                forceClose = true;
                Close();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>
        /// 退出期收尾：分离窗口走「释放」路径（控件不回收、不改偏好、不触发收回归位），
        /// 混音台控件在这里彻底 Shutdown —— VU 定时器 + DocManager 订阅的归宿（挂载期一律不动它）。
        /// </summary>
        private void ShutdownDetachedViews() {
            if (pianoRollWindow != null) {
                var window = pianoRollWindow;
                pianoRollWindow = null;
                window.ReturnToHost = null;
                window.ReleaseControl();
            }
            if (mixerWindow != null) {
                var window = mixerWindow;
                mixerWindow = null;
                window.ReturnToHost = null;
                window.ReleaseControl();
            }
            mixerControl?.Shutdown();
        }

        private async Task<bool> AskIfSaveAndContinue() {
            var result = await ShowExitConfirmOverlayAsync();
            switch (result) {
                case MessageBox.MessageBoxResult.Yes:
                    try {
                        await Save();
                    } catch (Exception e) {
                        Log.Error(e, "Failed to save on exit.");
                        await MessageBox.ShowError(this, e);
                    }
                    // Save cancelled or failed (e.g. dismissed the Save As picker) —
                    // stay in the app instead of exiting.
                    return viewModel.ProjectSaved;
                case MessageBox.MessageBoxResult.No:
                    return true; // Continue.
                default:
                    return false; // Cancel.
            }
        }

        // ── In-window modal overlay host ─────────────────────────
        // Shows arbitrary content (exit-save confirm, Preferences, Updater)
        // inside a rounded card, instead of a separate window.
        private TaskCompletionSource<MessageBox.MessageBoxResult>? overlayTcs;

        private void ShowOverlayContent(Control content, bool closable = true, double sizeFraction = 0, string? headerTitle = null) {
            OverlayContent.Content = content;
            overlaySizeFraction = sizeFraction;
            if (headerTitle != null) {
                // Dialog-style overlay: title + close live in the header bar.
                OverlayHeader.IsVisible = true;
                OverlayHeaderTitle.Text = headerTitle;
                OverlayCloseButton.IsVisible = false;
            } else {
                OverlayHeader.IsVisible = false;
                OverlayCloseButton.IsVisible = closable;
            }
            UpdateOverlayCardSize();
            OverlayLayer.IsVisible = true;
            overlayGeneration++;
            // 覆盖层：主内容模糊 + 卡片弹出（.md3-pop）+ 遮罩淡入（.md3-fade）
            MainGrid.Effect = new Avalonia.Media.BlurEffect { Radius = 24 };
            SetShown(OverlayCard, true);
            SetShown(OverlayBackdrop, true);
        }

        internal void CloseOverlay() {
            if (!OverlayLayer.IsVisible) {
                return;
            }
            if (OverlayContent.Content is UpdaterDialog updater) {
                updater.OnClosed();
            }
            overlayTcs?.TrySetResult(MessageBox.MessageBoxResult.Cancel);
            overlayTcs = null;
            // 卡片退场后再真正隐藏（代际号防止退场途中又开了新覆盖层被误关）
            int generation = ++overlayGeneration;
            // 过渡由样式层（Transitions）插值：先切 shown=false，等过渡走完再真正隐藏
            SetShown(OverlayCard, false);
            SetShown(OverlayBackdrop, false);
            if (TransitionMs == 0) {
                HideOverlayNow();
                return;
            }
            DispatcherTimer.RunOnce(() => {
                if (generation == overlayGeneration) {
                    HideOverlayNow();
                }
            }, TimeSpan.FromMilliseconds(TransitionMs), DispatcherPriority.Background);
        }

        private void HideOverlayNow() {
            OverlayLayer.IsVisible = false;
            MainGrid.Effect = null;
            OverlayContent.Content = null;
            // 复位过渡状态：下次打开时重新播放弹入（元素此刻不可见，复位不会造成闪烁）
            SetShown(OverlayCard, false);
            SetShown(OverlayBackdrop, false);
        }

        private void OnOverlayCloseClicked(object? sender, RoutedEventArgs e) {
            CloseOverlay();
        }

        private double overlaySizeFraction;
        private int overlayGeneration;

        private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e) {
            if (OverlayLayer.IsVisible) {
                UpdateOverlayCardSize();
            }
        }

        /// <summary>
        /// Resizes the overlay card to a fraction of the host window
        /// (e.g. 0.35 = 35%). When fraction is 0 the card sizes to its content.
        /// </summary>
        private void UpdateOverlayCardSize() {
            if (overlaySizeFraction > 0) {
                double w = Bounds.Width * overlaySizeFraction;
                double h = Bounds.Height * overlaySizeFraction;
                OverlayCard.Width = w;
                OverlayCard.Height = h;
            } else {
                OverlayCard.Width = double.NaN;
                OverlayCard.Height = double.NaN;
            }
        }

        private Task<MessageBox.MessageBoxResult> ShowExitConfirmOverlayAsync() {
            var title = new TextBlock {
                Text = ThemeManager.GetString("dialogs.exitsave.caption"),
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
            };
            var message = new TextBlock {
                Text = ThemeManager.GetString("dialogs.exitsave.message"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Opacity = 0.85,
                LineHeight = 20,
                MaxWidth = 400,
            };
            var buttons = new StackPanel {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new Thickness(0, 12, 0, 0),
            };

            void AddButton(string caption, MessageBox.MessageBoxResult result) {
                var btn = new Button { Content = caption };
                btn.Click += (_, _) => CompleteOverlay(result);
                buttons.Children.Add(btn);
            }

            AddButton(ThemeManager.GetString("button.yes"), MessageBox.MessageBoxResult.Yes);
            AddButton(ThemeManager.GetString("button.no"), MessageBox.MessageBoxResult.No);
            AddButton(ThemeManager.GetString("button.cancel"), MessageBox.MessageBoxResult.Cancel);

            // 内容自适应卡片（不写死 20% 窗口比例——窗口缩放时自动重排）
            var panel = new StackPanel {
                Spacing = 14,
                Margin = new Thickness(28, 24),
            };
            panel.Children.Add(title);
            panel.Children.Add(message);
            panel.Children.Add(buttons);

            overlayTcs = new TaskCompletionSource<MessageBox.MessageBoxResult>();
            ShowOverlayContent(panel, closable: false, sizeFraction: 0);
            return overlayTcs.Task;
        }

        private void CompleteOverlay(MessageBox.MessageBoxResult result) {
            if (!OverlayLayer.IsVisible) {
                return;
            }
            OverlayLayer.IsVisible = false;
            MainGrid.Effect = null;
            OverlayContent.Content = null;
            overlayTcs?.TrySetResult(result);
            overlayTcs = null;
        }

        private void OnOverlayBackdropPressed(object? sender, PointerPressedEventArgs e) {
            // Only the exit-save confirm is dismissed by clicking the backdrop.
            // Hosted dialogs (Preferences/Updater) close via their own buttons.
            if (OverlayContent.Content is StackPanel) {
                CompleteOverlay(MessageBox.MessageBoxResult.Cancel);
            }
        }

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is ErrorMessageNotification notif) {
                switch (notif.e) {
                    case Core.Render.NoResamplerException:
                    case Core.Render.NoWavtoolException:
                        MessageBox.Show(
                           this,
                           ThemeManager.GetString("dialogs.noresampler.message"),
                           ThemeManager.GetString("dialogs.noresampler.caption"),
                           MessageBox.MessageBoxButtons.Ok);
                        break;
                    default:
                        MessageBox.ShowError(this, notif.e, notif.message, true);
                        break;
                }
            } else if (cmd is VoiceColorRemappingNotification voicecolorNotif) {
                if (voicecolorNotif.TrackNo < 0 || DocManager.Inst.Project.tracks.Count <= voicecolorNotif.TrackNo) {
                    // Verify whether remapping is required when the voice color lineup changes
                    ValidateTracksVoiceColor();
                } else {
                    UTrack track = DocManager.Inst.Project.tracks[voicecolorNotif.TrackNo];
                    if (!voicecolorNotif.Validate) {
                        // When the user intentionally invokes remapping
                        if (track.VoiceColorExp.options.Length == 0) {
                            MessageBox.Show(this, ThemeManager.GetString("dialogs.voicecolorremapping.error"), ThemeManager.GetString("errors.caption"), MessageBox.MessageBoxButtons.Ok);
                        } else {
                            VoiceColorRemapping(track, track.VoiceColorNames, track.VoiceColorExp.options, true);
                        }
                    } else if (track.ValidateVoiceColor(out var oldColors, out var newColors)) { // Verify whether remapping is required when the singer is changed
                        VoiceColorRemapping(track, oldColors, newColors);
                    }
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Threading;
using DynamicData.Binding;
using OpenUtau.Api;
using OpenUtau.App.Views;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtau.Core.Vst;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace OpenUtau.App.ViewModels {
    public class PartsContextMenuArgs {
        public UPart? Part { get; set; }
        public bool IsVoicePart => Part is UVoicePart;
        public bool IsWavePart => Part is UWavePart;
        public ReactiveCommand<UPart, Unit>? PartDeleteCommand { get; set; }
        public ReactiveCommand<UPart, Unit>? PartRenameCommand { get; set; }
        public ReactiveCommand<UPart, Unit>? PartGotoFileCommand { get; set; }
        public ReactiveCommand<UPart, Unit>? PartReplaceAudioCommand { get; set; }
        public ReactiveCommand<UPart, Unit>? PartTranscribeCommand { get; set; }
        public ReactiveCommand<UPart, Unit>? PartMergeCommand { get; set; }
        public ReactiveCommand<UPart, Unit>? PartSplitCommand { get; set; }
        public IEnumerable<MenuItemViewModel> PartApplyPitchMenuItems { get; set; } = new List<MenuItemViewModel>();
    }

    public class RecentFileInfo {
        public string Name { get; }
        public string PathName { get; }
        public string Directory { get; }
        public DateTime LastWriteTime { get; }
        public string LastWriteTimeStr { get; }
        public string Format { get; }

        public RecentFileInfo(string path) {
            PathName = path;
            Name = Path.GetFileName(path);
            Directory = Path.GetDirectoryName(path) ?? string.Empty;
            LastWriteTime = File.GetLastWriteTime(path);
            LastWriteTimeStr = LastWriteTime.ToString("MM-dd HH:mm");
            Format = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        }
    }

    /// <summary>
    /// W16 面板系统：一个可调宽 / 可折叠面板的状态（**所有面板共用的同一套语义**）。
    ///
    /// - <see cref="Width"/>：展开时的宽度，XAML 绑面板容器 `Width`，并绑到 `PanelSplitter.Target`（TwoWay）。
    /// - <see cref="IsCollapsed"/>：折叠态。折叠 = 面板容器 `IsVisible=false` + 列宽 0（不留空白、不留窄条）。
    /// - <see cref="DefaultWidth"/>/<see cref="MinWidth"/>/<see cref="MaxWidth"/>：设计边界，双击分隔条复位到默认。
    /// - <see cref="Reset"/>：宽度回默认 + 展开（"重置布局"入口调用）。
    /// 落盘由 MainWindowViewModel 统一做（订阅 PropertyChanged，映射到 Preferences.Default.PanelLayout）。
    /// </summary>
    public sealed class PanelSlot : ViewModelBase {
        private readonly string key;

        public PanelSlot(string key, double defaultWidth, double minWidth, double maxWidth) {
            this.key = key;
            DefaultWidth = defaultWidth;
            MinWidth = minWidth;
            MaxWidth = maxWidth;
            width = defaultWidth;
        }

        /// <summary>面板标识（诊断/持久化字段映射用）。</summary>
        public string Key => key;

        public double DefaultWidth { get; set; }
        public double MinWidth { get; set; }
        public double MaxWidth { get; set; }

        private double width;
        public double Width {
            get => width;
            set => this.RaiseAndSetIfChanged(ref width, Math.Clamp(value, MinWidth, MaxWidth));
        }

        private bool isCollapsed;
        public bool IsCollapsed {
            get => isCollapsed;
            set => this.RaiseAndSetIfChanged(ref isCollapsed, value);
        }

        /// <summary>折叠时占用 0（"不留空白"）；展开时就是 <see cref="Width"/>。</summary>
        public double EffectiveWidth => isCollapsed ? 0 : width;

        /// <summary>折叠/展开（面板头部 chevron 用）。</summary>
        public void ToggleCollapse() => IsCollapsed = !IsCollapsed;

        /// <summary>宽度回默认 + 展开。</summary>
        public void Reset() {
            Width = DefaultWidth;
            IsCollapsed = false;
        }
    }

    public class MainWindowViewModel : ViewModelBase, ICmdSubscriber {
        public string Title => !ProjectSaved
            ? $"{AppVersion}"
            : $"{(DocManager.Inst.ChangesSaved ? "" : "*")}{AppVersion} [{DocManager.Inst.Project.FilePath}]";
        public double Width => Preferences.Default.MainWindowSize.Width;
        public double Height => Preferences.Default.MainWindowSize.Height;

        // 阶段 E3：欢迎页独立成 WelcomeWindow，Page（Carousel 索引）移除
        public ObservableCollectionExtended<RecentFileInfo> RecentFiles { get; } = new ObservableCollectionExtended<RecentFileInfo>();
        // 模板列表：欢迎页「模板」卡片的下拉要绑定它——公开是绑定可见性的保证（Avalonia 按公开属性解析绑定）
        public ObservableCollectionExtended<RecentFileInfo> TemplateFiles { get; } = new ObservableCollectionExtended<RecentFileInfo>();
        [Reactive] public bool HasRecovery { get; set; } = false;
        [Reactive] public string RecoveryPath { get; set; } = String.Empty;
        [Reactive] public string RecoveryString { get; set; } = String.Empty;

        [Reactive] public PlaybackViewModel PlaybackViewModel { get; set; }
        [Reactive] public TracksViewModel TracksViewModel { get; set; }
        [Reactive] public ReactiveCommand<string, Unit>? OpenRecentCommand { get; private set; }
        [Reactive] public ReactiveCommand<string, Unit>? OpenTemplateCommand { get; private set; }
        public ObservableCollectionExtended<MenuItemViewModel> OpenRecentMenuItems => openRecentMenuItems;
        public ObservableCollectionExtended<MenuItemViewModel> OpenTemplatesMenuItems => openTemplatesMenuItems;
        public ObservableCollectionExtended<MenuItemViewModel> TimelineContextMenuItems { get; }
            = new ObservableCollectionExtended<MenuItemViewModel>();

        [Reactive] public string ClearCacheHeader { get; set; }
        public bool ProjectSaved => !string.IsNullOrEmpty(DocManager.Inst.Project.FilePath) && DocManager.Inst.Project.Saved;
        // 版本号规则：UTvTU v{上游主线版本} p{Plus版本}（格式串唯一来源：Core.PlusInfo）
        public string AppVersion => Core.PlusInfo.VersionString;
        [Reactive] public bool IsDarkMode { get; set; }
        [Reactive] public double Progress { get; set; }
        [Reactive] public string ProgressText { get; set; }
        /// <summary>
        /// S5 视图化：工作区三视图（工作台 / 钢琴卷帘 / 混音台）的切换状态。
        /// 单一真值 <see cref="ViewSwitcherState.CurrentView"/>，三个 <c>Show*</c> 只翻可见性；
        /// 卷帘 / 混音台不再有「停靠行」的高度（旧 MixerMinHeight / PianoRollMinHeight 已退役）。
        /// </summary>
        public ViewSwitcherState ViewSwitcher { get; } = new ViewSwitcherState();

        // ── W16 面板系统（可调宽 / 可折叠 / 持久化）────────────────────────────
        // 一个面板 = 一个 PanelSlot（宽 + 折叠态）+ 一条 PanelSplitter；状态统一写
        // Preferences.Default.PanelLayout（不新增平行存储），重启恢复。
        /// <summary>工作台左列：轨道头。默认 248 / min 200 / max 420（理由见 PanelLayoutPreferences）。</summary>
        public PanelSlot TracksPanel { get; } = new PanelSlot("track-header", 248, 200, 420);
        /// <summary>工作台右列：素材库（工作台与混音台都可见，见 D9）。默认 272 / min 220 / max 480。</summary>
        public PanelSlot LibraryPanel { get; } = new PanelSlot("library", 272, 220, 480);
        /// <summary>左列分隔条可见性（视图可见 + 面板未折叠）；顶栏「布局」flyout 与折叠按钮共用。</summary>
        public bool TracksPanelVisible => ViewSwitcher.ShowWorkspace && !TracksPanel.IsCollapsed;
        /// <summary>右列分隔条可见性（工作台/混音台可见 + 面板未折叠）。</summary>
        public bool LibraryPanelVisible => ViewSwitcher.ShowLibrary && !LibraryPanel.IsCollapsed;
        /// <summary>
        /// 折叠处「快捷展开」边缘标签（W34）：**该视图下且面板确实被折叠**时显示。
        /// 注意不能用 `!PanelShown` 单独判断：换到混音台视图时轨头分隔条整条隐藏、`PanelShown` 也是 false，
        /// 那样标签会在不该出现的视图里冒出来。所以这里显式带上视图可见性。
        /// </summary>
        public bool ShowTracksRevealTab => ViewSwitcher.ShowWorkspace && TracksPanel.IsCollapsed;
        /// <summary>素材库那侧的快捷展开标签（工作台/混音台都在）。</summary>
        public bool ShowLibraryRevealTab => ViewSwitcher.ShowLibrary && LibraryPanel.IsCollapsed;
        /// <summary>是否显示轨头列（顶栏 / 工具菜单的可勾选项，= 未折叠）。</summary>
        public bool ShowTracksPanel {
            get => !TracksPanel.IsCollapsed;
            set => TracksPanel.IsCollapsed = !value;
        }
        /// <summary>是否显示素材库列（顶栏 / 工具菜单的可勾选项，= 未折叠）。</summary>
        public bool ShowLibraryPanel {
            get => !LibraryPanel.IsCollapsed;
            set => LibraryPanel.IsCollapsed = !value;
        }
        /// <summary>重置面板布局（宽度回默认 + 全部展开），并落盘。</summary>
        public ReactiveCommand<Unit, Unit>? ResetPanelLayoutCommand { get; private set; }

        /// <summary>
        /// W4（决策 B6）：素材库「效果器」页签 = 插件浏览器。
        /// 扫描结果 + 搜索过滤 + 插件扫描路径（与「偏好设置 → VST」共用同一份 Preferences 字段）。
        /// </summary>
        public PluginBrowserViewModel PluginBrowser { get; } = new PluginBrowserViewModel();
        public ReactiveCommand<UPart, Unit> PartDeleteCommand { get; set; }
        public ReactiveCommand<int, Unit>? AddTempoChangeCmd { get; set; }
        public ReactiveCommand<int, Unit>? DelTempoChangeCmd { get; set; }
        public ReactiveCommand<int, Unit>? AddTimeSigChangeCmd { get; set; }
        public ReactiveCommand<int, Unit>? DelTimeSigChangeCmd { get; set; }
        [Reactive] public bool CanUndo { get; set; } = false;
        [Reactive] public bool CanRedo { get; set; } = false;
        [Reactive] public string UndoText { get; set; } = ThemeManager.GetString("menu.edit.undo");
        [Reactive] public string RedoText { get; set; } = ThemeManager.GetString("menu.edit.redo");

        private ObservableCollectionExtended<MenuItemViewModel> openRecentMenuItems
            = new ObservableCollectionExtended<MenuItemViewModel>();
        private ObservableCollectionExtended<MenuItemViewModel> openTemplatesMenuItems
            = new ObservableCollectionExtended<MenuItemViewModel>();

        // view will set this to the real AskIfSaveAndContinue implementation
        public Func<Task<bool>>? AskIfSaveAndContinue { get; set; }

        public MainWindowViewModel() {
            IsDarkMode = ThemeManager.IsDarkMode;
            MessageBus.Current.Listen<ThemeChangedEvent>()
                .Subscribe(_ => { IsDarkMode = ThemeManager.IsDarkMode; });
            PlaybackViewModel = new PlaybackViewModel();
            TracksViewModel = new TracksViewModel();
            ClearCacheHeader = string.Empty;
            ProgressText = string.Empty;
            RecentFiles.Clear();
            RecentFiles.AddRange(Preferences.Default.RecentFiles
                .Select(file => new RecentFileInfo(file))
                .OrderByDescending(f => f.LastWriteTime));
            TemplateFiles.Clear();
            Directory.CreateDirectory(PathManager.Inst.TemplatesPath);
            var templates = Directory.GetFiles(PathManager.Inst.TemplatesPath, "*.ustxp")
                .Concat(Directory.GetFiles(PathManager.Inst.TemplatesPath, "*.ustx"));
            TemplateFiles.AddRange(templates
                .Select(file => new RecentFileInfo(file)));

            // create async commands that consult the view's save prompt
            OpenRecentCommand = ReactiveCommand.CreateFromTask<string>(async file => {
                if (!DocManager.Inst.ChangesSaved && AskIfSaveAndContinue != null) {
                    if (!await AskIfSaveAndContinue()) return;
                }
                OpenRecent(file);
            });

            OpenTemplateCommand = ReactiveCommand.CreateFromTask<string>(async file => {
                if (!DocManager.Inst.ChangesSaved && AskIfSaveAndContinue != null) {
                    if (!await AskIfSaveAndContinue()) return;
                }
                OpenTemplate(file);
            });

            PartDeleteCommand = ReactiveCommand.Create<UPart>(part => {
                TracksViewModel.DeleteSelectedParts();
            });
            InitPanelLayout();
            DocManager.Inst.AddSubscriber(this);
        }

        // ── W16 面板系统：状态装载 / 映射 / 落盘 ─────────────────────────────

        /// <summary>从 Preferences 装载两个面板的宽与折叠态，并订阅其变化（唯一的映射点）。</summary>
        private void InitPanelLayout() {
            TracksPanel.DefaultWidth = 248; TracksPanel.MinWidth = 200; TracksPanel.MaxWidth = 420;
            LibraryPanel.DefaultWidth = 272; LibraryPanel.MinWidth = 220; LibraryPanel.MaxWidth = 480;
            LoadPanelLayout(Preferences.Default.PanelLayout, TracksPanel, LibraryPanel);

            TracksPanel.PropertyChanged += (_, args) => OnPanelChanged(args.PropertyName, persist: false);
            LibraryPanel.PropertyChanged += (_, args) => OnPanelChanged(args.PropertyName, persist: false);
            ViewSwitcher.PropertyChanged += (_, args) => {
                if (args.PropertyName == nameof(ViewSwitcherState.CurrentView)) {
                    RaisePanelFlags();
                }
            };
            ResetPanelLayoutCommand = ReactiveCommand.Create(ResetPanelLayout);
            RaisePanelFlags();
        }

        /// <summary>Preferences → 面板槽（静态可测：两个面板的映射只有这一处）。</summary>
        internal static void LoadPanelLayout(Preferences.PanelLayoutPreferences prefs, PanelSlot tracks, PanelSlot library) {
            tracks.Width = prefs.TrackHeaderWidth;
            tracks.IsCollapsed = prefs.TrackHeaderCollapsed;
            library.Width = prefs.LibraryWidth;
            library.IsCollapsed = prefs.LibraryCollapsed;
        }

        /// <summary>面板槽 → Preferences 的内存对象（静态可测；不落盘 —— 落盘只发生在拖动结束/折叠/复位）。</summary>
        internal static void SavePanelLayout(Preferences.PanelLayoutPreferences prefs, PanelSlot tracks, PanelSlot library) {
            prefs.TrackHeaderWidth = tracks.Width;
            prefs.TrackHeaderCollapsed = tracks.IsCollapsed;
            prefs.LibraryWidth = library.Width;
            prefs.LibraryCollapsed = library.IsCollapsed;
        }

        private void OnPanelChanged(string? propertyName, bool persist) {
            ApplyPanelLayoutToPreferences();
            if (persist || propertyName == nameof(PanelSlot.IsCollapsed)) {
                PersistPanelLayout();     // 折叠态是单次动作，直接落盘；宽度拖动中只更新内存
            }
            if (propertyName == nameof(PanelSlot.IsCollapsed)) {
                RaisePanelFlags();
            }
        }

        /// <summary>把当前面板状态写进 Preferences 的内存对象（不落盘）。</summary>
        private void ApplyPanelLayoutToPreferences() {
            SavePanelLayout(Preferences.Default.PanelLayout, TracksPanel, LibraryPanel);
        }

        /// <summary>拖动结束 / 折叠 / 复位时落盘（拖动过程中不写文件）。</summary>
        public void PersistPanelLayout() {
            ApplyPanelLayoutToPreferences();
            Preferences.Save();
        }

        /// <summary>重置面板布局：宽度回默认、全部展开，并落盘。</summary>
        public void ResetPanelLayout() {
            TracksPanel.Reset();
            LibraryPanel.Reset();
            RaisePanelFlags();
            PersistPanelLayout();
        }

        /// <summary>面板可见性派生属性统一通知（折叠态或视图变化时）。</summary>
        public void RaisePanelFlags() {
            this.RaisePropertyChanged(nameof(TracksPanelVisible));
            this.RaisePropertyChanged(nameof(LibraryPanelVisible));
            this.RaisePropertyChanged(nameof(ShowTracksPanel));
            this.RaisePropertyChanged(nameof(ShowLibraryPanel));
            this.RaisePropertyChanged(nameof(ShowTracksRevealTab));
            this.RaisePropertyChanged(nameof(ShowLibraryRevealTab));
        }

        /// <summary>折叠处「快捷展开」：把面板展开回**持久化宽度**（不是强制默认宽），并落盘。</summary>
        public void RevealTracksPanel() {
            TracksPanel.IsCollapsed = false;
            RaisePanelFlags();
            PersistPanelLayout();
        }

        /// <summary>素材库那侧的快捷展开（同上）。</summary>
        public void RevealLibraryPanel() {
            LibraryPanel.IsCollapsed = false;
            RaisePanelFlags();
            PersistPanelLayout();
        }

        public void Undo() {
            DocManager.Inst.Undo();
        }
        public void Redo() {
            DocManager.Inst.Redo();
        }
        private void SetUndoState() {
            CanUndo = DocManager.Inst.GetUndoState(out string? undoNameKey);
            if (!string.IsNullOrWhiteSpace(undoNameKey)) {
                UndoText = $"{ThemeManager.GetString("menu.edit.undo")}: {ThemeManager.GetString(undoNameKey)}";
            } else {
                UndoText = ThemeManager.GetString("menu.edit.undo");
            }
            CanRedo = DocManager.Inst.GetRedoState(out string? redoNameKey);
            if (!string.IsNullOrWhiteSpace(redoNameKey)) {
                RedoText = $"{ThemeManager.GetString("menu.edit.redo")}:  {ThemeManager.GetString(redoNameKey)}";
            } else {
                RedoText = ThemeManager.GetString("menu.edit.redo");
            }
        }

        /// <summary>
        /// 阶段 E3：欢迎窗阶段初始化——只设置恢复状态（WelcomeWindow 显示恢复条）。
        /// 命令行动作分支移至 WelcomeWindow（打开工程由它创建 MainWindow 后触发）。
        /// </summary>
        public void InitProject() {
            var recPath = Preferences.Default.RecoveryPath;
            if (!string.IsNullOrWhiteSpace(recPath) && File.Exists(recPath)) {
                RecoveryPath = recPath;
                RecoveryString = ThemeManager.GetString("dialogs.recovery") + "\n" + recPath;
                HasRecovery = true;
            }
        }

        public void NewProject() {
            var defaultTemplate = Path.Combine(PathManager.Inst.TemplatesPath, "default.ustxp");
            if (!File.Exists(defaultTemplate)) {
                defaultTemplate = Path.Combine(PathManager.Inst.TemplatesPath, "default.ustx");
            }
            if (File.Exists(defaultTemplate)) {
                try {
                    OpenProject(new[] { defaultTemplate });
                    DocManager.Inst.Project.Saved = false;
                    DocManager.Inst.Project.FilePath = string.Empty;
                    this.RaisePropertyChanged(nameof(Title));
                    return;
                } catch (Exception e) {
                    var customEx = new MessageCustomizableException("Failed to load default template", "<translate:errors.failed.load>: default template", e);
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
                }
            }
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(Core.Format.Ustx.Create()));
            DocManager.Inst.Recovered = false;
        }



        public void OpenProject(string[] files) {
            if (files == null) {
                return;
            }
            DocManager.Inst.ExecuteCmd(new LoadingNotification(typeof(MainWindow), true, "project"));
            try {

                Core.Format.Formats.LoadProject(files);
                DocManager.Inst.ExecuteCmd(new VoiceColorRemappingNotification(-1, true));
                this.RaisePropertyChanged(nameof(Title));
            } finally {
                DocManager.Inst.ExecuteCmd(new LoadingNotification(typeof(MainWindow), false, "project"));
            }
            DocManager.Inst.Recovered = false;
        }

        public void OpenRecent(string file) {
            try {
                OpenProject(new string[] { file });
            } catch (Exception e) {
                var customEx = new MessageCustomizableException("Failed to open recent", "<translate:errors.failed.openfile>: recent project", e);
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
            }
        }

        public void OpenTemplate(string file) {
            try {
                OpenProject(new string[] { file });
                DocManager.Inst.Project.Saved = false;
                DocManager.Inst.Project.FilePath = string.Empty;
                this.RaisePropertyChanged(nameof(Title));
            } catch (Exception e) {
                var customEx = new MessageCustomizableException("Failed to open template", "<translate:errors.failed.openfile>: project template", e);
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
            }
        }

        public void SaveProject(string file = "") {
            if (file == null) {
                return;
            }
            DocManager.Inst.ExecuteCmd(new SaveProjectNotification(file));
            this.RaisePropertyChanged(nameof(Title));
        }

        public void ImportTracks(UProject[] loadedProjects, bool importTempo){
            if (loadedProjects == null || loadedProjects.Length < 1) {
                return;
            }
            Core.Format.Formats.ImportTracks(DocManager.Inst.Project, loadedProjects, importTempo);
        }

        public void ImportTracks(string[] files, bool importTempo) {
            if (files == null) {
                return;
            }
            Core.Format.Formats.ImportTracks(DocManager.Inst.Project, files, importTempo);
        }

        public void ImportAudio(string file) {
            if (file == null) {
                return;
            }
            var project = DocManager.Inst.Project;
            UWavePart part = new UWavePart() {
                FilePath = file,
            };
            part.Load(project);
            if (part == null) {
                return;
            }
            int trackNo = project.tracks.Count;
            part.trackNo = trackNo;
            DocManager.Inst.StartUndoGroup("command.import.audio");
            DocManager.Inst.ExecuteCmd(new AddTrackCommand(project, new UTrack(project) { TrackNo = trackNo }));
            DocManager.Inst.ExecuteCmd(new AddPartCommand(project, part));
            DocManager.Inst.EndUndoGroup();
        }

        /// <summary>
        /// 侧栏素材库（E4）：新建轨道 + 添加歌手。命令链（可撤销）——
        /// 建轨 → 设歌手 → 音素器（用户钉住项 → 歌手默认）→ 渲染设置。
        /// 参照 TrackHeaderViewModel.ApplySingerToTrack。
        /// </summary>
        public void AddSingerTrack(USinger singer) {
            if (singer == null) {
                return;
            }
            var project = DocManager.Inst.Project;
            int trackNo = project.tracks.Count;
            var track = new UTrack(project) { TrackNo = trackNo };
            DocManager.Inst.StartUndoGroup("command.track.add");
            DocManager.Inst.ExecuteCmd(new AddTrackCommand(project, track));
            DocManager.Inst.ExecuteCmd(new TrackChangeSingerCommand(project, track, singer));
            if (!string.IsNullOrEmpty(singer.Id) &&
                Preferences.Default.SingerPhonemizers.TryGetValue(singer.Id, out var phonemizerName)) {
                TryChangePhonemizer(track, phonemizerName);
            } else if (!string.IsNullOrEmpty(singer.DefaultPhonemizer)) {
                TryChangePhonemizer(track, singer.DefaultPhonemizer);
            }
            if (!singer.Found || singer.SingerType != track.RendererSettings.Renderer?.SingerType) {
                var settings = new URenderSettings();
                if (singer.Found) {
                    settings = new URenderSettings {
                        renderer = Core.Render.Renderers.GetDefaultRenderer(singer.SingerType),
                    };
                }
                DocManager.Inst.ExecuteCmd(new TrackChangeRenderSettingCommand(project, track, settings));
            }
            DocManager.Inst.EndUndoGroup();
        }

        private bool TryChangePhonemizer(UTrack track, string phonemizerName) {
            try {
                var factory = PhonemizerFactory.Get(phonemizerName);
                var phonemizer = factory?.Create();
                if (phonemizer != null) {
                    DocManager.Inst.ExecuteCmd(new TrackChangePhonemizerCommand(DocManager.Inst.Project, track, phonemizer));
                    return true;
                }
            } catch (Exception e) {
                Log.Error(e, $"Failed to load phonemizer {phonemizerName}");
            }
            return false;
        }

        public void ImportMidi(string file) {
            if (file == null) {
                return;
            }
            var project = DocManager.Inst.Project;
            var parts = Core.Format.MidiWriter.Load(file, project);
            DocManager.Inst.StartUndoGroup("command.import.track");
            foreach (var part in parts) {
                var track = new UTrack(project);
                track.TrackNo = project.tracks.Count;
                part.trackNo = track.TrackNo;
                if(part.name != "New Part"){
                    track.TrackName = part.name;
                }
                part.AfterLoad(project, track);
                DocManager.Inst.ExecuteCmd(new AddTrackCommand(project, track));
                DocManager.Inst.ExecuteCmd(new AddPartCommand(project, part));
            }
            DocManager.Inst.EndUndoGroup();
        }

        public void RefreshOpenRecent() {
            openRecentMenuItems.Clear();
            openRecentMenuItems.AddRange(Preferences.Default.RecentFiles.Select(file => new MenuItemViewModel() {
                Header = file,
                Command = OpenRecentCommand,
                CommandParameter = file,
            }));
        }

        public void RefreshTemplates() {
            Directory.CreateDirectory(PathManager.Inst.TemplatesPath);
            var templates = Directory.GetFiles(PathManager.Inst.TemplatesPath, "*.ustxp")
                .Concat(Directory.GetFiles(PathManager.Inst.TemplatesPath, "*.ustx")).ToList();
            openTemplatesMenuItems.Clear();
            openTemplatesMenuItems.AddRange(templates.Select(file => new MenuItemViewModel() {
                Header = Path.GetRelativePath(PathManager.Inst.TemplatesPath, file),
                Command = OpenTemplateCommand,
                CommandParameter = file,
            }));
        }

        public void RefreshCacheSize() {
            string header = ThemeManager.GetString("menu.tools.clearcache") ?? "";
            ClearCacheHeader = header;
            Task.Run(async () => {
                var cacheSize = PathManager.Inst.GetCacheSize();
                await Dispatcher.UIThread.InvokeAsync(() => {
                    ClearCacheHeader = $"{header} ({cacheSize})";
                });
            });
        }

        public void RefreshTimelineContextMenu(int tick) {
            TimelineContextMenuItems.Clear();
            var project = TracksViewModel.Project;
            var timeAxis = project.timeAxis;
            timeAxis.TickPosToBarBeat(tick, out int bar, out int beat, out int _);
            var timeSig = timeAxis.TimeSignatureAtBar(bar);
            if (bar == 0) {
                // Do nothing
            } else if (timeSig.barPosition != bar) {
                TimelineContextMenuItems.Add(new MenuItemViewModel {
                    Header = ThemeManager.GetString("context.timeline.addtimesig"),
                    Command = AddTimeSigChangeCmd,
                    CommandParameter = bar,
                });
            } else {
                TimelineContextMenuItems.Add(new MenuItemViewModel {
                    Header = ThemeManager.GetString("context.timeline.deltimesig"),
                    Command = DelTimeSigChangeCmd,
                    CommandParameter = bar,
                });
            }
            var tempo = project.tempos.LastOrDefault(t => t.position < tick);
            if (tempo != null && tempo.position > 0 && (tick - tempo.position) * TracksViewModel.TickWidth < 40) {
                string template = ThemeManager.GetString("context.timeline.deltempo");
                TimelineContextMenuItems.Add(new MenuItemViewModel {
                    Header = string.Format(template, tempo.position),
                    Command = DelTempoChangeCmd,
                    CommandParameter = tempo.position,
                });
            }
            TracksViewModel.TickToLineTick(tick, out int left, out int right);
            if (tempo == null || tempo.position != left) {
                string template = ThemeManager.GetString("context.timeline.addtempo");
                TimelineContextMenuItems.Add(new MenuItemViewModel {
                    Header = string.Format(template, left),
                    Command = AddTempoChangeCmd,
                    CommandParameter = left,
                });
            }
        }

        /// <summary>
        /// Remap a tick position from the old time axis to the new time axis without changing its absolute position (in ms).
        /// Note that this can only be used on positions, not durations.
        /// </summary>
        private int RemapTickPos(int tickPos, TimeAxis oldTimeAxis, TimeAxis newTimeAxis){
            double msPos = oldTimeAxis.TickPosToMsPos(tickPos);
            return newTimeAxis.MsPosToTickPos(msPos);
        }

        /// <summary>
        /// Remap the starting and ending positions of all the notes and parts in the whole project 
        /// from the old time axis to the new time axis, without changing their absolute positions in ms.
        /// </summary>
        public void RemapTimeAxis(TimeAxis oldTimeAxis, TimeAxis newTimeAxis){
            var project = DocManager.Inst.Project;
            foreach(var part in project.parts){
                var partOldStartTick = part.position;
                var partNewStartTick = RemapTickPos(part.position, oldTimeAxis, newTimeAxis);
                if(partNewStartTick != partOldStartTick){
                    DocManager.Inst.ExecuteCmd(new MovePartCommand(
                        project, part, partNewStartTick, part.trackNo));
                }
                if(part is UVoicePart voicePart){
                    var partOldDuration = voicePart.Duration;
                    var partNewDuration = RemapTickPos(partOldStartTick + voicePart.duration, oldTimeAxis, newTimeAxis) - partNewStartTick;
                    if(partNewDuration != partOldDuration) {
                        DocManager.Inst.ExecuteCmd(new ResizeVoicePartCommand(
                            project, voicePart, partNewDuration - partOldDuration, false));
                    }
                    var noteCommands = new List<UCommand>();
                    foreach(var note in voicePart.notes){
                        var noteOldStartTick = note.position + partOldStartTick;
                        var noteOldEndTick = note.End + partOldStartTick;
                        var noteOldDuration = note.duration;
                        var noteNewStartTick = RemapTickPos(noteOldStartTick, oldTimeAxis, newTimeAxis);
                        var noteNewEndTick = RemapTickPos(noteOldEndTick, oldTimeAxis, newTimeAxis);
                        var deltaPosTickInPart = (noteNewStartTick - partNewStartTick) - (noteOldStartTick - partOldStartTick);
                        if(deltaPosTickInPart != 0){
                            noteCommands.Add(new MoveNoteCommand(voicePart, note, deltaPosTickInPart, 0));
                        }
                        var noteNewDuration = noteNewEndTick - noteNewStartTick;
                        var deltaDur = noteNewDuration - noteOldDuration;
                        if(deltaDur != 0){
                            noteCommands.Add(new ResizeNoteCommand(voicePart, note, deltaDur));
                        }
                        //TODO: expression curve remapping, phoneme timing remapping
                    }
                    foreach(var command in noteCommands){
                        DocManager.Inst.ExecuteCmd(command);
                    }
                }
            }
        }

        #region ICmdSubscriber

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is ProgressBarNotification progressBarNotification) {
                Dispatcher.UIThread.InvokeAsync(() => {
                    Progress = progressBarNotification.Progress;
                    ProgressText = progressBarNotification.Info;
                }, DispatcherPriority.Background);
            } else if (cmd is LoadProjectNotification loadProject) {
                Preferences.AddRecentFileIfEnabled(loadProject.project.FilePath);
            } else if (cmd is SaveProjectNotification saveProject) {
                Preferences.AddRecentFileIfEnabled(saveProject.Path);
            }
            SetUndoState();
            this.RaisePropertyChanged(nameof(Title));
        }

        #endregion
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  W4（决策 B6）：素材库「效果器」页签 = 插件浏览器 + 插件扫描路径管理（两处同步）
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 插件浏览器的一行（扫描结果的展示投影）。
    /// 只读快照：名称 / 厂商 / 类型徽标（与链面板口径一致：VST3、VST2、VST3i…）/ 路径。
    /// </summary>
    public class VstPluginItem {
        /// <summary>插件全局标识 —— 拖入效果链面板的负载来源（<c>FxChainDragData.VstPayload</c>）。</summary>
        public string Uid { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Vendor { get; init; } = string.Empty;
        public string Path { get; init; } = string.Empty;
        /// <summary>类型徽标（VST3 / VST2 / VST3i / VST2i / VST）。</summary>
        public string Badge { get; init; } = "VST";

        public bool HasVendor => !string.IsNullOrEmpty(Vendor);
        public bool HasPath => !string.IsNullOrEmpty(Path);
    }

    /// <summary>
    /// 插件库变更广播：**插件扫描路径**与**扫描结果**变化时由两处 UI 各发一次，
    /// 接收侧按内容比对后同步（内容相同即不动 ⇒ 天然防环，不会收发互踢）。
    ///
    /// 为什么用 MessageBus：这是"应用级设置"而不是工程文档状态（不走 DocManager 的命令栈），
    /// 与既有的 PanChangeNotification / SingersRefreshedNotification 同款做法。
    /// </summary>
    public sealed class VstLibraryChangedNotification {
        /// <summary>发起方标识（<see cref="SourceLibrary"/> / <see cref="SourcePreferences"/>）。</summary>
        public string Source { get; init; } = string.Empty;

        public const string SourceLibrary = "library";
        public const string SourcePreferences = "prefs";

        /// <summary>广播（两处 UI 共用的唯一入口）。</summary>
        public static void Publish(string source) =>
            MessageBus.Current.SendMessage(new VstLibraryChangedNotification { Source = source });
    }

    /// <summary>
    /// 素材库「效果器」页签 VM（W4 / B6）。
    ///
    /// 数据源：<c>VstPluginManager</c> 的扫描结果（只读投影，不触发扫描；只列**效果器**——乐器进不了效果链）。
    /// 「重新扫描」把 <c>ScanPlugins()</c> 放后台线程，结果回 UI 线程再动集合
    /// （本轮 W3 刚踩过"非 UI 线程改绑定集合 → Dispatcher.VerifyAccess"）。
    /// 扫描路径与「偏好设置 → VST」**共用 <c>Preferences.Default.VstScanPaths</c>**，不新增平行存储；
    /// 两处各自写盘 + 广播，接收侧 <see cref="SyncScanPaths"/> 按内容比对同步。
    /// </summary>
    public class PluginBrowserViewModel : ViewModelBase, IDisposable {
        readonly Func<IReadOnlyList<VstPluginInfo>> pluginSource;
        readonly Action rescanAction;
        readonly IDisposable notificationSubscription;
        readonly List<VstPluginItem> allPlugins = new();
        bool disposed;

        /// <summary>按搜索过滤后的列表（页面绑它）。</summary>
        public ObservableCollection<VstPluginItem> Plugins { get; } = new ObservableCollection<VstPluginItem>();
        /// <summary>扫描路径（<c>Preferences.Default.VstScanPaths</c> 的镜像；两处同步）。</summary>
        public ObservableCollection<string> ScanPaths { get; } = new ObservableCollection<string>();

        /// <summary>搜索词（名称 / 厂商 / 徽标 三处包含匹配，忽略大小写）。</summary>
        [Reactive] public string SearchText { get; set; } = string.Empty;
        /// <summary>新增路径输入框内容。</summary>
        [Reactive] public string NewPath { get; set; } = string.Empty;
        /// <summary>扫描到的效果器总数（过滤前）。</summary>
        [Reactive] public int PluginCount { get; set; }
        [Reactive] public bool HasPlugins { get; set; }
        /// <summary>一个也没扫到（空态）。</summary>
        [Reactive] public bool ShowNoPlugins { get; set; }
        /// <summary>有插件但没匹配上（"未找到"提示）。</summary>
        [Reactive] public bool ShowNoMatch { get; set; }
        /// <summary>计数行可见（有插件且不在扫描中——扫描中让位给"正在扫描"文案）。</summary>
        [Reactive] public bool ShowCount { get; set; }
        [Reactive] public bool IsScanning { get; set; }

        /// <summary>本实例是否已尝试过"首次自动扫描"（每个窗口一次；刷新失败不影响后续手动重扫）。</summary>
        bool initialScanAttempted;

        /// <param name="pluginSource">测试接缝：插件来源（默认读 <c>VstPluginManager.Inst.KnownPlugins</c>）。</param>
        /// <param name="rescanAction">测试接缝：重扫动作（默认 <c>VstPluginManager.Inst.ScanPlugins()</c>）。</param>
        public PluginBrowserViewModel(
            Func<IReadOnlyList<VstPluginInfo>>? pluginSource = null,
            Action? rescanAction = null) {
            this.pluginSource = pluginSource ?? DefaultPluginSource;
            this.rescanAction = rescanAction ?? (() => VstPluginManager.Inst.ScanPlugins());
            SyncScanPaths();
            RefreshPlugins();
            this.WhenAnyValue(vm => vm.SearchText).Subscribe(_ => ApplyFilter());
            this.WhenAnyValue(vm => vm.IsScanning).Subscribe(_ => UpdateFlags());
            notificationSubscription = MessageBus.Current.Listen<VstLibraryChangedNotification>()
                .Subscribe(_ => OnLibraryChanged());
        }

        static IReadOnlyList<VstPluginInfo> DefaultPluginSource() =>
            VstPluginManager.Inst.KnownPlugins.Values.ToList();

        /// <summary>
        /// 只读刷新：重投影扫描结果 + 应用过滤（不扫描）。
        /// 页签展开时调用，保证列表是新的。
        /// </summary>
        public void RefreshPlugins() {
            allPlugins.Clear();
            try {
                allPlugins.AddRange(pluginSource()
                    .Where(info => info.IsEffect)      // 乐器不进效果链
                    .Select(info => new VstPluginItem {
                        Uid = info.PluginUid,
                        Name = string.IsNullOrEmpty(info.PluginName) ? info.PluginUid : info.PluginName,
                        Vendor = info.Vendor,
                        Path = info.PluginPath,
                        Badge = BadgeFor(info.PluginType, info.IsEffect),
                    })
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase));
            } catch (Exception e) {
                // 扫描在后台跑时注册表字典可能正在变更（预存在行为）；这里不让 UI 崩
                Log.Error(e, "Failed to enumerate VST plugins.");
            }
            ApplyFilter();
        }

        /// <summary>类型徽标（与链面板 <c>VstPluginSlot.PluginTypeDisplay</c> 同口径）。</summary>
        public static string BadgeFor(VstPluginType type, bool isEffect) => type switch {
            VstPluginType.VST3 => isEffect ? "VST3" : "VST3i",
            VstPluginType.VST2 => isEffect ? "VST2" : "VST2i",
            _ => "VST",
        };

        /// <summary>搜索过滤（<see cref="SearchText"/> 变化与列表刷新时都会走）。</summary>
        public void ApplyFilter() {
            string query = (SearchText ?? string.Empty).Trim();
            Plugins.Clear();
            foreach (VstPluginItem item in allPlugins) {
                if (query.Length == 0 || Matches(item, query)) {
                    Plugins.Add(item);
                }
            }
            UpdateFlags();
        }

        /// <summary>可见性标志（过滤后统一算，避免三处各写一份判断）。</summary>
        void UpdateFlags() {
            PluginCount = allPlugins.Count;
            HasPlugins = allPlugins.Count > 0;
            ShowNoPlugins = allPlugins.Count == 0;
            ShowNoMatch = allPlugins.Count > 0 && Plugins.Count == 0;
            ShowCount = allPlugins.Count > 0 && !IsScanning;
        }

        static bool Matches(VstPluginItem item, string query) =>
            item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            item.Vendor.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            item.Badge.Contains(query, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 重扫插件（后台线程扫描 → 回 UI 线程刷新 + 广播）。
        /// 扫描是秒级阻塞（目录遍历 + 读 PE 头），绝不能放 UI 线程。
        /// </summary>
        public async Task RescanInBackgroundAsync() {
            if (IsScanning) {
                return;
            }
            IsScanning = true;
            try {
                await Task.Run(rescanAction);   // 只有扫描在后台
                RefreshPlugins();               // 续体在 UI 线程 ⇒ 动集合安全
                VstLibraryChangedNotification.Publish(VstLibraryChangedNotification.SourceLibrary);
            } catch (Exception e) {
                Log.Error(e, "VST rescan failed.");
            } finally {
                IsScanning = false;
            }
        }

        /// <summary>
        /// 页签展开时调用（W11，**第一个真机缺口就在这里**）：
        /// ① 首次运行播种平台标准 VST3 扫描目录（幂等，用户删掉后不复活）；
        /// ② 注册表**空**则自动首扫一次 —— 注册表启动只从 Preferences 读缓存，
        ///    `VstPluginRegistry` 的标准目录只在扫描时才参与，所以"从不扫描"的机器
        ///    打开插件浏览器永远空态（W6 实测 32 个 .vst3 一个都看不到）。
        /// 播种/首扫都是懒执行：从不打开该页签的用户不会被写盘、不会被扫描。
        /// **返回首扫任务**（没触发则 null）：调用方可忽略（UI），测试则 await 它，
        /// 避免"后台扫描还在跑、用例已结束"的跨用例污染。
        /// </summary>
        /// <param name="platform">测试接缝：目标平台（默认当前平台）。</param>
        /// <param name="folder">测试接缝：取标准目录（默认 Environment.GetFolderPath）。</param>
        public Task? EnsureFirstScan(
            Preferences.VstPathPlatform? platform = null,
            Func<Environment.SpecialFolder, string>? folder = null) {
            if (initialScanAttempted) {
                return null;
            }
            initialScanAttempted = true;
            if (Preferences.SeedStandardVstScanPathsOnce(platform, folder) > 0) {
                SyncScanPaths();
                VstLibraryChangedNotification.Publish(VstLibraryChangedNotification.SourceLibrary);
            }
            if (allPlugins.Count == 0) {
                return RescanInBackgroundAsync();
            }
            return null;
        }

        /// <summary>
        /// 空态按钮：添加本机标准扫描路径（**用户主动**，不受"已播种"标记限制）并立即重扫。
        /// 返回新增路径条数（0 = 该平台标准目录都不存在）。
        /// </summary>
        public async Task<int> AddStandardPathsAndScanAsync(
            Preferences.VstPathPlatform? platform = null,
            Func<Environment.SpecialFolder, string>? folder = null) {
            int added = Preferences.AddStandardVstScanPaths(platform, folder);
            if (added > 0) {
                SyncScanPaths();
                VstLibraryChangedNotification.Publish(VstLibraryChangedNotification.SourceLibrary);
            }
            await RescanInBackgroundAsync();
            return added;
        }

        /// <summary>把 <c>Preferences.Default.VstScanPaths</c> 同步进本 VM（内容相同即不动）。</summary>
        public void SyncScanPaths() {
            List<string> current = Preferences.Default.VstScanPaths ?? new List<string>();
            if (ScanPaths.SequenceEqual(current, StringComparer.Ordinal)) {
                return;
            }
            ScanPaths.Clear();
            foreach (string path in current) {
                ScanPaths.Add(path);
            }
        }

        /// <summary>添加扫描路径（写同一份 Preferences 字段 + 存盘 + 广播）。</summary>
        public bool AddScanPath(string? path) {
            path = path?.Trim();
            if (string.IsNullOrEmpty(path) || ScanPaths.Contains(path)) {
                return false;
            }
            WriteScanPaths(ScanPaths.Append(path!));
            return true;
        }

        /// <summary>移除扫描路径（同上）。</summary>
        public bool RemoveScanPath(string? path) {
            if (string.IsNullOrEmpty(path) || !ScanPaths.Contains(path)) {
                return false;
            }
            WriteScanPaths(ScanPaths.Where(p => p != path));
            return true;
        }

        /// <summary>从输入框添加（成功后清空输入；供按钮处理器调用）。</summary>
        public bool AddPathFromInput() {
            if (!AddScanPath(NewPath)) {
                return false;
            }
            NewPath = string.Empty;
            return true;
        }

        void WriteScanPaths(IEnumerable<string> paths) {
            var list = paths.ToList();
            Preferences.Default.VstScanPaths = list;   // 唯一存储（既有字段）
            Preferences.Save();
            SyncScanPaths();                            // 本地镜像跟随
            VstLibraryChangedNotification.Publish(VstLibraryChangedNotification.SourceLibrary);
        }

        readonly UiThreadAffinity affinity = new UiThreadAffinity();

        void OnLibraryChanged() {
            if (disposed) {
                return;
            }
            // 广播可能来自任意线程（例如重扫的后台续体）：非 UI 线程一律编组回来。
            // 判据用 `UiThreadAffinity`（锚定**订阅时所属线程**）而非 `CheckAccess()`：
            // 后者在 headless 测试宿主下会误判为 true（我们已因此踩过两次）。
            affinity.Post(() => {
                if (disposed) {
                    return;
                }
                SyncScanPaths();
                RefreshPlugins();
            });
        }

        /// <summary>
        /// 插件行的拖拽数据：格式 <c>OpenUtau.FxChainItem</c>（<c>FxChainDragData.Format</c>），
        /// 负载 = 插件 UID；落点由链面板的 <c>DropPayload</c> 消费（落槽 / 建链，可撤销）。
        /// </summary>
        public static DataTransfer CreatePluginDragData(VstPluginItem item) =>
            FxChainDragData.CreateDataTransfer(FxChainDragData.VstPayload(item.Uid));

        public void Dispose() {
            if (disposed) {
                return;
            }
            disposed = true;
            notificationSubscription.Dispose();
        }
    }
}

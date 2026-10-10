using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Disposables;
using System.Collections.Specialized;
using System.Text.RegularExpressions;
using OpenUtau.Audio;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using OpenUtau.Core.Vst;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using OpenUtau.Core.Render;
using Serilog;

namespace OpenUtau.App.ViewModels {
    public class LyricsHelperOption {
        public readonly Type klass;
        public LyricsHelperOption(Type klass) {
            this.klass = klass;
        }
        public override string ToString() {
            return klass.Name;
        }
    }

    /// <summary>「外观 · 强调色」色板项：显示的是**该种子在当前深浅色下真正生成的主色**（所见即所得）。</summary>
    public class AccentSwatchViewModel {
        public uint Seed { get; }
        public Avalonia.Media.IBrush Brush { get; }
        public bool IsSelected { get; }
        public string Tooltip { get; }

        public AccentSwatchViewModel(uint seed, bool isDark, uint currentSeed) {
            Seed = seed;
            var colors = Core.Theming.Md3SchemeColors.Create(seed, Core.Theming.Md3SchemeVariant.TonalSpot, isDark);
            Brush = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(
                Theming.Md3ColorPool.ToColor(colors.Get(Core.Theming.Md3Role.Primary)));
            IsSelected = seed == currentSeed;
            Tooltip = $"#{seed & 0xFFFFFF:X6}";
        }
    }

    public class PreferencesViewModel : ViewModelBase, IDisposable {
        readonly CompositeDisposable subscriptions = new();
        bool syncingVstPaths;
        public bool IsDisposed { get; private set; }

        // General
        private CultureInfo? language;
        private CultureInfo? sortingOrder;

        public List<CultureInfo>? Languages { get; }
        public CultureInfo? Language {
            get => language;
            set => this.RaiseAndSetIfChanged(ref language, value);
        }
        public List<CultureInfo>? SortingOrders { get; }
        public CultureInfo? SortingOrder {
            get => sortingOrder;
            set => this.RaiseAndSetIfChanged(ref sortingOrder, value);
        }
        // Playback
        private List<AudioOutputDevice>? audioOutputDevices;
        private AudioOutputDevice? audioOutputDevice;
        public List<AudioOutputDevice>? AudioOutputDevices {
            get => audioOutputDevices;
            set => this.RaiseAndSetIfChanged(ref audioOutputDevices, value);
        }
        public AudioOutputDevice? AudioOutputDevice {
            get => audioOutputDevice;
            set => this.RaiseAndSetIfChanged(ref audioOutputDevice, value);
        }
        [Reactive] public bool UseSystemDefaultDevice { get; set; }
        [Reactive] public int PreferPortAudio { get; set; }
        [Reactive] public int LockStartTime { get; set; }
        [Reactive] public int PlaybackAutoScroll { get; set; }
        [Reactive] public double PlayPosMarkerMargin { get; set; }
        [Reactive] public int MetronomeVolume { get; set; }
        [Reactive] public int MetronomeHighFrequency { get; set; }
        [Reactive] public int MetronomeLowFrequency { get; set; }

        // Paths
        public string SingerPath => PathManager.Inst.SingersPath;
        public string AdditionalSingersPath => !string.IsNullOrWhiteSpace(PathManager.Inst.AdditionalSingersPath) ? PathManager.Inst.AdditionalSingersPath : "(None)";
        public string SamplesPath {
            get {
                var paths = Preferences.GetSampleSearchPaths();
                var path = paths.Count > 0 ? paths[0] : PathManager.Inst.SamplesPath;
                return string.IsNullOrWhiteSpace(path) ? "(None)" : path;
            }
        }
        [Reactive] public bool InstallToAdditionalSingersPath { get; set; }
        [Reactive] public bool LoadDeepFolders { get; set; }

        // Editing
        public List<LyricsHelperOption> LyricsHelpers { get; } =
            ActiveLyricsHelper.Inst.Available
                .Select(klass => new LyricsHelperOption(klass))
                .ToList();
        [Reactive] public LyricsHelperOption? LyricsHelper { get; set; }
        [Reactive] public bool LyricsHelperBrackets { get; set; }
        [Reactive] public bool PenPlusDefault { get; set; }

        // Render
        [Reactive] public bool PreRender { get; set; }
        [Reactive] public int NumRenderThreads { get; set; }
        public int LogicalCoreCount {
            get => Environment.ProcessorCount;
        }
        [Reactive] public bool HighThreads { get; set; }
        public int SafeMaxThreadCount {
            get => Math.Min(8, LogicalCoreCount / 2);
        }
        [Reactive] public bool SkipRenderingMutedTracks { get; set; }
        [Reactive] public bool ClearCacheOnQuit { get; set; }
        public List<string> OnnxRunnerOptions { get; set; }
        [Reactive] public string OnnxRunner { get; set; }
        public List<GpuInfo> OnnxGpuOptions { get; set; }
        [Reactive] public GpuInfo OnnxGpu { get; set; }
        [Reactive] public bool ShowOnnxGpu { get; set; }

        // Appearance
        [Reactive] public string ThemeName { get; set; }
        /// <summary>MD3 颜色池种子（决定整应用的强调色阶；D4）。</summary>
        public ObservableCollection<AccentSwatchViewModel> AccentSwatches { get; } = new ObservableCollection<AccentSwatchViewModel>();
        public ReactiveCommand<AccentSwatchViewModel, System.Reactive.Unit>? SelectAccentCommand { get; private set; }
        [Reactive] public int DegreeStyle { get; set; }
        [Reactive] public bool UseTrackColor { get; set; }
        [Reactive] public bool ShowPortrait { get; set; }
        [Reactive] public bool ShowIcon { get; set; }
        [Reactive] public bool ShowGhostNotes { get; set; }
        [Reactive] public bool NoteHoverGlow { get; set; }
        [Reactive] public bool ShowPlaybackNoteHighlight { get; set; }
        [Reactive] public bool ShowPlaybackNoteBounce { get; set; }
        [Reactive] public bool DetachPianoRoll { get; set; }
        [Reactive] public bool ThemeEditable { get; set; }
        public List<string> ThemeItems => ThemeManager.GetAvailableThemes();
        public bool IsThemeEditorOpen => Views.ThemeEditorWindow.IsOpen;

        /// <summary>可选强调色种子（取自设计稿「强调色」色板；色板上显示的是该种子**真实生成**的主色）。</summary>
        private static readonly uint[] AccentSeeds = {
            0xFF6FDBCB, 0xFFFFB4AB, 0xFFA9CBE7, 0xFFF3C97E, 0xFFC9B8E8, 0xFF9BE3A8,
        };

        /// <summary>换种子 → 立即重建颜色池（整应用重着色），并落盘（见决定文档 D4）。</summary>
        public void ApplyThemeSeed(uint seed) {
            if (IsDisposed) return;
            if (Preferences.Default.ThemeSeed == seed) {
                return;
            }
            Preferences.Default.ThemeSeed = seed;
            Preferences.Save();
            Theming.ColorPool.Initialize(seed, Core.Theming.Md3SchemeVariant.TonalSpot, ThemeManager.IsDarkMode);
            RebuildAccentSwatches();
        }

        private void RebuildAccentSwatches() {
            if (IsDisposed) return;
            AccentSwatches.Clear();
            foreach (uint seed in AccentSeeds) {
                AccentSwatches.Add(new AccentSwatchViewModel(seed, ThemeManager.IsDarkMode, Preferences.Default.ThemeSeed));
            }
        }

        // UTAU
        public List<string> DefaultRendererOptions { get; set; }
        [Reactive] public string DefaultRenderer { get; set; }
        [Reactive] public int OtoEditor { get; set; }
        public string VLabelerPath => Preferences.Default.VLabelerPath;
        public string SetParamPath => Preferences.Default.SetParamPath;

        // Diffsinger
        public List<int> DiffSingerStepsOptions { get; } = new List<int> { 2, 5, 10, 20, 50, 100, 200, 500, 1000 };
        public List<int> DiffSingerStepsVarianceOptions { get; } = new List<int> { 2, 5, 10, 20, 50, 100, 200, 500, 1000 };
        public List<int> DiffSingerStepsPitchOptions { get; } = new List<int> { 2, 5, 10, 20, 50, 100, 200, 500, 1000 };
        [Reactive] public int DiffSingerSteps { get; set; }
        [Reactive] public int DiffSingerStepsVariance { get; set; }
        [Reactive] public int DiffSingerStepsPitch { get; set; }
        [Reactive] public double DiffSingerDepth { get; set; }
        [Reactive] public bool DiffSingerTensorCache { get; set; }
        [Reactive] public bool DiffSingerVarianceLocalPitchPatch { get; set; }
        [Reactive] public bool DiffSingerLangCodeHide { get; set; }

        // Advanced
        [Reactive] public bool RememberMid { get; set; }
        [Reactive] public bool RememberUst { get; set; }
        [Reactive] public bool RememberVsqx { get; set; }
        public string WinePath => Preferences.Default.WinePath;
        [Reactive] public bool DefaultSnapCurve { get; set; }

        public PreferencesViewModel() {
            try {
                var audioOutput = PlaybackManager.Inst.AudioOutput;
                if (audioOutput != null) {
                    AudioOutputDevices = audioOutput.GetOutputDevices();
                    int deviceNumber = audioOutput.DeviceNumber;
                    var device = AudioOutputDevices.FirstOrDefault(d => d.deviceNumber == deviceNumber);
                    if (device != null) {
                        AudioOutputDevice = device;
                    }
                }
                UseSystemDefaultDevice = Preferences.Default.UseSystemDefaultAudioDevice;
                PreferPortAudio = Preferences.Default.PreferPortAudio ? 1 : 0;
                PlaybackAutoScroll = Preferences.Default.PlaybackAutoScroll;
                PlayPosMarkerMargin = Preferences.Default.PlayPosMarkerMargin;
                MetronomeVolume = Preferences.Default.MetronomeVolume;
                MetronomeHighFrequency = Preferences.Default.MetronomeHighFrequency;
                MetronomeLowFrequency = Preferences.Default.MetronomeLowFrequency;
                LockStartTime = Preferences.Default.LockStartTime;
                InstallToAdditionalSingersPath = Preferences.Default.InstallToAdditionalSingersPath;
                LoadDeepFolders = Preferences.Default.LoadDeepFolderSinger;
                ToolsManager.Inst.Initialize();
                var pattern = new Regex(@"Strings\.([\w-]+)\.axaml");
                Languages = App.GetLanguages().Keys
                    .Select(lang => CultureInfo.GetCultureInfo(lang))
                    .ToList();
                Language = string.IsNullOrEmpty(Preferences.Default.Language)
                    ? null
                    : CultureInfo.GetCultureInfo(Preferences.Default.Language);
                SortingOrders = Languages.ToList();
                SortingOrders.Insert(0, CultureInfo.InvariantCulture);
                SortingOrder = Preferences.Default.SortingOrder == null ? Language
                    : string.IsNullOrEmpty(Preferences.Default.SortingOrder) ? CultureInfo.InvariantCulture
                    : CultureInfo.GetCultureInfo(Preferences.Default.SortingOrder);
                PreRender = Preferences.Default.PreRender;
                DefaultRendererOptions = Renderers.getRendererOptions();
                DefaultRenderer = String.IsNullOrEmpty(Preferences.Default.DefaultRenderer) ?
                   DefaultRendererOptions[0] : Preferences.Default.DefaultRenderer;
                NumRenderThreads = Preferences.Default.NumRenderThreads;
                HighThreads = NumRenderThreads > SafeMaxThreadCount;
                OnnxRunnerOptions = Onnx.getRunnerOptions();
                OnnxRunner = String.IsNullOrEmpty(Preferences.Default.OnnxRunner) ?
                   OnnxRunnerOptions[0] : Preferences.Default.OnnxRunner;
                OnnxGpuOptions = Onnx.getGpuInfo();
                OnnxGpu = OnnxGpuOptions.FirstOrDefault(x => x.deviceId == Preferences.Default.OnnxGpu, OnnxGpuOptions[0]);
                ShowOnnxGpu = OnnxRunner == "DirectML";
                DiffSingerDepth = Preferences.Default.DiffSingerDepth * 100;
                DiffSingerSteps = Preferences.Default.DiffSingerSteps;
                DiffSingerStepsVariance = Preferences.Default.DiffSingerStepsVariance;
                DiffSingerStepsPitch = Preferences.Default.DiffSingerStepsPitch;
                DiffSingerTensorCache = Preferences.Default.DiffSingerTensorCache;
                DiffSingerVarianceLocalPitchPatch = Preferences.Default.DiffSingerVarianceLocalPitchPatch;
                DiffSingerLangCodeHide = Preferences.Default.DiffSingerLangCodeHide;
                SkipRenderingMutedTracks = Preferences.Default.SkipRenderingMutedTracks;
                ThemeName = Preferences.Default.ThemeName;
                ThemeEditable = ThemeName != "Light" && ThemeName != "Dark" && !Colors.CustomTheme.IsPackageTheme(ThemeName);
                // MD3 配色：色板显示"该种子在当前深浅色下真正生成的主色"（D4 种子可选）
                SelectAccentCommand = ReactiveCommand.Create<AccentSwatchViewModel>(swatch => ApplyThemeSeed(swatch.Seed));
                RebuildAccentSwatches();
                DegreeStyle = Preferences.Default.DegreeStyle;
                UseTrackColor = Preferences.Default.UseTrackColor;
                ShowPortrait = Preferences.Default.ShowPortrait;
                ShowIcon = Preferences.Default.ShowIcon;
                ShowGhostNotes = Preferences.Default.ShowGhostNotes;
                NoteHoverGlow = Preferences.Default.NoteHoverGlow;
                ShowPlaybackNoteHighlight = Preferences.Default.ShowPlaybackNoteHighlight;
                ShowPlaybackNoteBounce = Preferences.Default.ShowPlaybackNoteBounce;
                DetachPianoRoll = Preferences.Default.DetachPianoRoll;
                LyricsHelper = LyricsHelpers.FirstOrDefault(option => option.klass.Equals(ActiveLyricsHelper.Inst.GetPreferred()));
                LyricsHelperBrackets = Preferences.Default.LyricsHelperBrackets;
                OtoEditor = Preferences.Default.OtoEditor;
                RememberMid = Preferences.Default.RememberMid;
                RememberUst = Preferences.Default.RememberUst;
                RememberVsqx = Preferences.Default.RememberVsqx;
                DefaultSnapCurve = Preferences.Default.DefaultSnapCurve;
                ClearCacheOnQuit = Preferences.Default.ClearCacheOnQuit;

                MessageBus.Current.Listen<ThemeEditorStateChangedEvent>()
                    .Subscribe(_ => this.RaisePropertyChanged(nameof(IsThemeEditorOpen))).DisposeWith(subscriptions);
            
                this.WhenAnyValue(vm => vm.UseSystemDefaultDevice)
                    .Skip(1)
                    .Subscribe(useDefault => {
                        Preferences.Default.UseSystemDefaultAudioDevice = useDefault;
                        Preferences.Save();
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.AudioOutputDevice)
                    .Skip(1)
                    .WhereNotNull()
                    .SubscribeOn(RxApp.MainThreadScheduler)
                    .Subscribe(device => {
                        if (UseSystemDefaultDevice) {
                            return;
                        }
                        if (PlaybackManager.Inst.AudioOutput != null) {
                            try {
                                PlaybackManager.Inst.AudioOutput.SelectDevice(device.guid, device.deviceNumber);
                            } catch (Exception e) {
                                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification($"Failed to select device {device.name}", e));
                            }
                        }
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.PreferPortAudio)
                    .Skip(1)
                    .Subscribe(index => {
                        Preferences.Default.PreferPortAudio = index > 0;
                        Preferences.Save();
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.PlaybackAutoScroll)
                    .Skip(1)
                    .Subscribe(autoScroll => {
                        Preferences.Default.PlaybackAutoScroll = autoScroll;
                        Preferences.Save();
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.PlayPosMarkerMargin)
                    .Skip(1)
                    .Subscribe(playPosMarkerMargin => {
                        Preferences.Default.PlayPosMarkerMargin = playPosMarkerMargin;
                        Preferences.Save();
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.MetronomeVolume)
                    .Skip(1)
                    .Subscribe(metronomeVolume => {
                        Preferences.Default.MetronomeVolume = metronomeVolume;
                        Preferences.Save();
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.MetronomeHighFrequency)
                    .Skip(1)
                    .Subscribe(metronomeHighFrequency => {
                        Preferences.Default.MetronomeHighFrequency = metronomeHighFrequency;
                        Preferences.Save();
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.MetronomeLowFrequency)
                    .Skip(1)
                    .Subscribe(metronomeLowFrequency => {
                        Preferences.Default.MetronomeLowFrequency = metronomeLowFrequency;
                        Preferences.Save();
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.LockStartTime)
                    .Skip(1)
                    .Subscribe(lockStartTime => {
                        Preferences.Default.LockStartTime = lockStartTime;
                        Preferences.Save();
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.InstallToAdditionalSingersPath)
                    .Skip(1)
                    .Subscribe(additionalSingersPath => {
                        Preferences.Default.InstallToAdditionalSingersPath = additionalSingersPath;
                        Preferences.Save();
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.LoadDeepFolders)
                    .Skip(1)
                    .Subscribe(loadDeepFolders => {
                        Preferences.Default.LoadDeepFolderSinger = loadDeepFolders;
                        Preferences.Save();
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.PreRender)
                    .Skip(1)
                    .Subscribe(preRender => {
                        Preferences.Default.PreRender = preRender;
                        Preferences.Save();
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.Language)
                    .Skip(1)
                    .Subscribe(lang => {
                        Preferences.Default.Language = lang?.Name ?? string.Empty;
                        Preferences.Save();
                        App.SetLanguage(Preferences.Default.Language);
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.SortingOrder)
                    .Skip(1)
                    .Subscribe(so => {
                        Preferences.Default.SortingOrder = so?.Name ?? null;
                        Preferences.Save();
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.ThemeName)
                    .Skip(1)
                    .Subscribe(themeName => {
                        ThemeEditable = themeName != "Light" && themeName != "Dark" && !Colors.CustomTheme.IsPackageTheme(themeName);
                        if (!IsThemeEditorOpen) {
                            Preferences.Default.ThemeName = themeName;
                            Preferences.Save();
                            App.SetTheme();
                        }
                    }).DisposeWith(subscriptions);
                // 主题切换后色板要跟着换深浅色（显示的是当前变体下真实生成的主色）
                MessageBus.Current.Listen<ThemeChangedEvent>()
                    .Subscribe(_ => RebuildAccentSwatches()).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.DegreeStyle)
                    .Skip(1)
                    .Subscribe(degreeStyle => {
                        Preferences.Default.DegreeStyle = degreeStyle;
                        Preferences.Save();
                        MessageBus.Current.SendMessage(new PianorollRefreshEvent("Part"));
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.UseTrackColor)
                    .Skip(1)
                    .Subscribe(trackColor => {
                        Preferences.Default.UseTrackColor = trackColor;
                        Preferences.Save();
                        MessageBus.Current.SendMessage(new PianorollRefreshEvent("TrackColor"));
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.ShowPortrait)
                    .Skip(1)
                    .Subscribe(showPortrait => {
                        Preferences.Default.ShowPortrait = showPortrait;
                        Preferences.Save();
                        MessageBus.Current.SendMessage(new PianorollRefreshEvent("Portrait"));
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.ShowIcon)
                    .Skip(1)
                    .Subscribe(showIcon => {
                        Preferences.Default.ShowIcon = showIcon;
                        Preferences.Save();
                        MessageBus.Current.SendMessage(new PianorollRefreshEvent("Portrait"));
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.ShowGhostNotes)
                    .Skip(1)
                    .Subscribe(showGhostNotes => {
                        Preferences.Default.ShowGhostNotes = showGhostNotes;
                        Preferences.Save();
                        MessageBus.Current.SendMessage(new PianorollRefreshEvent("Part"));
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.NoteHoverGlow)
                    .Skip(1)
                    .Subscribe(noteHoverGlow => {
                        Preferences.Default.NoteHoverGlow = noteHoverGlow;
                        Preferences.Save();
                        MessageBus.Current.SendMessage(new NotesRefreshEvent());
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.ShowPlaybackNoteHighlight)
                    .Skip(1)
                    .Subscribe(showPlaybackNoteHighlight => {
                        Preferences.Default.ShowPlaybackNoteHighlight = showPlaybackNoteHighlight;
                        Preferences.Save();
                        MessageBus.Current.SendMessage(new PianorollRefreshEvent("PlaybackNoteHighlight"));
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.ShowPlaybackNoteBounce)
                    .Skip(1)
                    .Subscribe(showPlaybackNoteBounce => {
                        Preferences.Default.ShowPlaybackNoteBounce = showPlaybackNoteBounce;
                        Preferences.Save();
                        MessageBus.Current.SendMessage(new PianorollRefreshEvent("PlaybackNoteBounce"));
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.DetachPianoRoll)
                    .Skip(1)
                    .Subscribe(detachPianoRoll => {
                        Preferences.Default.DetachPianoRoll = detachPianoRoll;
                        Preferences.Save();
                        MessageBus.Current.SendMessage(new PianorollRefreshEvent("Attachment"));
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.LyricsHelper)
                    .Skip(1)
                    .Subscribe(option => {
                        ActiveLyricsHelper.Inst.Set(option?.klass);
                        Preferences.Default.LyricHelper = option?.klass?.Name ?? string.Empty;
                        Preferences.Save();
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.LyricsHelperBrackets)
                    .Skip(1)
                    .Subscribe(brackets => {
                        Preferences.Default.LyricsHelperBrackets = brackets;
                        Preferences.Save();
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.OtoEditor)
                    .Skip(1)
                    .Subscribe(index => {
                        Preferences.Default.OtoEditor = index;
                        Preferences.Save();
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.NumRenderThreads)
                    .Skip(1)
                    .Subscribe(index => {
                        Preferences.Default.NumRenderThreads = index;
                        HighThreads = index > SafeMaxThreadCount ? true : false;
                        Preferences.Save();
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.DefaultRenderer)
                    .Skip(1)
                    .Subscribe(index => {
                        Preferences.Default.DefaultRenderer = index;
                        Preferences.Save();
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.OnnxRunner)
                    .Skip(1)
                    .Subscribe(index => {
                        Preferences.Default.OnnxRunner = index;
                        Preferences.Save();
                        ToggleOnnxGpuDisplay(index == "DirectML");
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.OnnxGpu)
                    .Skip(1)
                    .Subscribe(index => {
                        Preferences.Default.OnnxGpu = index.deviceId;
                        Preferences.Save();
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.RememberMid)
                    .Skip(1)
                    .Subscribe(index => {
                        Preferences.Default.RememberMid = index;
                        Preferences.Save();
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.RememberUst)
                    .Skip(1)
                    .Subscribe(index => {
                        Preferences.Default.RememberUst = index;
                        Preferences.Save();
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.RememberVsqx)
                    .Skip(1)
                    .Subscribe(index => {
                        Preferences.Default.RememberVsqx = index;
                        Preferences.Save();
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.DefaultSnapCurve)
                    .Skip(1)
                    .Subscribe(index => {
                        Preferences.Default.DefaultSnapCurve = index;
                        Preferences.Save();
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.ClearCacheOnQuit)
                    .Skip(1)
                    .Subscribe(index => {
                        Preferences.Default.ClearCacheOnQuit = index;
                        Preferences.Save();
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.DiffSingerSteps)
                    .Skip(1)
                    .Subscribe(index => {
                        Preferences.Default.DiffSingerSteps = index;
                        Preferences.Save();
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.DiffSingerStepsVariance)
                    .Skip(1)
                     .Subscribe(index => {
                         Preferences.Default.DiffSingerStepsVariance = index;
                         Preferences.Save();
                     }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.DiffSingerStepsPitch)
                    .Skip(1)
                    .Subscribe(index => {
                        Preferences.Default.DiffSingerStepsPitch = index;
                        Preferences.Save();
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.DiffSingerDepth)
                    .Skip(1)
                    .Subscribe(index => {
                        Preferences.Default.DiffSingerDepth = index / 100;
                        Preferences.Save();
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.DiffSingerTensorCache)
                    .Skip(1)
                    .Subscribe(useCache => {
                        Preferences.Default.DiffSingerTensorCache = useCache;
                        Preferences.Save();
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.DiffSingerVarianceLocalPitchPatch)
                    .Skip(1)
                    .Subscribe(useLocalPatch => {
                        Preferences.Default.DiffSingerVarianceLocalPitchPatch = useLocalPatch;
                        Preferences.Save();
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.DiffSingerLangCodeHide)
                    .Skip(1)
                    .Subscribe(useCache => {
                        Preferences.Default.DiffSingerLangCodeHide = useCache;
                        Preferences.Save();
                    }).DisposeWith(subscriptions);
                this.WhenAnyValue(vm => vm.SkipRenderingMutedTracks)
                    .Skip(1)
                    .Subscribe(skipRenderingMutedTracks => {
                        Preferences.Default.SkipRenderingMutedTracks = skipRenderingMutedTracks;
                        Preferences.Save();
                    }).DisposeWith(subscriptions);
            } catch {
                Dispose();
                throw;
            }
        }

        public void TestAudioOutputDevice() {
            if (IsDisposed) return;
            try {
                PlaybackManager.Inst.PlayTestSound();
            } catch (Exception e) {
                Log.Error(e, "Failed to play test sound.");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification("Failed to play test sound.", e));
            }
        }

        public void TestMetronome() {
            if (IsDisposed) return;
            try {
                PlaybackManager.Inst.PlayMetronomeClick();
            } catch (Exception e) {
                Log.Error(e, "Failed to play metronome preview.");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification("Failed to play metronome preview.", e));
            }
        }

        public void ResetMetronomeVolume() {
            if (IsDisposed) return;
            MetronomeVolume = new Preferences.SerializablePreferences().MetronomeVolume;
        }

        public void ResetMetronomeHighFrequency() {
            if (IsDisposed) return;
            MetronomeHighFrequency = new Preferences.SerializablePreferences().MetronomeHighFrequency;
        }

        public void ResetMetronomeLowFrequency() {
            if (IsDisposed) return;
            MetronomeLowFrequency = new Preferences.SerializablePreferences().MetronomeLowFrequency;
        }

        public void OpenResamplerLocation() {
            if (IsDisposed) return;
            try {
                string path = PathManager.Inst.ResamplersPath;
                Directory.CreateDirectory(path);
                OS.OpenFolder(path);
            } catch (Exception e) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        public void SetAddlSingersPath(string path) {
            if (IsDisposed) return;
            Preferences.Default.AdditionalSingerPath = path;
            Preferences.Save();
            this.RaisePropertyChanged(nameof(AdditionalSingersPath));
        }

        public void SetSamplesPath(string path) {
            if (IsDisposed) return;
            if (string.IsNullOrWhiteSpace(path)) {
                Preferences.SetSampleSearchPaths(new List<string>());
            } else {
                var paths = Preferences.GetSampleSearchPaths();
                paths.RemoveAll(p => p == path);
                paths.Insert(0, path);
                Preferences.SetSampleSearchPaths(paths);
            }
            this.RaisePropertyChanged(nameof(SamplesPath));
        }

        public void SetVLabelerPath(string path) {
            if (IsDisposed) return;
            Preferences.Default.VLabelerPath = path;
            Preferences.Save();
            this.RaisePropertyChanged(nameof(VLabelerPath));
        }

        public void SetSetParamPath(string path) {
            if (IsDisposed) return;
            Preferences.Default.SetParamPath = path;
            Preferences.Save();
            this.RaisePropertyChanged(nameof(SetParamPath));
        }

        public void SetWinePath(string path) {
            if (IsDisposed) return;
            Preferences.Default.WinePath = path;
            Preferences.Save();
            ToolsManager.Inst.Initialize();
            this.RaisePropertyChanged(nameof(WinePath));
        }

        public void RefreshThemes() {
            if (IsDisposed) return;
            Colors.CustomTheme.ListThemes();
            _ = OudepLoaderRegistry.LoadAllAsync();
            this.RaisePropertyChanged(nameof(ThemeItems));
        }

        public void ToggleOnnxGpuDisplay(bool show) {
            if (IsDisposed) return;
            ShowOnnxGpu = show;
        }

        // ── OpenUTAU Plus: VST Settings ─────────────────────

        private ObservableCollection<string>? _vstScanPaths;
        public ObservableCollection<string> VstScanPaths {
            get {
                if (_vstScanPaths == null) {
                    _vstScanPaths = new ObservableCollection<string>(Preferences.Default.VstScanPaths);
                    if (!IsDisposed) _vstScanPaths.CollectionChanged += OnVstScanPathsChanged;
                }
                return _vstScanPaths;
            }
        }

        void OnVstScanPathsChanged(object? sender, NotifyCollectionChangedEventArgs e) {
            if (IsDisposed || syncingVstPaths) return;
            Preferences.Default.VstScanPaths = VstScanPaths.ToList();
            Preferences.Save();
        }

        public void SyncVstScanPathsFromStore() {
            if (IsDisposed) return;
            var paths = (Preferences.Default.VstScanPaths ?? new List<string>()).ToArray();
            if (VstScanPaths.SequenceEqual(paths, StringComparer.Ordinal)) return;
            syncingVstPaths = true;
            try {
                VstScanPaths.Clear();
                foreach (var path in paths) VstScanPaths.Add(path);
            } finally {
                syncingVstPaths = false;
            }
        }

        public void Dispose() {
            if (IsDisposed) return;
            IsDisposed = true;
            subscriptions.Dispose();
            SelectAccentCommand?.Dispose();
            if (_vstScanPaths != null) _vstScanPaths.CollectionChanged -= OnVstScanPathsChanged;
        }

        [Reactive] public int VstPluginCount { get; set; }
        [Reactive] public int VstEffectCount { get; set; }

        public List<VstPluginEntry> VstKnownPlugins =>
            VstPluginRegistry.Inst.All.ToList();

        public void AddVstScanPath(string path) {
            if (IsDisposed) return;
            if (string.IsNullOrWhiteSpace(path)) return;
            if (VstScanPaths.Contains(path)) return;
            VstScanPaths.Add(path);
        }

        public void RemoveVstScanPath(string path) {
            if (IsDisposed) return;
            VstScanPaths.Remove(path);
        }

        public void RefreshVstPlugins() {
            if (IsDisposed) return;
            VstPluginRegistry.Inst.Rescan();
            VstPluginCount = VstPluginRegistry.Inst.Count;
            VstEffectCount = VstPluginRegistry.Inst.EffectCount;
            this.RaisePropertyChanged(nameof(VstKnownPlugins));
            this.RaisePropertyChanged(nameof(VstPluginCount));
            this.RaisePropertyChanged(nameof(VstEffectCount));
        }
    }
}

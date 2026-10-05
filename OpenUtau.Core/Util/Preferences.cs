using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using OpenUtau.Core.Render;
using Serilog;

namespace OpenUtau.Core.Util {

    public static class Preferences {
        public static SerializablePreferences Default;

        static Preferences() {
            Load();
        }

        public static void Save() {
            try {
                File.WriteAllText(PathManager.Inst.PrefsFilePath,
                    JsonConvert.SerializeObject(Default, Formatting.Indented),
                    Encoding.UTF8);
            } catch (Exception e) {
                Log.Error(e, "Failed to save prefs.");
            }
        }

        public static void Reset() {
            Default = new SerializablePreferences();
            try
            {
                string exePath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                string shippedPrefsPath = Path.Combine(exePath, "prefs-default.json");
                if (File.Exists(shippedPrefsPath)) {
                    var shippedPrefs = JsonConvert.DeserializeObject<SerializablePreferences>(
                        File.ReadAllText(shippedPrefsPath, Encoding.UTF8));
                    if (shippedPrefs != null) {
                        Default = shippedPrefs;
                    }
                }
            } catch(Exception e){
                Log.Error(e, "failed to load prefs-default.json");
            }
            Save();
        }

        public static List<string> GetSingerSearchPaths() {
            return new List<string>(Default.SingerSearchPaths);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  W11：VST 标准扫描路径（首次运行播种 + 一键添加）
        //
        //  动机：插件浏览器（素材库「效果器」页签）在新机器上永远空态——Preferences 里
        //  VstScanPaths 默认空，而用户并不知道标准目录在哪。这里把"平台标准 VST3 目录"
        //  变成可播种的默认值，并提供**纯函数**（平台与取目录都可注入）以便测试。
        //  策略：播种只在 VstScanPathsSeeded == false 时发生一次，之后置位并落盘；
        //  用户删掉标准路径不会被复活（下次启动也不会补回来）。
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>路径平台（作为纯参数传入 ⇒ 测试无需真的跑在三个系统上）。</summary>
        public enum VstPathPlatform {
            Windows,
            MacOS,
            Linux,
        }

        /// <summary>当前运行平台。</summary>
        public static VstPathPlatform CurrentVstPathPlatform() =>
            OperatingSystem.IsWindows() ? VstPathPlatform.Windows
            : OperatingSystem.IsMacOS() ? VstPathPlatform.MacOS
            : VstPathPlatform.Linux;

        /// <summary>
        /// 各平台的**标准 VST3 扫描目录候选**（纯函数；不判存在、不落盘）。
        /// <paramref name="folder"/> 默认 <c>Environment.GetFolderPath</c>，测试可注入假目录树。
        /// </summary>
        public static IReadOnlyList<string> StandardVstScanPaths(
            VstPathPlatform platform,
            Func<Environment.SpecialFolder, string>? folder = null) {
            folder ??= Environment.GetFolderPath;
            var paths = new List<string>();
            void AddPath(string? path) {
                if (!string.IsNullOrWhiteSpace(path)) {
                    paths.Add(path!);
                }
            }
            // 组合路径用 Path.Combine（平台分隔符正确）；POSIX 字面量直接给整串，
            // 避免在 Windows 上被拼成 "usr\lib\vst3" 这种混合分隔符（纯函数要保持可跨平台断言）。
            void AddUnder(string? root, params string[] parts) {
                if (string.IsNullOrEmpty(root)) {
                    return;
                }
                string path = root!;
                foreach (string part in parts) {
                    path = Path.Combine(path, part);
                }
                AddPath(path);
            }
            switch (platform) {
                case VstPathPlatform.Windows:
                    AddUnder(folder(Environment.SpecialFolder.CommonProgramFiles), "VST3");            // %CommonProgramFiles%\VST3
                    AddUnder(folder(Environment.SpecialFolder.ProgramFiles), "Common Files", "VST3");  // 同一目录的等价写法
                    AddUnder(folder(Environment.SpecialFolder.ProgramFilesX86), "Common Files", "VST3");
                    AddUnder(folder(Environment.SpecialFolder.LocalApplicationData), "Programs", "Common", "VST3");
                    break;
                case VstPathPlatform.MacOS:
                    AddPath("/Library/Audio/Plug-Ins/VST3");                                           // 系统级
                    AddUnder(folder(Environment.SpecialFolder.UserProfile), "Library", "Audio", "Plug-Ins", "VST3");
                    AddPath("/usr/local/lib/vst3");
                    break;
                case VstPathPlatform.Linux:
                    AddUnder(folder(Environment.SpecialFolder.UserProfile), ".vst3");
                    AddPath("/usr/lib/vst3");
                    AddPath("/usr/local/lib/vst3");
                    break;
            }
            // 去重（Windows/macOS 大小写不敏感；Linux 敏感）
            StringComparer comparer = platform == VstPathPlatform.Linux
                ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
            return paths.Distinct(comparer).ToList();
        }

        /// <summary>
        /// 把标准路径并入 <c>VstScanPaths</c>：**幂等**，只加"目录真实存在且尚未收录"的。
        /// 用户主动触发（按钮）时用它；返回新增条数。
        /// </summary>
        public static int AddStandardVstScanPaths(
            VstPathPlatform? platform = null,
            Func<Environment.SpecialFolder, string>? folder = null) {
            VstPathPlatform target = platform ?? CurrentVstPathPlatform();
            StringComparer comparer = target == VstPathPlatform.Linux
                ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
            int added = 0;
            foreach (string path in StandardVstScanPaths(target, folder)) {
                if (!Directory.Exists(path)) {
                    continue;   // 只播种真实存在的目录（避免往面板里塞死路径）
                }
                if (Default.VstScanPaths.Any(existing => comparer.Equals(existing, path))) {
                    continue;
                }
                Default.VstScanPaths.Add(path);
                added++;
            }
            if (added > 0) {
                Save();
            }
            return added;
        }

        /// <summary>
        /// **首次运行播种**：仅当 <c>VstScanPathsSeeded == false</c> 时执行一次，然后置位并落盘。
        /// 返回新增条数；<c>-1</c> 表示本次跳过（已播种过 ⇒ 用户删掉的路径不会被复活）。
        /// </summary>
        public static int SeedStandardVstScanPathsOnce(
            VstPathPlatform? platform = null,
            Func<Environment.SpecialFolder, string>? folder = null) {
            if (Default.VstScanPathsSeeded) {
                return -1;
            }
            Default.VstScanPathsSeeded = true;
            int added = AddStandardVstScanPaths(platform, folder);
            Save();     // 标记本身也要落盘（即使一条路径都没加）
            return added;
        }

        public static void SetSingerSearchPaths(List<string> paths) {
            Default.SingerSearchPaths = new List<string>(paths);
            Save();
        }

        public static List<string> GetSampleSearchPaths() {
            return new List<string>(Default.SampleSearchPaths);
        }

        public static void SetSampleSearchPaths(List<string> paths) {
            Default.SampleSearchPaths = new List<string>(paths);
            Save();
        }

        public static void AddRecentFileIfEnabled(string filePath){
            //Users can choose adding .ust, .vsqx and .mid files to recent files or not
            string ext = Path.GetExtension(filePath);
            switch(ext){
                case ".ustx":
                case ".ustxp":
                    AddRecentFile(filePath);
                    break;
                case ".mid":
                case ".midi":
                    if(Preferences.Default.RememberMid){
                        AddRecentFile(filePath);
                    }
                    break;
                case ".ust":
                    if(Preferences.Default.RememberUst){
                        AddRecentFile(filePath);
                    }
                    break;
                case ".vsqx":
                    if(Preferences.Default.RememberVsqx){
                        AddRecentFile(filePath);
                    }
                    break;
                default:
                    break;
            }
        }

        private static void AddRecentFile(string filePath) {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) {
                return;
            }
            var recent = Default.RecentFiles;
            recent.RemoveAll(f => f == filePath);
            recent.Insert(0, filePath);
            recent.RemoveAll(f => string.IsNullOrEmpty(f)
                || !File.Exists(f)
                || f.Contains(PathManager.Inst.TemplatesPath));
            if (recent.Count > 16) {
                recent.RemoveRange(16, recent.Count - 16);
            }
            Save();
        }

        private static void Load() {
            try {
                if (File.Exists(PathManager.Inst.PrefsFilePath)) {
                    Default = JsonConvert.DeserializeObject<SerializablePreferences>(
                        File.ReadAllText(PathManager.Inst.PrefsFilePath, Encoding.UTF8));
                    if(Default == null) {
                        Reset();
                        return;
                    }

                    if (!ValidString(new Action(() => CultureInfo.GetCultureInfo(Default.Language)))) Default.Language = string.Empty;
                    if (!ValidString(new Action(() => CultureInfo.GetCultureInfo(Default.SortingOrder)))) Default.SortingOrder = string.Empty;
                    if (!Renderers.getRendererOptions().Contains(Default.DefaultRenderer)) Default.DefaultRenderer = string.Empty;
                    if (!Onnx.getRunnerOptions().Contains(Default.OnnxRunner)) Default.OnnxRunner = string.Empty;
                    if (Default.Theme != null) {
                        Default.ThemeName = Default.Theme switch {
                            1 => "Dark",
                            _ => "Light"
                        };
                        Default.Theme = null;
                    }
                    // 历史 prefs 里可能存着空白歌手名（上游 d53af641）：按名查歌手会命中空串，
                    // 选中/收藏路径再取歌手元数据时崩溃。加载时过滤空白项。
                    Default.RecentSingers = Default.RecentSingers?
                        .Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();
                    Default.FavoriteSingers = Default.FavoriteSingers?
                        .Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();
                } else {
                    Reset();
                }
            } catch (Exception e) {
                Log.Error(e, "Failed to load prefs.");
                Default = new SerializablePreferences();
            }
        }

        private static bool ValidString(Action action) {
            try {
                action();
                return true;
            } catch {
                return false;
            }
        }

        [Serializable]
        public class SerializablePreferences {
            public WindowSize MainWindowSize = new WindowSize();
            public WindowSize PianorollWindowSize = new WindowSize();
            /// <summary>
            /// W16 面板系统：可调宽 / 可折叠面板的布局状态（宽 + 折叠态）。
            /// **不新增平行存储**——面板一律写这里；加新面板就在 <see cref="PanelLayoutPreferences"/> 里加一对字段。
            /// </summary>
            public PanelLayoutPreferences PanelLayout = new PanelLayoutPreferences();
            public int UndoLimit = 100;
            public List<string> SingerSearchPaths = new List<string>();
            public string PlaybackDevice = string.Empty;
            public int PlaybackDeviceNumber;
            public bool ShowPrefs = true;
            public bool ShowTips = true;
            /// <summary>减少界面动效（MD3 动效令牌全部归零；偏好页的开关待偏好页重做时加）。</summary>
            public bool ReduceMotion = false;
            public string ThemeName = "Dark";
            /// <summary>MD3 颜色池种子（决定整应用强调色阶；见决定文档 D4。默认 = M3 基线紫）。</summary>
            public uint ThemeSeed = 0xFF6750A4;
            public int DegreeStyle;
            public bool UseTrackColor = false;
            public bool ClearCacheOnQuit = false;
            public bool PreRender = true;
            public int NumRenderThreads = 2;
            public string DefaultRenderer = string.Empty;
            public int WorldlineR = 0;
            public string OnnxRunner = string.Empty;
            public int OnnxGpu = 0;
            public double DiffSingerDepth = 1.0;
            public int DiffSingerSteps = 20;
            public int DiffSingerStepsVariance = 20;
            public int DiffSingerStepsPitch = 10;
            public bool DiffSingerTensorCache = true;
            public bool DiffSingerVarianceLocalPitchPatch = false;
            public bool DiffSingerLangCodeHide = false;
            public bool Metronome = false;
            public bool SkipRenderingMutedTracks = false;
            public string Language = string.Empty;
            public string? SortingOrder = null;
            public List<string> RecentFiles = new List<string>();
            public string SkipUpdate = string.Empty;
            public string AdditionalSingerPath = string.Empty;
            public bool InstallToAdditionalSingersPath = true;
            public bool LoadDeepFolderSinger = true;
            public bool PreferCommaSeparator = false;
            public bool ResamplerLogging = false;
            public List<string> RecentSingers = new List<string>();
            public List<string> FavoriteSingers = new List<string>();
            public Dictionary<string, string> SingerPhonemizers = new Dictionary<string, string>();
            public List<string> RecentPhonemizers = new List<string>();
            public bool PreferPortAudio = false;
            public bool UseSystemDefaultAudioDevice = true;
            public double PlayPosMarkerMargin = 0.9;
            public int MetronomeVolume = 60;
            public int MetronomeHighFrequency = 2200;
            public int MetronomeLowFrequency = 1320;
            public int LockStartTime = 0;
            public int PlaybackAutoScroll = 2;
            public bool ReverseLogOrder = true;
            public bool ShowPortrait = true;
            public bool ShowIcon = true;
            public bool ShowGhostNotes = true;
            public bool NoteHoverGlow = true;
            public bool ShowPlaybackNoteHighlight = true;
            public bool ShowPlaybackNoteBounce = false;
            public EditTool EditTool = new EditTool();
            public bool PlayTone = true;
            public bool ShowVibrato = true;
            public bool ShowPitch = true;
            public bool ShowFinalPitch = true;
            public bool ShowWaveform = true;
            public bool ShowPhoneme = true;
            public bool ShowExpressions = true;
            public bool ShowPhonemizerTags = true;
            public bool ShowNoteParams = true;
            public Dictionary<string, string> DefaultResamplers = new Dictionary<string, string>();
            public Dictionary<string, string> DefaultWavtools = new Dictionary<string, string>();
            public string LyricHelper = string.Empty;
            public bool LyricsHelperBrackets = false;
            public int OtoEditor = 0;
            public string VLabelerPath = string.Empty;
            public string SetParamPath = string.Empty;
            public bool Beta = false;
            public bool RememberMid = false;
            public bool RememberUst = true;
            public bool RememberVsqx = true;
            public string WinePath = string.Empty;
            public bool DefaultSnapCurve = true;
            public string PhoneticAssistant = string.Empty;
            public string RecentOpenSingerDirectory = string.Empty;
            public string RecentOpenProjectDirectory = string.Empty;
            public bool LockUnselectedNotesPitch = true;
            public bool LockUnselectedNotesVibrato = true;
            public bool LockUnselectedNotesExpressions = true;
            public bool LyricLivePreview = true;
            public bool LyricApplySelectionOnly = true;
            public bool VoicebankPublishUseIgnore = true;
            public string VoicebankPublishIgnores = @"#Adobe Audition
*.pkf

#UTAU Engines
*.ctspec
*.d4c
*.dio
*.frc
*.frt
#*.frq
*.harvest
*.lessaudio
*.llsm
*.mrq
*.pitchtier
*.pkf
*.platinum
*.pmk
*.sc.npz
*.star
*.uspec
*.vs4ufrq

#UTAU related tools
\$read
*.setParam-Scache
*.lbp
*.lbp.caches/*

#OpenUtau
errors.txt
";
            public string RecoveryPath = string.Empty;
            // S5/A2：钢琴卷帘的**视图级**分离状态（true = 独立窗口，false = 工作区视图）。
            // 由偏好页开关与卷帘菜单翻转，分离/收回时 MainWindow 会同步写回并落盘。
            public bool DetachPianoRoll = false;

            // ----- Mix FX (post-processing) -----
            // Per-track FX state lives in UTrack.MixFx and the project ustx.
            // Preferences only stores the global "apply on mixdown export" toggle
            // and the user preset library (named full-rack snapshots).
            public bool MixFxApplyOnExportMixdown = true;
            public List<MixFxUserPreset> MixFxUserPresets = new List<MixFxUserPreset>();

            // OpenUTAU Plus: VST plugin scan paths
            public List<string> VstScanPaths = new();
            // OpenUTAU Plus (W11): 「标准扫描路径已播种」标记。
            // 只在**首次运行**把平台标准 VST3 目录并入 VstScanPaths；置位后永不再自动加，
            // 因此用户手动删掉标准路径不会被复活（播种是"建议默认值"，不是每次启动补齐）。
            public bool VstScanPathsSeeded = false;
            // OpenUTAU Plus: cached VST registry (avoids re-scan on restart)
            public List<VstCachedEntry> VstCachedPlugins = new();
            // OpenUTAU Plus: backing track (BGM) library scan paths
            public List<string> SampleSearchPaths = new();

            // ── Mixer attachment ────────────────────────
            // DetachMixer = 视图级「分离」状态（S5/A2）：true = 混音台在独立窗口里，
            // false = 混音台是主窗口工作区的一个视图。随窗口关闭/收回翻回 false 并落盘。
            public bool DetachMixer = false;
            // 默认值必须与 MixerWindow.axaml 的 720x480 一致：S5 起窗口关闭时会**恢复**这份尺寸，
            // 若沿用 WindowSize 的 1200x650 默认，首次分离就会得到一个比设计值大的窗口。
            public WindowSize MixerWindowSize = new WindowSize { Width = 720, Height = 480 };

            // Legacy
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public int? Theme;
        }

        /// <summary>
        /// W16 面板系统：可调宽 / 可折叠面板的持久化状态。
        ///
        /// 命名口径：`XxxWidth` = 展开时的宽度（px）；`XxxCollapsed` = 是否折叠（true 时列宽 0、不留空白）。
        /// **默认值一律"展开"**：素材库是插件浏览器入口（D9「从素材库拖进链面板」是主路径），
        /// 默认折叠会让这条路径不可发现；折叠是用户主动选择，然后持久化。
        ///
        /// 加新面板（例：混音台链面板、卷帘侧栏）：在这里加一对字段 + 在 MainWindowViewModel 里
        /// `new PanelSlot(...)` 一处 + XAML 里 `<c:PanelSplitter Target="{Binding XxxPanel.Width}"/>` 一行。
        /// </summary>
        public class PanelLayoutPreferences {
            /// <summary>工作台左列（轨道头）。默认 248：内容实测需 ~190-200（音量滑条 150 + 内边距 12×2），
            /// 248 留约 25% 余量，同时比设计稿 264 瘦 16px 让给编排区。</summary>
            public double TrackHeaderWidth = 248;
            public bool TrackHeaderCollapsed = false;

            /// <summary>工作台右列（素材库）。默认 272：34 缩略图 + 12 间距 + 12px 名称在 272 下不折行，
            /// 比设计稿 296 瘦 24px；展开态下卡片与「N 已安装」计数仍完整。</summary>
            public double LibraryWidth = 272;
            public bool LibraryCollapsed = false;

            /// <summary>混音台右侧效果链面板（W19 接入）。默认 280：链行解剖实测固定件 136
            /// （把手 24 + 序号 24 + 旁通 44 + 移除 22 + 内边距/边框 22）⇒ 280 给"名称+徽标"列约 115px，
            /// 长 VST 名可读且不挤；Min 264 保住名称列 ~99px，Max 480 让最长名+副标题+状态角标全展开。</summary>
            public double MixerChainWidth = 280;
            public bool MixerChainCollapsed = false;
        }

        /// <summary>
        /// Named full-rack FX snapshot (EQ + Comp + Reverb together).
        /// Persisted in Preferences so users can save and recall their own presets.
        /// </summary>
        public class MixFxUserPreset {
            public string Name { get; set; } = string.Empty;
            public Ustx.UMixFx Fx { get; set; } = new Ustx.UMixFx();
        }

        /// <summary>
        /// Cached VST registry entry — persisted in Preferences to avoid re-scanning on restart.
        /// </summary>
        public class VstCachedEntry {
            public string Uid { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Vendor { get; set; } = string.Empty;
            public string Path { get; set; } = string.Empty;
            public int Type { get; set; }
            public List<string> Subs { get; set; } = new();
        }
    }
}

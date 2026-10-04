using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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

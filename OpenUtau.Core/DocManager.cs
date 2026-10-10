using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Api;
using OpenUtau.Classic;
using OpenUtau.Core.Editing;
using OpenUtau.Core.Lib;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core {
    public struct ValidateOptions {
        public bool SkipTiming;
        public UPart Part;
        public bool SkipPhonemizer;
        public bool SkipPhoneme;
    }

    public class DocManager : SingletonBase<DocManager> {
        DocManager() {
            Project = new UProject();
        }

        private Thread mainThread;
        private TaskScheduler mainScheduler;

        public int playPosTick = 0;
        public int rangeStartTick = 0;
        public int rangeEndTick = 0;

        public TaskScheduler MainScheduler => mainScheduler;
        public Action<Action> PostOnUIThread { get; set; }
        public Plugin[] Plugins { get; private set; }
        public PhonemizerFactory[] PhonemizerFactories { get; private set; }
        public UProject Project { get; private set; }
        public bool HasOpenUndoGroup => undoGroup != null;
        public List<UPart>? PartsClipboard { get; set; }
        public List<UNote>? NotesClipboard { get; set; }
        public CurveSelection? CurvesClipboard { get; set; }
        internal PhonemizerRunner PhonemizerRunner { get; private set; }
        public List<Type> ExternalBatchEditTypes { get; private set; } = new List<Type>();

        public void Initialize(Thread mainThread, TaskScheduler mainScheduler) {
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler((sender, args) => {
                CrashSave();
            });
            SearchAllPlugins();
            SearchAllLegacyPlugins();
            this.mainThread = mainThread;
            this.mainScheduler = mainScheduler;
            PhonemizerRunner = new PhonemizerRunner(mainScheduler);
        }

        public void SearchAllLegacyPlugins() {
            try {
                var stopWatch = Stopwatch.StartNew();
                Plugins = PluginLoader.LoadAll(PathManager.Inst.PluginsPath);
                stopWatch.Stop();
                Log.Information($"Search all legacy plugins: {stopWatch.Elapsed}");
            } catch (Exception e) {
                Log.Error(e, "Failed to search legacy plugins.");
                Plugins = new Plugin[0];
            }
        }

        public void SearchAllPlugins() {
            const string kBuiltin = "OpenUtau.Plugin.Builtin.dll";
            var stopWatch = Stopwatch.StartNew();
            var files = new List<string>();
            try {
                files.Add(Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory), kBuiltin));
                Directory.CreateDirectory(PathManager.Inst.PluginsPath);
                string oldBuiltin = Path.Combine(PathManager.Inst.PluginsPath, kBuiltin);
                if (File.Exists(oldBuiltin)) {
                    File.Delete(oldBuiltin);
                }
                SearchPluginInternal(PathManager.Inst.PluginsPath, files);
            } catch (Exception e) {
                Log.Error(e, "Failed to search plugins.");
            }
            foreach (var file in files) {
                Assembly assembly;
                try {
                    if (!LibraryLoader.IsManagedAssembly(file)) {
                        Log.Information($"Skipping {file}");
                        continue;
                    }
                    assembly = Assembly.LoadFile(file);
                    foreach (var type in assembly.GetExportedTypes()) {
                        if (!type.IsAbstract && type.IsSubclassOf(typeof(Phonemizer))) {
                            PhonemizerFactory.Get(type);
                        }
                        if (Path.GetFileName(file) != kBuiltin
                            && typeof(BatchEdit).IsAssignableFrom(type)
                            && !type.IsInterface
                            && !type.IsAbstract
                            && type.GetConstructor(Type.EmptyTypes) != null) {
                            ExternalBatchEditTypes.Add(type);
                        }
                    }
                } catch (Exception e) {
                    Log.Warning(e, $"Failed to load {file}.");
                    continue;
                }
            }
            foreach (var type in GetType().Assembly.GetExportedTypes()) {
                if (!type.IsAbstract && type.IsSubclassOf(typeof(Phonemizer))) {
                    PhonemizerFactory.Get(type);
                }
            }
            PhonemizerFactory.BuildList();
            stopWatch.Stop();
            Log.Information($"Search all plugins: {stopWatch.Elapsed}");
        }
        private void SearchPluginInternal(string path, List<string> result) {
            if (Directory.EnumerateFiles(path, "plugin.txt", SearchOption.TopDirectoryOnly).Any()) {
                return;
            }
            result.AddRange(Directory.EnumerateFiles(path, "*.dll", SearchOption.TopDirectoryOnly));
            var directories = Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly);
            foreach (var directory in directories) {
                SearchPluginInternal(directory, result);
            }
        }

        #region Command Queue

        readonly Deque<UCommandGroup> undoQueue = new Deque<UCommandGroup>();
        readonly Deque<UCommandGroup> redoQueue = new Deque<UCommandGroup>();
        UCommandGroup? undoGroup = null;
        UCommandGroup? savedPoint = null;
        UCommandGroup? autosavedPoint = null;
        public bool Recovered { get; set; } = false; // Flag to not overwrite backup file

        public bool ChangesSaved {
            get {
                return (Project.Saved || (Project.tracks.Count <= 1 && Project.parts.Count == 0)) &&
                    (undoQueue.Count > 0 && savedPoint == undoQueue.Last() || undoQueue.Count == 0 && savedPoint == null) &&
                    !Recovered && (undoGroup == null || undoGroup.Commands.Count == 0);
            }
        }

        public UCommandGroup? ActiveUndoGroup => undoGroup;

        /// <summary>
        /// 标记工程已修改——混音台推子/声像/Mute/Solo、MixFx 滑杆等**直接改模型**
        /// （不进 undo 队列）的路径必须调用：否则 ChangesSaved 误判 true →
        /// 退出不提示保存、30s autosave 跳过 → 静默丢 mixer 改动。
        /// </summary>
        public void MarkProjectModified() {
            if (Project == null) return;
            Project.Saved = false;
            // 哨兵：使 autosave 跳过条件（undoQueue.LastOrDefault() == autosavedPoint）失效
            autosavedPoint = new UCommandGroup(null, false);
        }


        private void CrashSave() {
            try {
                if (Project == null) {
                    Log.Warning("Crash save project is null.");
                    return;
                }
                bool untitled = string.IsNullOrEmpty(Project.FilePath);
                if (untitled) {
                    Directory.CreateDirectory(PathManager.Inst.BackupsPath);
                }
                string dir = untitled
                    ? PathManager.Inst.BackupsPath
                    : Path.GetDirectoryName(Project.FilePath);
                string filename = untitled
                    ? "Untitled"
                    : Path.GetFileNameWithoutExtension(Project.FilePath);
                string backup = Path.Join(dir, filename + "-backup.ustxp");
                Log.Information($"Saving backup {backup}.");
                if (Format.Ustxp.TryAutoSave(backup, Project)) {
                    Log.Information($"Saved backup {backup}.");
                } else {
                    Log.Warning("Backup was not persisted: {Path}", backup);
                }
            } catch (Exception e) {
                Log.Error(e, "Save backup failed.");
            }
        }

        public void AutoSave() {
            if (Project == null) {
                return;
            }
            if (undoQueue.LastOrDefault() == autosavedPoint && (undoGroup == null || undoGroup.Commands.Count == 0)) {
                Log.Information("Autosave skipped.");
                return;
            }
            try {
                bool untitled = string.IsNullOrEmpty(Project.FilePath);
                if (untitled) {
                    Directory.CreateDirectory(PathManager.Inst.BackupsPath);
                }
                string dir = untitled
                    ? PathManager.Inst.BackupsPath
                    : Path.GetDirectoryName(Project.FilePath);
                string filename = untitled
                    ? "Untitled"
                    : Path.GetFileNameWithoutExtension(Project.FilePath);

                string backup = Path.Join(dir, filename + "-autosave.ustxp");
                Log.Information($"Autosave {backup}.");
                if (Format.Ustxp.TryAutoSave(backup, Project)) {
                    Log.Information($"Autosaved {backup}.");
                    autosavedPoint = undoQueue.LastOrDefault();
                } else {
                    Log.Warning("Autosave was not persisted; will retry: {Path}", backup);
                }
            } catch (Exception e) {
                Log.Error(e, "Autosave failed.");
            }
        }

        /// <summary>
        /// 命令统一入口。**命令层统一锚定所属线程**：非主线程调用一律经
        /// <see cref="PostOnUIThread"/> 投递回 UI 线程，而不是让每个调用点自己包一层
        /// 调度（上游 832aea2c 是在 7 处渲染回调里逐点包 <c>Task.StartNew(MainScheduler)</c>；
        /// 那种做法漏一处就复发，且 MainScheduler 为 null 时 StartNew 会直接抛，
        /// 故此处保留我们的集中口径，只补"投递通道缺失"这一个缺口）。
        /// </summary>
        public void ExecuteCmd(UCommand cmd) {
            if (mainThread != Thread.CurrentThread) {
                if (!(cmd is ProgressBarNotification)) {
                    Log.Warning($"{cmd} not on main thread");
                }
                if (PostOnUIThread != null) {
                    PostOnUIThread(() => ExecuteCmd(cmd));
                } else {
                    // 无头/测试宿主/启动早期可能还没装投递通道：就地执行，绝不 NRE。
                    Log.Warning("PostOnUIThread is not set; executing the command inline.");
                    ExecuteCmdCore(cmd);
                }
                return;
            }
            ExecuteCmdCore(cmd);
        }

        private void ExecuteCmdCore(UCommand cmd) {
            if (cmd is UNotification) {
                if (cmd is SaveProjectNotification saveProjectNotif) {
                    if (undoGroup != null) EndUndoGroup();
                    string path = string.IsNullOrEmpty(saveProjectNotif.Path) ? Project.FilePath : saveProjectNotif.Path;
                    if (Format.Ustxp.Save(path, Project)) {
                        savedPoint = undoQueue.LastOrDefault();
                    }
                } else if (cmd is LoadProjectNotification notification) {
                    undoQueue.Clear();
                    redoQueue.Clear();
                    undoGroup = null;
                    savedPoint = null;
                    autosavedPoint = null;
                    Project = notification.project;
                    playPosTick = 0;
                    rangeStartTick = 0;
                    rangeEndTick = 0;
                } else if (cmd is SetPlayPosTickNotification setPlayPosTickNotif) {
                    playPosTick = setPlayPosTickNotif.playPosTick;
                } else if (cmd is SetRangeSelectionNotification setRange) {
                    rangeStartTick = setRange.startTick;
                    rangeEndTick = setRange.endTick;
                } else if (cmd is SingersChangedNotification) {
                    SingerManager.Inst.SearchAllSingers();
                } else if (cmd is ValidateProjectNotification) {
                    Project.ValidateFull();
                } else if (cmd is SingersRefreshedNotification || cmd is OtoChangedNotification) {
                    foreach (var track in Project.tracks) {
                        track.OnSingerRefreshed();
                    }
                    Project.ValidateFull();
                    if (cmd is OtoChangedNotification) {
                        ExecuteCmd(new PreRenderNotification());
                    }
                }
                Publish(cmd);
                if (!cmd.Silent) {
                    Log.Information($"Publish notification {cmd}");
                }
                return;
            }
            if (undoGroup == null) {
                Log.Error($"No active UndoGroup {cmd}");
                return;
            }
            undoGroup.Commands.Add(cmd);
            lock (Project) {
                cmd.Execute();
            }
            if (!cmd.Silent) {
                Log.Information($"ExecuteCmd {cmd}");
            }
            Publish(cmd);
            if (!undoGroup.DeferValidate && cmd.AffectsSynthesis) {
                Project.Validate(cmd.ValidateOptions);
            }
        }

        public void StartUndoGroup(string? nameKey = null, bool deferValidate = false) {
            if (undoGroup != null) {
                Log.Error("undoGroup already started");
                EndUndoGroup();
            }
            undoGroup = new UCommandGroup(nameKey, deferValidate);
            Log.Information("undoGroup started");
        }

        public void EndUndoGroup() {
            if (undoGroup == null) {
                Log.Error("No active undoGroup to end.");
                return;
            }
            if (undoGroup.Commands.Count > 0) {
                undoQueue.AddToBack(undoGroup);
                redoQueue.Clear();
            }
            while (undoQueue.Count > Util.Preferences.Default.UndoLimit) {
                undoQueue.RemoveFromFront();
            }
            bool affectsSynthesis = undoGroup.Commands.Any(cmd => cmd.AffectsSynthesis);
            if (undoGroup.DeferValidate && affectsSynthesis) {
                Project.ValidateFull();
            }
            undoGroup.Merge();
            undoGroup = null;
            Log.Information("undoGroup ended");
            Publish(new CommandHistoryChangedNotification());
            if (affectsSynthesis) ExecuteCmd(new PreRenderNotification());
        }

        public void RollBackUndoGroup() {
            if (undoGroup == null) {
                Log.Error("No active undoGroup to rollback.");
                return;
            }
            for (int i = undoGroup.Commands.Count - 1; i >= 0; i--) {
                var cmd = undoGroup.Commands[i];
                cmd.Unexecute();
                if (i == 0 && undoGroup.Commands.Any(c => c.AffectsSynthesis)) {
                    Project.ValidateFull();
                }
                Publish(cmd, true);
            }
            undoGroup.Commands.Clear();
        }

        public void Undo() {
            if (undoGroup != null) EndUndoGroup();
            if (undoQueue.Count == 0) {
                return;
            }
            var group = undoQueue.RemoveFromBack();
            for (int i = group.Commands.Count - 1; i >= 0; i--) {
                var cmd = group.Commands[i];
                cmd.Unexecute();
                if (i == 0 && group.Commands.Any(c => c.AffectsSynthesis)) {
                    Project.ValidateFull();
                }
                Publish(cmd, true);
            }
            redoQueue.AddToBack(group);
            Publish(new CommandHistoryChangedNotification());
            if (group.Commands.Any(c => c.AffectsSynthesis)) ExecuteCmd(new PreRenderNotification());
        }

        public void Redo() {
            if (undoGroup != null) EndUndoGroup();
            if (redoQueue.Count == 0) {
                return;
            }
            var group = redoQueue.RemoveFromBack();
            for (var i = 0; i < group.Commands.Count; i++) {
                var cmd = group.Commands[i];
                cmd.Execute();
                if (i == group.Commands.Count - 1 && group.Commands.Any(c => c.AffectsSynthesis)) {
                    Project.ValidateFull();
                }
                Publish(cmd);
            }
            undoQueue.AddToBack(group);
            Publish(new CommandHistoryChangedNotification());
            if (group.Commands.Any(c => c.AffectsSynthesis)) ExecuteCmd(new PreRenderNotification());
        }

        public bool GetUndoState(out string? key) {
            key = null;
            if (undoGroup?.Commands.Count > 0) {
                key = undoGroup.NameKey;
                return true;
            } else if (undoQueue.Count > 0) {
                key = undoQueue.Last().NameKey;
                return true;
            } else {
                return false;
            }
        }
        public bool GetRedoState(out string? key) {
            key = null;
            if (undoGroup?.Commands.Count > 0) return false;
            if (redoQueue.Count > 0) {
                key = redoQueue.Last().NameKey;
                return true;
            } else {
                return false;
            }
        }

        # endregion

        # region Command Subscribers

        private readonly object lockObj = new object();
        private readonly List<ICmdSubscriber> subscribers = new List<ICmdSubscriber>();

        public void AddSubscriber(ICmdSubscriber sub) {
            lock (lockObj) {
                if (!subscribers.Contains(sub)) {
                    subscribers.Add(sub);
                }
            }
        }

        public void RemoveSubscriber(ICmdSubscriber sub) {
            lock (lockObj) {
                if (subscribers.Contains(sub)) {
                    subscribers.Remove(sub);
                }
            }
        }

        private void Publish(UCommand cmd, bool isUndo = false) {
            if (cmd is LoadProjectNotification || cmd is TrackCommand) {
                TrackMixCommands.RefreshMuted(Project.tracks);
            }
            ICmdSubscriber[] snapshot;
            lock (lockObj) snapshot = subscribers.ToArray();
            // Track/view rebuilding can subscribe or dispose while receiving this command.
            foreach (var sub in snapshot) sub.OnNext(cmd, isUndo);
            if (cmd is TrackMixCommand mix) {
                // Notify the audio engine and views on execute AND undo/redo.
                foreach (var track in mix.Tracks) {
                    if (mix.Property is TrackMixProperty.Volume or TrackMixProperty.Mute or TrackMixProperty.Solo) {
                        Publish(new VolumeChangeNotification(track.TrackNo, track.Muted ? -24 : track.Volume));
                    } else if (mix.Property == TrackMixProperty.Pan) {
                        Publish(new PanChangeNotification(track.TrackNo, track.Pan * 100));
                    }
                }
                Publish(new TrackMixChangedNotification(Project, mix.Tracks), isUndo);
            } else if (cmd is TrackCommand) {
                foreach (var track in Project.tracks) {
                    Publish(new VolumeChangeNotification(track.TrackNo, track.Muted ? -24 : track.Volume));
                }
                Publish(new TrackMixChangedNotification(Project, Project.tracks.ToArray()), isUndo);
            }
        }

        #endregion
    }
}

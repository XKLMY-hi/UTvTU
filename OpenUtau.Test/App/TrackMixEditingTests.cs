using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenUtau.App.Editing;
using OpenUtau.App.ViewModels;
using OpenUtau.App.Views;
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.Theming;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtau.Test.TestSupport;
using OpenUtau.Theming;
using Xunit;

namespace OpenUtau.Test.App {
    [CollectionDefinition("TrackMixEditing", DisableParallelization = true)]
    public class TrackMixEditingCollection { }

    [Collection("TrackMixEditing")]
    public class TrackMixEditingTests : IDisposable {
        readonly DocManagerTestSetup.ScopedDispatcher dispatcher;
        readonly string directory = Path.Combine(Path.GetTempPath(), "utvtu-mix-" + Guid.NewGuid().ToString("N"));
        readonly string previousDataPath;
        readonly string previousRecoveryPath;
        readonly UProject previousProject;
        readonly PropertyInfo dataPath = typeof(PathManager).GetProperty(nameof(PathManager.DataPath))!;
        DocManager Doc => DocManager.Inst;

        public TrackMixEditingTests() {
            dispatcher = DocManagerTestSetup.EnterScopedDispatcher(nullChannel: true, installScheduler: false);
            previousProject = Doc.Project;
            previousDataPath = PathManager.Inst.DataPath;
            previousRecoveryPath = Preferences.Default.RecoveryPath;
            Directory.CreateDirectory(directory);
            dataPath.SetValue(PathManager.Inst, directory);
            ColorPool.Initialize(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, true);
        }

        UProject Load() {
            var project = new UProject { Saved = true };
            project.tracks.Add(new UTrack("Harmony") { TrackNo = 1 });
            Doc.ExecuteCmd(new LoadProjectNotification(project));
            Assert.True(Doc.ChangesSaved);
            return project;
        }

        string Save(string name = "song") {
            string path = Path.Combine(directory, name + ".ustxp");
            Doc.ExecuteCmd(new SaveProjectNotification(path));
            Assert.True(Doc.ChangesSaved);
            Assert.True(File.Exists(path));
            return path;
        }

        [AvaloniaFact]
        public void RejectedHeaderAndRackEdits_RestoreTheDisplayedModelValues() {
            var track = Load().tracks[0];
            track.MixFx = new UMixFx { Enabled = true, EqLowDb = 2 };
            using var header = new TrackHeaderViewModel(track);
            using var rack = new MixFxViewModel(track);
            Doc.StartUndoGroup();
            var foreign = Doc.ActiveUndoGroup;
            header.Volume = -8;
            header.Pan = 40;
            rack.EqLowDb = 6;
            Assert.Equal(0, header.Volume);
            Assert.Equal(0, header.Pan);
            Assert.Equal(2, rack.EqLowDb);
            Assert.Equal(2, track.MixFx.EqLowDb);
            Assert.Same(foreign, Doc.ActiveUndoGroup);
            Assert.Empty(foreign!.Commands);
            Doc.EndUndoGroup();
        }

        [AvaloniaFact]
        public void MixGesture_RejectsAndPreservesAnotherControlsTransaction() {
            var track = Load().tracks[0];
            using var edits = new TrackMixEditSession(track);
            Doc.StartUndoGroup();
            var foreign = Doc.ActiveUndoGroup;
            edits.BeginGesture();
            Assert.False(edits.SetVolume(-6));
            edits.EndGesture();
            Assert.Same(foreign, Doc.ActiveUndoGroup);
            Assert.Empty(foreign!.Commands);
            Assert.Equal(0, track.Volume);
            Doc.EndUndoGroup();
            Assert.True(edits.SetVolume(-3));
            Assert.Null(Doc.ActiveUndoGroup);
            Doc.Undo();
            Assert.Equal(0, track.Volume);
        }

        [AvaloniaFact]
        public void MixGesture_AfterSaveSeal_CannotCloseAnInterveningTransaction() {
            var track = Load().tracks[0];
            using var edits = new TrackMixEditSession(track);
            edits.BeginGesture();
            Assert.True(edits.SetVolume(-3));
            Save();
            Doc.StartUndoGroup();
            var foreign = Doc.ActiveUndoGroup;
            Assert.False(edits.SetVolume(-8));
            edits.Dispose();
            Assert.Same(foreign, Doc.ActiveUndoGroup);
            Assert.Equal(-3, track.Volume);
            Assert.Empty(foreign!.Commands);
            Doc.EndUndoGroup();
        }

        [AvaloniaFact]
        public void FaderGesture_IsOneUndo_AndDoesNotRegenerateSinging() {
            var project = Load();
            var track = project.tracks[0];
            using var recorder = new Recorder();
            using var edits = new TrackMixEditSession(track);
            edits.BeginGesture();
            edits.SetVolume(-2);
            edits.SetVolume(-5);
            edits.SetVolume(-9);
            Assert.False(Doc.ChangesSaved); // even before pointer release
            Assert.True(Doc.GetUndoState(out _)); // Ctrl+Z must be enabled on the first drag
            edits.EndGesture();
            Doc.Undo();
            Assert.Equal(0, track.Volume);
            Assert.True(Doc.ChangesSaved);
            Assert.False(Doc.GetUndoState(out _));
            Doc.Redo();
            Assert.Equal(-9, track.Volume);
            Assert.False(Doc.ChangesSaved);
            Assert.DoesNotContain(recorder.Commands, cmd => cmd is PreRenderNotification);
            Assert.Contains(recorder.Commands, cmd => cmd is VolumeChangeNotification);
        }

        [AvaloniaFact]
        public void SaveDuringGesture_SplitsUndoAtTheSavedValue() {
            var track = Load().tracks[0];
            using var edits = new TrackMixEditSession(track);
            edits.BeginGesture();
            edits.SetVolume(-3);
            string path = Save();
            edits.SetVolume(-7);
            edits.EndGesture();
            Assert.False(Doc.ChangesSaved);
            Doc.Undo();
            Assert.Equal(-3, track.Volume);
            Assert.True(Doc.ChangesSaved);
            Assert.Equal(-3, Ustxp.Load(path).tracks[0].Volume);
        }

        [AvaloniaFact]
        public void UndoDuringGesture_RevertsTheCurrentDrag() {
            var track = Load().tracks[0];
            using var edits = new TrackMixEditSession(track);
            edits.SetPan(20);
            edits.BeginGesture();
            edits.SetVolume(-8);
            Doc.Undo();
            Assert.Equal(0, track.Volume);
            Assert.Equal(.2, track.Pan);
            edits.EndGesture();
            Doc.Redo();
            Assert.Equal(-8, track.Volume);
        }

        [AvaloniaFact]
        public void SoloAndMute_UndoRestoresPeers_WithoutMountedHeaders() {
            var project = Load();
            var lead = project.tracks[0];
            var harmony = project.tracks[1];
            using var leadEdits = new TrackMixEditSession(lead);
            using var harmonyEdits = new TrackMixEditSession(harmony);
            harmonyEdits.SetSolo(true);
            Save();
            leadEdits.SetSolo(true);
            Assert.True(lead.Solo);
            Assert.False(harmony.Solo);
            Assert.True(harmony.Muted);
            Doc.Undo();
            Assert.False(lead.Solo);
            Assert.True(harmony.Solo);
            Assert.True(lead.Muted);
            Assert.True(Doc.ChangesSaved);
            harmonyEdits.SetMute(true);
            Assert.True(harmony.Mute);
            Assert.False(harmony.Muted); // solo overrides explicit mute
            Doc.Undo();
            Assert.False(harmony.Mute);
        }

        [AvaloniaFact]
        public void MountedHeaders_ReadUndoState_WithoutAddingHistory() {
            var track = Load().tracks[0];
            using var header = new TrackHeaderViewModel(track);
            var strip = new MixerTrackStripViewModel(track);
            using var edits = strip.Edits;
            Assert.True(Doc.ChangesSaved);
            Assert.False(Doc.GetUndoState(out _));
            strip.ApplyVolume(-6);
            Assert.Equal(-6, header.Volume);
            Doc.Undo();
            Assert.Equal(0, header.Volume);
            Assert.True(Doc.ChangesSaved);
            Assert.False(Doc.GetUndoState(out _));
            header.Pan = 35;
            Assert.Equal(.35, track.Pan);
            Doc.Undo();
            Assert.Equal(0, header.Pan);
            Assert.True(Doc.ChangesSaved);
        }

        [AvaloniaFact]
        public void HeaderSliderGesture_IsOneUndo_AndDisposedSessionCannotWrite() {
            var track = Load().tracks[0];
            using var header = new TrackHeaderViewModel(track);
            header.BeginMixGesture();
            header.Volume = -1;
            header.Volume = -4;
            header.Volume = -9;
            header.EndMixGesture();
            Doc.Undo();
            Assert.Equal(0, track.Volume);
            Assert.True(Doc.ChangesSaved);
            Assert.False(Doc.GetUndoState(out _));
            header.Dispose();
            header.Volume = -15;
            Assert.Equal(0, track.Volume);
            Assert.True(Doc.ChangesSaved);
        }

        [AvaloniaFact]
        public void Effects_SaveThenCancel_RemainsDirty_AndUndoRecoversSavedPreview() {
            var track = Load().tracks[0];
            track.MixFx = new UMixFx { Enabled = true, EqLowDb = 0 };
            using var rack = new MixFxViewModel(track);
            rack.EqLowDb = 4;
            string path = Save();
            rack.Revert();
            Assert.Equal(0, track.MixFx!.EqLowDb);
            Assert.False(Doc.ChangesSaved);
            Assert.Equal(4, Ustxp.Load(path).tracks[0].MixFx!.EqLowDb);
            Doc.Undo();
            Assert.Equal(4, track.MixFx.EqLowDb);
            Assert.Equal(4, rack.EqLowDb);
            Assert.True(Doc.ChangesSaved);
            Doc.Redo();
            Assert.Equal(0, rack.EqLowDb);
            Save();
            Assert.Equal(0, Ustxp.Load(path).tracks[0].MixFx!.EqLowDb);
        }

        [AvaloniaFact]
        public void OldRack_CloseAfterProjectSwitch_CannotDirtyTheNewDocument() {
            var track = Load().tracks[0];
            MixFxDialog.Open(null, track);
            var windows = (Dictionary<UTrack, MixFxDialog>)typeof(MixFxDialog)
                .GetField("open", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var window = windows[track];
            var rack = Assert.IsType<MixFxViewModel>(window.DataContext);
            rack.Edits!.BeginGesture();
            rack.EqLowDb = 5;
            var newProject = Load();
            rack.EqLowDb = 8; // a late binding update from the old view
            rack.Revert();
            Doc.StartUndoGroup("new document gesture");
            var newGroup = Doc.ActiveUndoGroup;
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsVisible);
            Assert.Same(newGroup, Doc.ActiveUndoGroup);
            Assert.Same(newProject, Doc.Project);
            Assert.True(Doc.ChangesSaved);
            Assert.Null(newProject.tracks[0].MixFx);
            Doc.EndUndoGroup();
        }

        [AvaloniaFact]
        public void FailedSave_PreservesDirtyState_PreviousPath_AndDiskContents() {
            var track = Load().tracks[0];
            string path = Save();
            string originalFile = File.ReadAllText(path);
            using var edits = new TrackMixEditSession(track);
            edits.SetVolume(-12);
            string blockedPath = Path.Combine(directory, "blocked.ustxp");
            Directory.CreateDirectory(blockedPath);
            using var recorder = new Recorder();
            Doc.ExecuteCmd(new SaveProjectNotification(blockedPath));
            Assert.False(Doc.ChangesSaved);
            Assert.Equal(path, Doc.Project.FilePath);
            Assert.Equal(originalFile, File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            Assert.Contains(recorder.Commands, cmd => cmd is ErrorMessageNotification);
            Doc.Undo();
            Assert.True(Doc.ChangesSaved);
        }

        [AvaloniaFact]
        public void MixFxModuleToggle_DoesNotMutateTheSavedSnapshot() {
            var track = Load().tracks[0];
            var original = new UMixFx { Enabled = true, EqEnabled = true };
            track.MixFx = original;
            Doc.StartUndoGroup();
            Doc.ExecuteCmd(TrackMixCommands.SetMixFxModule(track, MixFxModule.Eq, false));
            Doc.EndUndoGroup();
            Assert.True(original.EqEnabled);
            Assert.False(track.MixFx!.EqEnabled);
            Doc.Undo();
            Assert.Same(original, track.MixFx);
            Assert.True(Doc.ChangesSaved);
        }

        [AvaloniaFact]
        public void TrackChange_CanRebuildSubscribedViewsDuringDispatch() {
            var project = Load();
            TrackHeaderViewModel? header = new(project.tracks[0]);
            using var subscriber = new ViewRebuilder(() => {
                header.Dispose();
                header = new TrackHeaderViewModel(project.tracks[0]);
            });
            try {
                Doc.StartUndoGroup();
                Doc.ExecuteCmd(new AddTrackCommand(project, new UTrack("New")));
                Doc.EndUndoGroup();
                Assert.Equal(3, project.tracks.Count);
                Doc.Undo();
                Assert.Equal(2, project.tracks.Count);
            } finally {
                header.Dispose();
            }
        }

        public void Dispose() {
            if (Doc.ActiveUndoGroup != null) Doc.EndUndoGroup();
            Doc.ExecuteCmd(new LoadProjectNotification(previousProject));
            Preferences.Default.RecoveryPath = previousRecoveryPath;
            dataPath.SetValue(PathManager.Inst, previousDataPath);
            dispatcher.Dispose();
            Directory.Delete(directory, recursive: true);
        }

        sealed class Recorder : ICmdSubscriber, IDisposable {
            public readonly List<UCommand> Commands = new();
            public Recorder() => DocManager.Inst.AddSubscriber(this);
            public void OnNext(UCommand cmd, bool isUndo) => Commands.Add(cmd);
            public void Dispose() => DocManager.Inst.RemoveSubscriber(this);
        }

        sealed class ViewRebuilder : ICmdSubscriber, IDisposable {
            readonly Action rebuild;
            public ViewRebuilder(Action rebuild) {
                this.rebuild = rebuild;
                DocManager.Inst.AddSubscriber(this);
            }
            public void OnNext(UCommand cmd, bool isUndo) {
                if (cmd is TrackCommand) rebuild();
            }
            public void Dispose() => DocManager.Inst.RemoveSubscriber(this);
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using OpenUtau.App.Controls;
using OpenUtau.App.Commands;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.App {
    [Collection("WorkspaceSession")]
    public class TrackActionOwnershipTests : IDisposable {
        readonly DocManagerTestSetup.ScopedDispatcher dispatcher =
            DocManagerTestSetup.EnterScopedDispatcher(nullChannel: true, installScheduler: false);
        readonly UProject previous = DocManager.Inst.Project;
        readonly string previousDataPath = PathManager.Inst.DataPath;
        readonly Preferences.SerializablePreferences previousPreferences = Preferences.Default;
        readonly string directory = Path.Combine(Path.GetTempPath(), "utvtu-track-actions-" + Guid.NewGuid().ToString("N"));
        static readonly PropertyInfo DataPath = typeof(PathManager).GetProperty(nameof(PathManager.DataPath))!;
        DocManager Doc => DocManager.Inst;

        public TrackActionOwnershipTests() {
            Directory.CreateDirectory(directory);
            DataPath.SetValue(PathManager.Inst, directory);
            Preferences.Default = new Preferences.SerializablePreferences();
        }

        UProject Load() {
            var project = new UProject { Saved = true };
            project.tracks.Add(new UTrack("Harmony") { TrackNo = 1 });
            project.parts.Add(new UVoicePart { Duration = 1920, trackNo = 0 });
            Doc.ExecuteCmd(new LoadProjectNotification(project));
            return project;
        }

        [AvaloniaFact]
        public void HeaderActionsCannotAppendToOrEndAnotherControlsTransaction() {
            var project = Load();
            var track = project.tracks[0];
            using var header = new TrackHeaderViewModel(track);
            var name = track.TrackName;
            var singer = track.Singer;
            var renderer = track.RendererSettings;
            Doc.StartUndoGroup();
            var foreign = Doc.ActiveUndoGroup;
            header.Remove(); header.MoveDown(); header.Duplicate(); header.DuplicateSettings();
            header.RenameTo("Rejected");
            header.SelectRendererCommand.Execute("Rejected").Subscribe();
            header.SelectSingerCommand.Execute(USinger.CreateMissing("Rejected")).Subscribe();
            header.SelectPhonemizerCommand.Execute(new OpenUtau.Api.PhonemizerFactory {
                type = typeof(OpenUtau.Api.Phonemizer),
            }).Subscribe();
            Assert.Same(foreign, Doc.ActiveUndoGroup);
            Assert.Empty(foreign!.Commands);
            Assert.Equal(2, project.tracks.Count);
            Assert.Same(track, project.tracks[0]);
            Assert.Equal(name, track.TrackName);
            Assert.Equal(name, header.TrackName);
            Assert.Same(singer, track.Singer);
            Assert.Same(renderer, track.RendererSettings);
        }

        [AvaloniaFact]
        public void HeaderAndColorCallbacksRejectRemovedOrReplacedDocumentTargets() {
            var old = Load();
            var track = old.tracks[0];
            using var header = new TrackHeaderViewModel(track);
            var color = new TrackColorViewModel(track);
            color.SelectedColor = color.TrackColors.First(c => c.Name != track.TrackColor);
            var next = Load();
            Doc.StartUndoGroup();
            var foreign = Doc.ActiveUndoGroup;
            header.RenameTo("Late callback"); header.Remove(); header.MoveUp(); header.Duplicate();
            color.Finish();
            Assert.NotEqual("Late callback", track.TrackName);
            Assert.NotEqual(color.SelectedColor.Name, track.TrackColor);
            Assert.Equal(2, old.tracks.Count);
            Assert.Equal(2, next.tracks.Count);
            Assert.Same(foreign, Doc.ActiveUndoGroup);
            Assert.Empty(foreign!.Commands);
            Doc.EndUndoGroup();
            using var removedHeader = new TrackHeaderViewModel(next.tracks[1]);
            next.tracks.RemoveAt(1);
            removedHeader.MoveUp(); removedHeader.MoveDown(); removedHeader.RenameTo("Removed");
            Assert.Null(Doc.ActiveUndoGroup);
            Assert.Single(next.tracks);
        }

        [AvaloniaFact]
        public void DuplicateCapturesPartsBeforePublication_AndSurvivesViewReplacementAsOneUndo() {
            var project = Load();
            var track = project.tracks[0];
            using var header = new TrackHeaderViewModel(track);
            using var replace = new PublicationAction(() => header.Dispose());
            header.Duplicate();
            Assert.Equal(3, project.tracks.Count);
            Assert.Equal(2, project.parts.Count);
            var copy = project.tracks[1];
            Assert.NotSame(track.RendererSettings, copy.RendererSettings);
            Assert.Contains(project.parts, p => p.trackNo == 1);
            Assert.Null(Doc.ActiveUndoGroup);
            Doc.Undo();
            Assert.Equal(2, project.tracks.Count);
            Assert.Single(project.parts);
            Doc.Redo();
            Assert.Equal(3, project.tracks.Count);
            Assert.Equal(2, project.parts.Count);
            header.DuplicateSettings(); // A disposed view cannot start another edit.
            Assert.Equal(3, project.tracks.Count);
        }

        [AvaloniaFact]
        public void RenameAndColorAreUndoableAndDisposedAvatarDoesNotReallocate() {
            var project = Load();
            var track = project.tracks[0];
            using var header = new TrackHeaderViewModel(track);
            var previousName = track.TrackName;
            header.RenameTo("Lead");
            Assert.Equal("Lead", header.TrackName);
            Doc.Undo();
            Assert.Equal(previousName, track.TrackName);
            var color = new TrackColorViewModel(track);
            var previousColor = track.TrackColor;
            color.SelectedColor = color.TrackColors.First(c => c.Name != previousColor);
            color.Finish();
            Assert.Equal(color.SelectedColor.Name, track.TrackColor);
            Doc.Undo();
            Assert.Equal(previousColor, track.TrackColor);
            header.Dispose();
            header.RefreshAvatar();
            Assert.Null(header.Avatar);
        }

        [AvaloniaFact]
        public void RejectedStripInputRestoresBothReadoutAndNextInputOrigin() {
            var track = Load().tracks[0];
            var strip = new MixerTrackStrip(track);
            try {
                Doc.StartUndoGroup();
                var foreign = Doc.ActiveUndoGroup;
                strip.PanValue = 40;
                Assert.Equal(0, strip.PanValue);
                Assert.Equal(0, track.Pan);
                strip.FaderBox.RaiseEvent(new KeyEventArgs {
                    RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Key.Down,
                });
                Assert.Equal(track.Volume, strip.FaderBox.Value);
                Assert.Equal(track.Volume, strip.VolumeField.Value);
                Assert.Equal("+0.0 dB", strip.VolValueLabel.Text);
                Assert.Same(foreign, Doc.ActiveUndoGroup);
                Assert.Empty(foreign!.Commands);
                Doc.EndUndoGroup();
                strip.PanValue += 1;
                Assert.Equal(.01, track.Pan);
                Doc.Undo();
                Assert.Equal(0, strip.PanValue);
            } finally { strip.DisposeSubscriptions(); }
        }

        [AvaloniaFact]
        public void MixerShutdownDiscardsQueuedRebuildAndAddCannotStealHistory() {
            var project = Load();
            var mixer = new MixerControl();
            try {
                Doc.StartUndoGroup();
                var foreign = Doc.ActiveUndoGroup;
                mixer.ViewModel.AddTrack();
                Assert.Same(foreign, Doc.ActiveUndoGroup);
                Assert.Empty(foreign!.Commands);
                Assert.Equal(2, project.tracks.Count);
                Doc.EndUndoGroup();
                Task.Run(mixer.RebuildStrips).GetAwaiter().GetResult();
                Task.Run(mixer.ViewModel.RefreshTracks).GetAwaiter().GetResult();
                mixer.Shutdown();
                Dispatcher.UIThread.RunJobs();
                mixer.RebuildStrips(); mixer.ViewModel.AddTrack();
                Assert.Empty(mixer.TrackStripsPanel.Children);
                Assert.Null(mixer.FxChainHost.Content);
                Assert.False(mixer.LevelTimerRunning);
                Assert.Equal(2, project.tracks.Count);
            } finally { mixer.Shutdown(); }
        }

        [AvaloniaFact]
        public void WorkspaceDocumentCommandsWorkWithoutAWindowAndRejectInvalidSelections() {
            var project = Load();
            using var vm = new MainWindowViewModel();
            // Like the production shell, the hosted VM receives a load notification after creation.
            Doc.ExecuteCmd(new LoadProjectNotification(project));
            IWorkspaceEditingCommands commands = vm;
            vm.TracksViewModel.SelectedParts.Add(project.parts[0]);
            commands.MuteSelectedPart();
            Assert.True(project.tracks[0].Mute);
            commands.Undo();
            Assert.False(project.tracks[0].Mute);
            commands.SoloSelectedPart();
            Assert.True(project.tracks[0].Solo);
            commands.Undo();
            Assert.False(project.tracks[0].Solo);
            commands.MovePlayPosToEnd();
            Assert.Equal(1920, Doc.playPosTick);
            commands.MovePlayPosToStart();
            Assert.Equal(0, Doc.playPosTick);
            vm.TracksViewModel.SelectedParts.Clear();
            vm.TracksViewModel.SelectedParts.Add(new UVoicePart { trackNo = 999 });
            commands.SoloSelectedPart(); commands.MuteSelectedPart();
            Assert.False(project.tracks[0].Solo);
            Assert.False(project.tracks[0].Mute);
            Assert.Null(Doc.ActiveUndoGroup);
        }

        [AvaloniaFact]
        public void TrackSettingsRetainsItsDocumentAndCannotSealAnotherTransaction() {
            var project = Load();
            var track = project.tracks[0];
            var settings = new TrackSettingsViewModel(track);
            // No actual renderer is created; this only enables the settings commit path.
            track.RendererSettings.renderer = OpenUtau.Core.Render.Renderers.CLASSIC;
            var original = track.RendererSettings;
            Doc.StartUndoGroup();
            var foreign = Doc.ActiveUndoGroup;
            settings.Finish();
            Assert.Same(original, track.RendererSettings);
            Assert.Same(foreign, Doc.ActiveUndoGroup);
            Assert.Empty(foreign!.Commands);
            Doc.EndUndoGroup();
            var next = Load();
            settings.Finish();
            Assert.Same(original, track.RendererSettings);
            Assert.Null(Doc.ActiveUndoGroup);
            Assert.True(Doc.ChangesSaved);
            Assert.Equal(2, next.tracks.Count);
        }

        sealed class PublicationAction : ICmdSubscriber, IDisposable {
            readonly Action action;
            public PublicationAction(Action action) { this.action = action; DocManager.Inst.AddSubscriber(this); }
            public void OnNext(UCommand command, bool undo) { if (command is AddTrackCommand) action(); }
            public void Dispose() => DocManager.Inst.RemoveSubscriber(this);
        }

        public void Dispose() {
            if (Doc.ActiveUndoGroup != null) Doc.EndUndoGroup();
            Doc.ExecuteCmd(new LoadProjectNotification(previous));
            DataPath.SetValue(PathManager.Inst, previousDataPath);
            Preferences.Default = previousPreferences;
            dispatcher.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }
}

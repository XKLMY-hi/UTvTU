using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenUtau.App;
using OpenUtau.App.ViewModels;
using OpenUtau.App.Views;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtau.Test.TestSupport;
using ReactiveUI;
using Xunit;

namespace OpenUtau.Test.App {
    [CollectionDefinition("WorkspaceSession", DisableParallelization = true)]
    public class WorkspaceSessionCollection { }

    [Collection("WorkspaceSession")]
    public class WorkspaceSessionTests : IDisposable {
        readonly DocManagerTestSetup.ScopedDispatcher dispatcher;
        readonly string directory = Path.Combine(Path.GetTempPath(), "utvtu-session-" + Guid.NewGuid().ToString("N"));
        readonly string previousDataPath;
        readonly UProject previousProject;
        readonly Preferences.SerializablePreferences previousPreferences;
        readonly PropertyInfo dataPath = typeof(PathManager).GetProperty(nameof(PathManager.DataPath))!;
        DocManager Doc => DocManager.Inst;

        public WorkspaceSessionTests() {
            dispatcher = DocManagerTestSetup.EnterScopedDispatcher(nullChannel: true, installScheduler: false);
            previousProject = Doc.Project;
            previousDataPath = PathManager.Inst.DataPath;
            previousPreferences = Preferences.Default;
            Directory.CreateDirectory(directory);
            dataPath.SetValue(PathManager.Inst, directory);
            Preferences.Default = new Preferences.SerializablePreferences();
        }

        UProject Load() {
            var project = new UProject();
            project.parts.Add(new UVoicePart { name = "Lead", Duration = 1920 });
            project.parts.Add(new UVoicePart { name = "Harmony", Duration = 1920 });
            Doc.ExecuteCmd(new LoadProjectNotification(project));
            return project;
        }

        static void Select(params UPart[] parts) =>
            MessageBus.Current.SendMessage(new PartsSelectionEvent(parts, Array.Empty<UPart>()));

        [AvaloniaFact]
        public void VoiceEntry_RequiresAValidVoicePart() {
            var project = Load();
            using var nav = new PianoRollNavigationViewModel();
            Assert.False(nav.CanOpen);
            var wave = new UWavePart();
            project.parts.Add(wave);
            Select(wave);
            Assert.False(nav.CanOpen);
            Select(project.parts[0]);
            Assert.True(nav.CanOpen);
            Assert.Same(project.parts[0], nav.TargetPart);
        }

        [AvaloniaFact]
        public void VoiceEntry_PrefersSelectionAndFallsBackToCurrentPart() {
            var project = Load();
            using var nav = new PianoRollNavigationViewModel();
            Doc.ExecuteCmd(new LoadPartNotification(project.parts[0], project, 0));
            Select(project.parts[1]);
            Assert.Same(project.parts[1], nav.TargetPart);
            Select();
            Assert.Same(project.parts[0], nav.TargetPart);
        }

        [AvaloniaFact]
        public void VoiceEntry_DeleteAndUndoUpdateAvailability() {
            var project = Load();
            using var nav = new PianoRollNavigationViewModel();
            var part = project.parts[0];
            Select(part);
            Doc.StartUndoGroup();
            Doc.ExecuteCmd(new RemovePartCommand(project, part));
            Doc.EndUndoGroup();
            Assert.False(nav.CanOpen);
            Doc.Undo();
            Assert.Same(part, nav.TargetPart);
        }

        [AvaloniaFact]
        public void VoiceEntry_ProjectSwitchRejectsOldPartNotifications() {
            var previous = Load();
            using var nav = new PianoRollNavigationViewModel();
            Select(previous.parts[0]);
            var next = Load();
            Assert.False(nav.CanOpen);
            Doc.ExecuteCmd(new LoadPartNotification(previous.parts[0], previous, 0));
            Select(previous.parts[0]);
            Assert.False(nav.CanOpen);
            Select(next.parts[1]);
            Assert.Same(next.parts[1], nav.TargetPart);
        }

        [AvaloniaFact]
        public void VoiceEntry_DisposedSessionIgnoresDocumentAndSelection() {
            var project = Load();
            var nav = new PianoRollNavigationViewModel();
            Select(project.parts[0]);
            nav.Dispose();
            Select(project.parts[1]);
            Doc.ExecuteCmd(new LoadPartNotification(project.parts[0], project, 0));
            Assert.False(nav.CanOpen);
        }

        [AvaloniaFact]
        public void VoiceEditor_DisposeReleasesEntireModelGraphAndPreferenceObservers() {
            using var editor = new PianoRollViewModel();
            var notes = editor.NotesViewModel;
            var curves = editor.CurveViewModel;
            bool overwrite = Preferences.Default.EditTool.OverwritePitch;
            editor.Dispose();
            Assert.True(notes.IsDisposed);
            Assert.True(curves.IsDisposed);
            editor.PitchOverwrite = !overwrite;
            Assert.Equal(overwrite, Preferences.Default.EditTool.OverwritePitch);
            Assert.Empty(editor.LegacyPlugins);
        }

        [AvaloniaFact]
        public void OpeningPreferences_DoesNotWriteSettings() {
            Preferences.Default.MetronomeVolume = 47;
            using var vm = new PreferencesViewModel();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(47, vm.MetronomeVolume);
            Assert.Empty(Directory.GetFiles(directory));
        }

        [AvaloniaFact]
        public void ClosedPreferences_CannotWriteSettingsOrConsumeGlobalEvents() {
            var vm = new PreferencesViewModel();
            var paths = vm.VstScanPaths;
            int swatchChanges = 0;
            int editorChanges = 0;
            vm.AccentSwatches.CollectionChanged += (_, _) => swatchChanges++;
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.IsThemeEditorOpen)) editorChanges++; };
            int volume = Preferences.Default.MetronomeVolume;
            vm.Dispose();
            vm.MetronomeVolume = volume + 1;
            paths.Add("closed-session");
            vm.SetVLabelerPath("closed-session");
            MessageBus.Current.SendMessage(new ThemeChangedEvent());
            MessageBus.Current.SendMessage(new ThemeEditorStateChangedEvent());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(volume, Preferences.Default.MetronomeVolume);
            Assert.DoesNotContain("closed-session", Preferences.Default.VstScanPaths);
            Assert.NotEqual("closed-session", Preferences.Default.VLabelerPath);
            Assert.Equal(0, swatchChanges);
            Assert.Equal(0, editorChanges);
            Assert.Empty(Directory.GetFiles(directory));
        }

        [AvaloniaFact]
        public void VstPaths_ExternalRefreshPreservesStoreWithoutWritingIntermediateLists() {
            Preferences.Default.VstScanPaths = new() { "first" };
            using var vm = new PreferencesViewModel();
            Assert.Equal("first", Assert.Single(vm.VstScanPaths));
            Preferences.Default.VstScanPaths = new() { "external-a", "external-b" };
            vm.SyncVstScanPathsFromStore();
            Assert.Equal(new[] { "external-a", "external-b" }, vm.VstScanPaths);
            Assert.Equal(new[] { "external-a", "external-b" }, Preferences.Default.VstScanPaths);
            Assert.Empty(Directory.GetFiles(directory));
            vm.VstScanPaths.Add("user-edit");
            Assert.Equal("user-edit", Preferences.Default.VstScanPaths.Last());
            Assert.NotEmpty(Directory.GetFiles(directory));
        }

        [AvaloniaFact]
        public void PreferencesView_ReplacingOrReleasingSessionDisposesOldModel() {
            var previous = new PreferencesViewModel();
            var current = new PreferencesViewModel();
            var view = new PreferencesView { DataContext = previous };
            view.DataContext = current;
            Assert.True(previous.IsDisposed);
            Assert.False(current.IsDisposed);
            view.ReleaseSession();
            Assert.True(current.IsDisposed);
            Assert.Null(view.DataContext);
        }

        [AvaloniaFact]
        public void PreferencesView_QueuedRefreshDoesNotReachReplacementSession() {
            Preferences.Default.VstScanPaths = new() { "old" };
            var previous = new PreferencesViewModel();
            var view = new PreferencesView { DataContext = previous };
            var window = new Window { Content = view };
            window.Show();
            Exception error = null;
            var worker = new Thread(() => {
                try { MessageBus.Current.SendMessage(new VstLibraryChangedNotification()); }
                catch (Exception e) { error = e; }
            });
            worker.Start();
            worker.Join();
            Assert.Null(error);
            Preferences.Default.VstScanPaths = new() { "new" };
            using var current = new PreferencesViewModel();
            Assert.Equal("new", Assert.Single(current.VstScanPaths));
            view.DataContext = current;
            Preferences.Default.VstScanPaths = new() { "later-store" };
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("new", Assert.Single(current.VstScanPaths));
            view.ReleaseSession();
            window.Close();
        }

        [AvaloniaFact]
        public void PreferencesView_TemporaryReparentingKeepsSessionAlive() {
            using var vm = new PreferencesViewModel();
            var view = new PreferencesView { DataContext = vm };
            var window = new Window { Content = view };
            window.Show();
            window.Content = null;
            Assert.False(vm.IsDisposed);
            window.Content = view;
            Assert.False(vm.IsDisposed);
            view.ReleaseSession();
            window.Close();
        }

        public void Dispose() {
            Doc.ExecuteCmd(new LoadProjectNotification(previousProject));
            Preferences.Default = previousPreferences;
            dataPath.SetValue(PathManager.Inst, previousDataPath);
            dispatcher.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }
}

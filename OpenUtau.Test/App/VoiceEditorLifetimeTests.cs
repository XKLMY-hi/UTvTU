using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtau.Test.TestSupport;
using OpenUtau.ViewModels;
using ReactiveUI;
using Xunit;

namespace OpenUtau.Test.App {
    [Collection("WorkspaceSession")]
    public class VoiceEditorLifetimeTests : IDisposable {
        readonly DocManagerTestSetup.ScopedDispatcher dispatcher;
        readonly UProject previousProject;
        readonly Preferences.SerializablePreferences previousPreferences;
        readonly string previousDataPath;
        readonly string directory = Path.Combine(Path.GetTempPath(), "utvtu-voice-lifetime-" + Guid.NewGuid().ToString("N"));
        readonly PropertyInfo dataPath = typeof(PathManager).GetProperty(nameof(PathManager.DataPath))!;
        static DocManager Doc => DocManager.Inst;

        public VoiceEditorLifetimeTests() {
            dispatcher = DocManagerTestSetup.EnterScopedDispatcher(nullChannel: true, installScheduler: false);
            previousProject = Doc.Project;
            previousPreferences = Preferences.Default;
            previousDataPath = PathManager.Inst.DataPath;
            Directory.CreateDirectory(directory);
            dataPath.SetValue(PathManager.Inst, directory);
            Preferences.Default = new Preferences.SerializablePreferences { ShowTips = false };
            Doc.ExecuteCmd(new LoadProjectNotification(OpenUtau.Core.Format.Ustx.Create()));
        }

        [AvaloniaFact]
        public void DisposedNotes_IgnoreGlobalRefreshSelectionAndPreferenceChanges() {
            var vm = new NotesViewModel();
            vm.Unsubscribe();
            vm.Dispose();
            bool highlight = vm.ShowPlaybackNoteHighlight;
            bool showPitch = Preferences.Default.ShowPitch;
            Preferences.Default.ShowPlaybackNoteHighlight = !highlight;
            MessageBus.Current.SendMessage(new PianorollRefreshEvent("PlaybackNoteHighlight"));
            Assert.Equal(highlight, vm.ShowPlaybackNoteHighlight);
            vm.ShowPitch = !showPitch;
            Assert.Equal(showPitch, Preferences.Default.ShowPitch);
            var note = UNote.Create();
            vm.Selection.Select(new[] { note });
            MessageBus.Current.SendMessage(new CurveSelectionEvent(new CurveSelection()));
            Assert.Contains(note, vm.Selection);
            Assert.True(vm.IsDisposed);
        }

        [AvaloniaFact]
        public void DisposedNotes_RejectAlreadyQueuedDocumentCallback() {
            using var vm = new NotesViewModel();
            string key = vm.PrimaryKey;
            Exception? error = null;
            var worker = new Thread(() => {
                try { vm.OnNext(new SelectExpressionNotification(OpenUtau.Core.Format.Ustx.PITD, 0, true), false); }
                catch (Exception e) { error = e; }
            });
            worker.Start();
            worker.Join();
            Assert.Null(error);
            vm.Dispose();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(key, vm.PrimaryKey);
            vm.OnNext(new SelectExpressionNotification(OpenUtau.Core.Format.Ustx.DYN, 0, true), false);
            Assert.Equal(key, vm.PrimaryKey);
        }

        sealed class TrackedBitmap : Bitmap {
            public int DisposeCount { get; private set; }
            public TrackedBitmap() : base(new MemoryStream(Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNgYGBgAAAABQABpfZFQAAAAABJRU5ErkJggg=="))) { }
            public override void Dispose() {
                DisposeCount++;
                base.Dispose();
            }
        }

        [AvaloniaFact]
        public void NotesDispose_ReleasesOwnedBitmapsOnce_AndClearsDocumentReferences() {
            var vm = new NotesViewModel();
            var image = new TrackedBitmap();
            vm.Avatar = image;
            vm.Portrait = image; // Aliased bitmap ownership must not cause two Dispose calls.
            vm.Part = new UVoicePart();
            vm.Dispose();
            vm.Unsubscribe();
            Assert.Equal(1, image.DisposeCount);
            Assert.Null(vm.Avatar);
            Assert.Null(vm.Portrait);
            Assert.Null(vm.PortraitMask);
            Assert.Null(vm.Part);
            Assert.Empty(vm.SnapDivs);
            Assert.Empty(vm.Keys);
        }

        [AvaloniaFact]
        public void DisposedCurve_RejectsEditingAndDocumentNotifications() {
            var vm = new CurveViewModel();
            var descriptor = new UExpressionDescriptor("Pitch", "pitd", -1200, 1200, 0);
            vm.Select(descriptor, 0, 480, null);
            Assert.True(vm.IsSelected("pitd"));
            vm.Dispose();
            vm.Dispose();
            vm.Select(descriptor, 0, 480, null);
            vm.OnNext(new SelectExpressionNotification("pitd", 0, true), false);
            MessageBus.Current.SendMessage(new NotesSelectionEvent(new NoteSelectionViewModel()));
            Assert.False(vm.TryGetSelection("pitd", out _));
            Assert.False(vm.IsSelected("pitd"));
            Assert.True(vm.IsDisposed);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static (WeakReference notes, WeakReference curves) CreateDisposedModels() {
            var notes = new NotesViewModel();
            var curves = new CurveViewModel();
            notes.Dispose();
            curves.Dispose();
            return (new WeakReference(notes), new WeakReference(curves));
        }

        [AvaloniaFact]
        public void GlobalBusesAndDocManager_DoNotKeepDisposedModelsAlive() {
            var (notes, curves) = CreateDisposedModels();
            Dispatcher.UIThread.RunJobs();
            for (int i = 0; i < 3 && (notes.IsAlive || curves.IsAlive); i++) {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            Assert.False(notes.IsAlive, "Disposed NotesViewModel must not be retained by MessageBus, OAPH or DocManager");
            Assert.False(curves.IsAlive, "Disposed CurveViewModel must not be retained by NotesSelectionEvent or DocManager");
        }

        [AvaloniaFact]
        public void ConstructorFailure_RemovesPartiallyRegisteredDocumentSubscriber() {
            var subscribers = (ICollection<ICmdSubscriber>)typeof(DocManager)
                .GetField("subscribers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Doc)!;
            var before = subscribers.ToArray();
            using var fail = MessageBus.Current.Listen<PianoRollOpenPartChangedEvent>()
                .Subscribe(_ => throw new InvalidOperationException("Injected constructor broadcast failure"));
            Assert.Throws<InvalidOperationException>(() => new NotesViewModel());
            Assert.Equal(before, subscribers.ToArray());
        }

        public void Dispose() {
            Dispatcher.UIThread.RunJobs();
            Doc.ExecuteCmd(new LoadProjectNotification(previousProject));
            Preferences.Default = previousPreferences;
            dataPath.SetValue(PathManager.Inst, previousDataPath);
            dispatcher.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }
}

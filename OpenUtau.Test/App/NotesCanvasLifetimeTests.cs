using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using Xunit;

namespace OpenUtau.Test.App {
    // No PianoRoll, NotesViewModel, project loading, audio, or text layout.
    [Collection("WorkspaceSession")]
    public class NotesCanvasLifetimeTests {
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        static T State<T>(NotesCanvas canvas, string name) =>
            (T)typeof(NotesCanvas).GetField(name, PrivateInstance)!.GetValue(canvas)!;
        static void SetState(NotesCanvas canvas, string name, object value) =>
            typeof(NotesCanvas).GetField(name, PrivateInstance)!.SetValue(canvas, value);
        static void Call(NotesCanvas canvas, string name, params object[] arguments) =>
            typeof(NotesCanvas).GetMethod(name, PrivateInstance)!.Invoke(canvas, arguments);
        static HashSet<UNote> Selection(NotesCanvas canvas) => State<HashSet<UNote>>(canvas, "selectedNotes");
        static List<UPart> Ghosts(NotesCanvas canvas) => State<List<UPart>>(canvas, "otherPartsInView");
        static void SendSelection(UNote note) {
            var selection = new NoteSelectionViewModel();
            selection.Add(note);
            MessageBus.Current.SendMessage(new NotesSelectionEvent(selection));
        }
        static Window Mount(Control control) {
            var window = new Window { Width = 120, Height = 80, Content = control };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return window;
        }

        [AvaloniaFact]
        public void GlobalSelectionAndPartRefresh_AreActiveOnlyWhileMounted_AndResumeOnRemount() {
            var canvas = new NotesCanvas();
            var first = UNote.Create();
            var second = UNote.Create();
            SendSelection(first);
            Assert.Empty(Selection(canvas));
            var host = new Panel { Children = { canvas } };
            var window = Mount(host);
            try {
                SendSelection(first);
                Assert.Contains(first, Selection(canvas));
                Ghosts(canvas).Add(new UVoicePart());
                MessageBus.Current.SendMessage(new PartRefreshEvent(new UVoicePart()));
                Assert.Empty(Ghosts(canvas));
                host.Children.Remove(canvas);
                SendSelection(second);
                Assert.Contains(first, Selection(canvas));
                Assert.DoesNotContain(second, Selection(canvas));
                Ghosts(canvas).Add(new UVoicePart());
                MessageBus.Current.SendMessage(new PartRefreshEvent(new UVoicePart()));
                Assert.Single(Ghosts(canvas));
                host.Children.Add(canvas);
                Assert.Empty(Ghosts(canvas));
                SendSelection(second);
                Assert.Single(Selection(canvas));
                Assert.Contains(second, Selection(canvas));
            } finally { window.Close(); }
        }

        [AvaloniaFact]
        public void BackgroundMessageQueuedForOldAttachment_CannotOverwriteRemountedState() {
            var canvas = new NotesCanvas();
            var host = new Panel { Children = { canvas } };
            var window = Mount(host);
            try {
                var oldNote = UNote.Create();
                Exception? failure = null;
                var worker = new Thread(() => {
                    try {
                        SendSelection(oldNote);
                        MessageBus.Current.SendMessage(new PartRefreshEvent(new UVoicePart()));
                        MessageBus.Current.SendMessage(new NotesRefreshEvent());
                    } catch (Exception e) { failure = e; }
                });
                worker.Start();
                worker.Join();
                Assert.Null(failure);
                Assert.Empty(Selection(canvas)); // The worker must not change UI state directly.
                host.Children.Remove(canvas);
                host.Children.Add(canvas);
                var newNote = UNote.Create();
                SendSelection(newNote);
                var marker = new UVoicePart();
                Ghosts(canvas).Add(marker);
                Dispatcher.UIThread.RunJobs();
                Assert.Single(Selection(canvas));
                Assert.Contains(newNote, Selection(canvas));
                Assert.DoesNotContain(oldNote, Selection(canvas));
                Assert.Contains(marker, Ghosts(canvas));
            } finally { window.Close(); }
        }

        [AvaloniaFact]
        public void DeferredRenderInvalidation_CannotClearNewAttachmentPendingState() {
            var canvas = new NotesCanvas();
            var host = new Panel { Children = { canvas } };
            var window = Mount(host);
            try {
                // Simulate the old render pass fading a note; no actual note rendering is needed.
                SetState(canvas, "activePlaybackNote", UNote.Create());
                SetState(canvas, "activeHighlight", 1f);
                SetState(canvas, "renderPassActive", true);
                Call(canvas, "UpdatePlaybackHighlight", false);
                Assert.True(State<bool>(canvas, "invalidatePending"));
                SetState(canvas, "renderPassActive", false);
                host.Children.Remove(canvas);
                host.Children.Add(canvas);
                SetState(canvas, "invalidatePending", true);
                Dispatcher.UIThread.RunJobs();
                Assert.True(State<bool>(canvas, "invalidatePending"));
            } finally { window.Close(); }
        }

        [AvaloniaFact]
        public void DetachStopsBothTimersAndResetsAnimation_PartChangesCannotRestartThemUntilRemount() {
            var canvas = new NotesCanvas();
            var host = new Panel { Children = { canvas } };
            var window = Mount(host);
            try {
                var highlight = State<DispatcherTimer>(canvas, "highlightTimer");
                var hover = State<DispatcherTimer>(canvas, "hoverTimer");
                highlight.Start();
                hover.Start();
                SetState(canvas, "hoverNote", UNote.Create());
                SetState(canvas, "fadingPlaybackNote", UNote.Create());
                SetState(canvas, "activeHighlight", 1f);
                host.Children.Remove(canvas);
                Assert.False(highlight.IsEnabled);
                Assert.False(hover.IsEnabled);
                Assert.Null(State<UNote?>(canvas, "hoverNote"));
                Assert.Null(State<UNote?>(canvas, "fadingPlaybackNote"));
                Assert.Equal(0f, State<float>(canvas, "activeHighlight"));
                Assert.True(State<bool>(canvas, "playbackSeekPending"));
                using var flag = canvas.Bind(NotesCanvas.ShowPlaybackNoteHighlightProperty, Observable.Return(true));
                using var position = canvas.Bind(NotesCanvas.PlayPosTickProperty, Observable.Return(480));
                canvas.Part = new UVoicePart();
                Call(canvas, "UpdatePlaybackHighlight", true);
                Assert.False(highlight.IsEnabled);
                Assert.False(hover.IsEnabled);
                // A model-free host should not initialize the playback singleton on remount.
                using var hideHighlight = canvas.Bind(NotesCanvas.ShowPlaybackNoteHighlightProperty, Observable.Return(false));
                host.Children.Add(canvas);
                SendSelection(UNote.Create());
                Assert.Single(Selection(canvas));
                Assert.Null(State<UNote?>(canvas, "hoverNote"));
            } finally { window.Close(); }
        }

        [AvaloniaFact]
        public void UnmountedCanvas_IsNotRetainedByAnyGlobalMessageOrAnimationTimer() {
            var weak = MountAndRelease();
            Dispatcher.UIThread.RunJobs();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.False(weak.IsAlive);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static WeakReference MountAndRelease() {
            var canvas = new NotesCanvas();
            var window = Mount(canvas);
            State<DispatcherTimer>(canvas, "highlightTimer").Start();
            State<DispatcherTimer>(canvas, "hoverTimer").Start();
            var weak = new WeakReference(canvas);
            window.Content = null;
            window.Close();
            return weak;
        }
    }
}

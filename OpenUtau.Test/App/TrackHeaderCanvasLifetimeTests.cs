using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Test.TestSupport;
using ReactiveUI;
using Xunit;

namespace OpenUtau.Test.App {
    // Small host only: no MainWindow / workspace page, no layout or rendering loops.
    [Collection("WorkspaceSession")]
    public class TrackHeaderCanvasLifetimeTests : IDisposable {
        readonly DocManagerTestSetup.ScopedDispatcher dispatcher =
            DocManagerTestSetup.EnterScopedDispatcher(nullChannel: true, installScheduler: false);

        static TrackHeaderCanvas CreateCanvas(ObservableCollection<UTrack> items) {
            var canvas = new TrackHeaderCanvas { Items = items };
            canvas.Bind(TrackHeaderCanvas.TrackHeightProperty, Observable.Return(105.0));
            return canvas;
        }

        static TrackHeader[] Headers(TrackHeaderCanvas canvas) => canvas.Children.OfType<TrackHeader>().ToArray();

        static Window Mount(Control control) {
            var window = new Window { Width = 180, Height = 240, Content = control };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return window;
        }

        [AvaloniaFact]
        public void PrepopulatedAndDefaultItems_AreSynchronizedOnMount() {
            var first = new UTrack { TrackName = "Lead" };
            var second = new UTrack { TrackNo = 1, TrackName = "Harmony" };
            var canvas = new TrackHeaderCanvas();
            canvas.Items.Add(first); // The default collection must work without replacing Items.
            using var height = canvas.Bind(TrackHeaderCanvas.TrackHeightProperty, Observable.Return(105.0));
            Assert.Empty(Headers(canvas));
            var window = Mount(canvas);
            try {
                Assert.Equal("Lead", Assert.Single(Headers(canvas)).ViewModel!.TrackName);
                canvas.Items = new ObservableCollection<UTrack> { first, second };
                Assert.Equal(2, Headers(canvas).Length);
                Assert.Equal(2, canvas.Children.OfType<TrackAdder>().Single().TrackNo);
            } finally { window.Close(); }
            Assert.Empty(Headers(canvas));
        }

        [AvaloniaFact]
        public void ReplacedCollection_StopsUpdating_AndResetReconcilesCurrentContents() {
            var first = new UTrack { TrackName = "Lead" };
            var second = new UTrack { TrackName = "Harmony" };
            var original = new ObservableCollection<UTrack> { first };
            var replacement = new ResettableTracks();
            var canvas = CreateCanvas(original);
            var window = Mount(canvas);
            try {
                var oldHeader = Assert.Single(Headers(canvas));
                canvas.Items = replacement;
                Assert.Empty(Headers(canvas));
                original.Add(second);
                Assert.Empty(Headers(canvas));
                replacement.ResetTo(first, second);
                Assert.Equal(2, Headers(canvas).Length);
                replacement.ResetTo(second);
                var survivor = Assert.Single(Headers(canvas));
                Assert.Equal("Harmony", survivor.ViewModel!.TrackName);
                replacement.ResetTo(first, second);
                replacement.Clear(); // Multi-track Reset used to mutate the enumerated dictionary.
                Assert.Empty(Headers(canvas));
                int staleRefreshes = 0;
                oldHeader.ViewModel!.PropertyChanged += (_, _) => staleRefreshes++;
                MessageBus.Current.SendMessage(new TracksRefreshEvent());
                Assert.Equal(0, staleRefreshes);
            } finally { window.Close(); }
        }

        [AvaloniaFact]
        public void DetachRemount_RestoresCurrentItemsAndSelection_WithoutDuplicateRefreshes() {
            var first = new UTrack { TrackName = "Lead" };
            var second = new UTrack { TrackNo = 1, TrackName = "Harmony" };
            var items = new ObservableCollection<UTrack> { first };
            using var tracks = new TracksViewModel();
            tracks.SelectedTracks.Add(first);
            var canvas = CreateCanvas(items);
            canvas.DataContext = tracks;
            var host = new Panel { Children = { canvas } };
            var window = Mount(host);
            try {
                var oldHeader = Assert.Single(Headers(canvas));
                Assert.True(oldHeader.ViewModel!.IsSelected);
                host.Children.Remove(canvas);
                Assert.Empty(Headers(canvas));
                items.Clear();
                items.Add(second);
                Assert.Empty(Headers(canvas));
                tracks.SelectedTracks.Clear();
                tracks.SelectedTracks.Add(second);
                MessageBus.Current.SendMessage(new TrackSelectionEvent(new[] { second }));
                Assert.True(oldHeader.ViewModel.IsSelected); // Disposed header no longer receives selection.
                host.Children.Add(canvas);
                var currentHeader = Assert.Single(Headers(canvas));
                Assert.NotSame(oldHeader, currentHeader);
                Assert.Equal("Harmony", currentHeader.ViewModel!.TrackName);
                Assert.True(currentHeader.ViewModel.IsSelected);
                host.Children.Remove(canvas);
                host.Children.Add(canvas); // Another remount must still subscribe exactly once.
                var vm = Assert.Single(Headers(canvas)).ViewModel!;
                int refreshes = 0;
                vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Renderer)) refreshes++; };
                MessageBus.Current.SendMessage(new MixFxChangedNotification(second.TrackNo));
                Assert.Equal(1, refreshes);
                MessageBus.Current.SendMessage(new TrackSelectionEvent(Array.Empty<UTrack>()));
                Assert.False(vm.IsSelected);
            } finally { window.Close(); }
        }

        [AvaloniaFact]
        public void UnmountedCanvas_IsNotRetainedByGlobalBusOrFormerItems() {
            var items = new ObservableCollection<UTrack> { new UTrack() };
            var weak = MountAndRelease(items);
            Dispatcher.UIThread.RunJobs();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.False(weak.IsAlive);
            // Keep the source alive across collection; its event must not retain the canvas.
            items.Add(new UTrack());
            GC.KeepAlive(items);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static WeakReference MountAndRelease(ObservableCollection<UTrack> items) {
            var canvas = CreateCanvas(items);
            var window = Mount(canvas);
            var weak = new WeakReference(canvas);
            window.Content = null;
            window.Close();
            return weak;
        }

        sealed class ResettableTracks : ObservableCollection<UTrack> {
            public void ResetTo(params UTrack[] tracks) {
                Items.Clear();
                foreach (var track in tracks) Items.Add(track);
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }
        }

        public void Dispose() => dispatcher.Dispose();
    }
}

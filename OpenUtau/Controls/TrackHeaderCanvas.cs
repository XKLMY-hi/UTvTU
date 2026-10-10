using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core.Ustx;
using ReactiveUI;

namespace OpenUtau.App.Controls {
    class TrackHeaderCanvas : Canvas {
        public static readonly DirectProperty<TrackHeaderCanvas, double> TrackHeightProperty =
            AvaloniaProperty.RegisterDirect<TrackHeaderCanvas, double>(
                nameof(TrackHeight),
                o => o.TrackHeight,
                (o, v) => o.TrackHeight = v);
        public static readonly DirectProperty<TrackHeaderCanvas, double> TrackOffsetProperty =
            AvaloniaProperty.RegisterDirect<TrackHeaderCanvas, double>(
                nameof(TrackOffset),
                o => o.TrackOffset,
                (o, v) => o.TrackOffset = v);
        public static readonly DirectProperty<TrackHeaderCanvas, ObservableCollection<UTrack>> ItemsProperty =
            AvaloniaProperty.RegisterDirect<TrackHeaderCanvas, ObservableCollection<UTrack>>(
                nameof(Items),
                o => o.Items,
                (o, v) => o.Items = v);

        public double TrackHeight {
            get => trackHeight;
            private set => SetAndRaise(TrackHeightProperty, ref trackHeight, value);
        }
        public double TrackOffset {
            get => trackOffset;
            private set => SetAndRaise(TrackOffsetProperty, ref trackOffset, value);
        }
        public ObservableCollection<UTrack> Items {
            get => _items;
            set => SetAndRaise(ItemsProperty, ref _items, value ?? new ObservableCollection<UTrack>());
        }

        private double trackHeight;
        private double trackOffset;
        private ObservableCollection<UTrack> _items = new ObservableCollection<UTrack>();

        private readonly Dictionary<UTrack, TrackHeader> trackHeaders = new Dictionary<UTrack, TrackHeader>();
        private TrackAdder? trackAdder;
        private CompositeDisposable? subscriptions;
        private ObservableCollection<UTrack>? observedItems;
        private readonly HashSet<UTrack> selectedTracks = new();

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs attachment) {
            base.OnAttachedToVisualTree(attachment);
            if (subscriptions != null) return;
            var lifetime = new CompositeDisposable();
            subscriptions = lifetime;
            try {
                MessageBus.Current.Listen<TracksRefreshEvent>()
                .Subscribe(_ => {
                    if (!ReferenceEquals(subscriptions, lifetime)) return;
                    foreach (var header in trackHeaders.Values.ToArray()) {
                        if (header.ViewModel == null) {
                            continue;
                        }
                        header.ViewModel.JudgeMuted();
                        header.ViewModel.ManuallyRaise();
                    }
                    if (trackAdder != null) {
                        trackAdder.TrackNo = trackHeaders.Count;
                    }
                }).DisposeWith(lifetime);
                MessageBus.Current.Listen<MixFxChangedNotification>()
                .Subscribe(e => {
                    if (!ReferenceEquals(subscriptions, lifetime)) return;
                    foreach (var (track, header) in trackHeaders.ToArray()) {
                        if (header.ViewModel != null && track.TrackNo == e.trackNo) {
                            header.ViewModel.ManuallyRaise();
                        }
                    }
                }).DisposeWith(lifetime);
                MessageBus.Current.Listen<TrackSelectionEvent>()
                .Subscribe(e => {
                    if (!ReferenceEquals(subscriptions, lifetime)) return;
                    selectedTracks.Clear();
                    selectedTracks.UnionWith(e.selectedTracks);
                    ApplySelection(readViewModel: false);
                }).DisposeWith(lifetime);
                MessageBus.Current.Listen<ThemeChangedEvent>()
                .Subscribe(_ => {
                    if (!ReferenceEquals(subscriptions, lifetime)) return;
                    foreach (var header in trackHeaders.Values.ToArray()) {
                        header.ViewModel?.RefreshSelectionStyle();
                    }
                }).DisposeWith(lifetime);
                ObserveItems();
            } catch {
                StopListening();
                ClearHeaders();
                throw;
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            StopListening();
            // Headers own document/bus subscribers too; retaining them after detach would
            // retain this canvas through their property bindings. Rebuild from Items on remount.
            ClearHeaders();
            base.OnDetachedFromVisualTree(e);
        }

        private void StopListening() {
            var lifetime = subscriptions;
            subscriptions = null; // Reject callbacks already queued for this attachment.
            lifetime?.Dispose();
            if (observedItems != null) observedItems.CollectionChanged -= Items_CollectionChanged;
            observedItems = null;
        }

        protected override void OnInitialized() {
            base.OnInitialized();
            trackAdder = new TrackAdder();
            trackAdder.DataContext = DataContext;
            trackAdder.Bind(this);
            trackAdder.TrackNo = trackHeaders.Count;
            Children.Add(trackAdder);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == ItemsProperty) {
                if (subscriptions != null) ObserveItems();
            } else if (change.Property == DataContextProperty) {
                if (trackAdder != null) {
                    trackAdder.DataContext = DataContext;
                }
                ApplySelection();
            }
        }

        private void ObserveItems() {
            if (observedItems != null) observedItems.CollectionChanged -= Items_CollectionChanged;
            observedItems = Items;
            observedItems.CollectionChanged += Items_CollectionChanged;
            SynchronizeItems();
        }

        private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
            if (subscriptions != null && ReferenceEquals(sender, observedItems)) SynchronizeItems();
        }

        private void SynchronizeItems() {
            var current = new HashSet<UTrack>(Items);
            // Reset can replace the contents as well as clear them. Never mutate an
            // enumerated dictionary, and keep surviving headers (including move operations).
            foreach (var track in trackHeaders.Keys.ToArray()) {
                if (!current.Contains(track)) Remove(track);
            }
            foreach (var track in Items.ToArray()) {
                if (!trackHeaders.ContainsKey(track)) Add(track);
            }
            selectedTracks.IntersectWith(current);
            ApplySelection();
            if (trackAdder != null) trackAdder.TrackNo = trackHeaders.Count;
        }

        private void ApplySelection(bool readViewModel = true) {
            if (readViewModel && DataContext is TracksViewModel vm) {
                selectedTracks.Clear();
                selectedTracks.UnionWith(vm.SelectedTracks);
            }
            foreach (var (track, header) in trackHeaders.ToArray()) {
                if (header.ViewModel != null) header.ViewModel.IsSelected = selectedTracks.Contains(track);
            }
        }

        private void ClearHeaders() {
            foreach (var track in trackHeaders.Keys.ToArray()) Remove(track);
        }

        void Add(UTrack track) {
            var vm = new TrackHeaderViewModel(track);
            TrackHeader? header = null;
            try {
                header = new TrackHeader() { DataContext = vm, ViewModel = vm };
                header.Bind(track, this);
                trackHeaders.Add(track, header);
                Children.Add(header);
            } catch {
                trackHeaders.Remove(track);
                if (header != null) {
                    header.Dispose();
                    Children.Remove(header);
                } else vm.Dispose();
                throw;
            }
            if (trackAdder != null) {
                trackAdder.TrackNo = trackHeaders.Count;
            }
        }

        void Remove(UTrack track) {
            if (!trackHeaders.Remove(track, out var header)) return;
            try { header.Dispose(); }
            finally { Children.Remove(header); }
            if (trackAdder != null) {
                trackAdder.TrackNo = trackHeaders.Count;
            }
        }
    }
}

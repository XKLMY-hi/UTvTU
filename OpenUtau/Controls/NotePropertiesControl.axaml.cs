using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using OpenUtau.App.ViewModels;
using OpenUtau.App.Views;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using Serilog;
using SharpCompress;

namespace OpenUtau.App.Controls {
    public partial class NotePropertiesControl : UserControl, ICmdSubscriber {
        private readonly NotePropertiesViewModel ViewModel;
        readonly OpenUtau.App.UiThreadAffinity affinity = new();
        CompositeDisposable? subscriptions;
        NotePropertiesViewModel.NotePropertyEditSession? textEdit;
        NotePropertiesViewModel.NotePropertyEditSession? sliderEdit;
        TextBox? editingBox;
        Slider? editingSlider;
        readonly Dictionary<Slider, List<Thumb>> sliderThumbs = new();

        public NotePropertiesControl() {
            InitializeComponent();
            DataContext = ViewModel = new NotePropertiesViewModel();

            foreach (var box in this.GetLogicalDescendants().OfType<TextBox>()) {
                  box.AddHandler(GotFocusEvent, OnTextBoxGotFocus);
                  box.AddHandler(LostFocusEvent, OnTextBoxLostFocus);
              }
              foreach (var slider in this.GetLogicalDescendants().OfType<Slider>()) {
                  slider.AddHandler(PointerPressedEvent, SliderPointerPressed, RoutingStrategies.Tunnel);
                  slider.AddHandler(PointerReleasedEvent, SliderPointerReleased, RoutingStrategies.Tunnel);
                  slider.AddHandler(PointerCaptureLostEvent, SliderCaptureLost, RoutingStrategies.Direct, handledEventsToo: true);
                  slider.TemplateApplied += (_, _) => {
                      if (ReferenceEquals(slider, editingSlider)) EndSliderEdit();
                      ObserveSliderThumbs(slider);
                  };
                  slider.AddHandler(KeyDownEvent, SliderKeyDown, RoutingStrategies.Tunnel);
                  slider.AddHandler(PointerWheelChangedEvent, SliderWheel, RoutingStrategies.Tunnel);
                  slider.PropertyChanged += SliderValueChanged;
              }
          
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs args) {
            base.OnAttachedToVisualTree(args);
            if (subscriptions != null || ViewModel.IsDisposed) return;
            var lifetime = new CompositeDisposable();
            subscriptions = lifetime;
            MessageBus.Current.Listen<PianorollRefreshEvent>().Subscribe(e => affinity.Post(() => {
                if (ReferenceEquals(subscriptions, lifetime) && !ViewModel.IsDisposed && e.refreshItem == "Part") LoadPart(ViewModel.Part);
            })).DisposeWith(lifetime);
            DocManager.Inst.AddSubscriber(this);
            LoadPart(ViewModel.Part);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs args) {
            var lifetime = subscriptions;
            subscriptions = null;
            lifetime?.Dispose();
            DocManager.Inst.RemoveSubscriber(this);
            CancelInput();
            foreach (var slider in sliderThumbs.Keys.ToArray()) ReleaseSliderThumbs(slider);
            base.OnDetachedFromVisualTree(args);
        }

        private void LoadPart(UPart? part) {
            if (ViewModel.IsDisposed) return;
            CancelInput();
            bool previousLoading = NotePropertiesViewModel.NoteLoading;
            NotePropertiesViewModel.NoteLoading = true;
            try {
            ViewModel.LoadPart(part);
            ExpressionsPanel.Children.Clear();
            foreach (NotePropertyExpViewModel expVM in ViewModel.Expressions) {
                var control = new NotePropertyExpression() { DataContext = expVM };
                ExpressionsPanel.Children.Add(control);
            }

            } finally { NotePropertiesViewModel.NoteLoading = previousLoading; }
        }

        private string textBoxValue = string.Empty;
        void OnTextBoxGotFocus(object? sender, FocusChangedEventArgs args) {
            if (sender is not TextBox box || box.Tag is not string) return;
            textEdit?.Dispose();
            editingBox = box;
            textBoxValue = box.Text ?? string.Empty;
            textEdit = ViewModel.BeginInputEdit();
        }
        void OnTextBoxLostFocus(object? sender, RoutedEventArgs args) {
            if (ReferenceEquals(sender, editingBox)) CommitTextEdit();
        }
        void CommitTextEdit() {
            var edit = textEdit;
            var box = editingBox;
            textEdit = null;
            editingBox = null;
            try {
                if (edit != null && box?.Tag is string tag && box.Text != textBoxValue)
                    edit.ApplyParameter(tag, box.Text);
            } finally {
                edit?.Dispose();
                ViewModel.RefreshInputs();
                RestoreText(box);
            }
        }
        void CancelTextEdit() {
            var box = editingBox;
            textEdit?.Dispose();
            textEdit = null;
            editingBox = null;
            ViewModel.RefreshInputs();
            RestoreText(box);
        }
        void RestoreText(TextBox? box) {
            if (box == null || ViewModel.IsDisposed) return;
            // OneWay bindings do not push an unchanged source value back after a draft
            // was rejected. Restore the current target's value explicitly, also after
            // selection changes, rather than restoring the old selection's draft.
            object? value = box.Tag switch {
                "Lyric" => ViewModel.Lyric,
                "Tone" => ViewModel.Tone,
                "Tuning" => ViewModel.Tuning,
                "PortamentoLength_TextBox" => ViewModel.PortamentoLength,
                "PortamentoStart_TextBox" => ViewModel.PortamentoStart,
                "VibratoLength" => ViewModel.VibratoLength,
                "VibratoPeriod" => ViewModel.VibratoPeriod,
                "VibratoDepth" => ViewModel.VibratoDepth,
                "VibratoIn" => ViewModel.VibratoIn,
                "VibratoOut" => ViewModel.VibratoOut,
                "VibratoShift" => ViewModel.VibratoShift,
                "VibratoDrift" => ViewModel.VibratoDrift,
                "VibratoVolLink" => ViewModel.VibratoVolLink,
                _ => null,
            };
            if (value != null) box.SetCurrentValue(TextBox.TextProperty, value.ToString());
        }
        void SliderPointerPressed(object? sender, PointerPressedEventArgs args) {
            if (sender is not Slider slider || slider.Tag is not string || !slider.IsEffectivelyEnabled) return;
            var point = args.GetCurrentPoint(slider);
            if (point.Properties.IsLeftButtonPressed) {
                ObserveSliderThumbs(slider);
                EndSliderEdit();
                editingSlider = slider;
                sliderEdit = ViewModel.BeginInputEdit();
            } else if (point.Properties.IsRightButtonPressed && slider.Tag is string tag) {
                using var edit = ViewModel.BeginInputEdit();
                edit.ApplyParameter(tag, null);
                ViewModel.RefreshInputs();
                args.Handled = true;
            }
        }
        void SliderValueChanged(object? sender, AvaloniaPropertyChangedEventArgs args) {
            if (args.Property == IsEffectivelyEnabledProperty && sender is Slider disabled &&
                ReferenceEquals(disabled, editingSlider) && !disabled.IsEffectivelyEnabled) {
                EndSliderEdit();
                return;
            }
            if (ViewModel.IsRefreshingInputs) return;
            if (args.Property == RangeBase.ValueProperty && sender is Slider slider &&
                ReferenceEquals(slider, editingSlider) && slider.Tag is string tag)
                sliderEdit?.ApplyParameter(tag, (float)slider.Value);
        }
        void SliderPointerReleased(object? sender, PointerReleasedEventArgs args) => EndSliderEdit();
        void SliderCaptureLost(object? sender, PointerCaptureLostEventArgs args) {
            if (editingSlider == null) return;
            if (ReferenceEquals(sender, editingSlider) || sender is Thumb thumb &&
                sliderThumbs.TryGetValue(editingSlider, out var thumbs) && thumbs.Contains(thumb)) EndSliderEdit();
        }
        void ObserveSliderThumbs(Slider slider) {
            ReleaseSliderThumbs(slider);
            var thumbs = slider.GetVisualDescendants().OfType<Thumb>().ToList();
            sliderThumbs[slider] = thumbs;
            foreach (var thumb in thumbs)
                thumb.AddHandler(PointerCaptureLostEvent, SliderCaptureLost, RoutingStrategies.Direct, handledEventsToo: true);
        }
        void ReleaseSliderThumbs(Slider slider) {
            if (!sliderThumbs.Remove(slider, out var thumbs)) return;
            foreach (var thumb in thumbs) thumb.RemoveHandler(PointerCaptureLostEvent, SliderCaptureLost);
        }
        void EndSliderEdit() {
            var edit = sliderEdit;
            sliderEdit = null;
            editingSlider = null;
            edit?.Dispose();
            ViewModel.RefreshInputs();
        }
        void SliderKeyDown(object? sender, KeyEventArgs args) {
            if (sender is not Slider slider || slider.Tag is not string || (args.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0) return;
            double step = slider.TickFrequency > 0 ? slider.TickFrequency : 1;
            if (args.KeyModifiers.HasFlag(KeyModifiers.Shift)) step *= .1;
            double value;
            switch (args.Key) {
                case Key.Up: case Key.Right: value = slider.Value + step; break;
                case Key.Down: case Key.Left: value = slider.Value - step; break;
                case Key.PageUp: value = slider.Value + step * 10; break;
                case Key.PageDown: value = slider.Value - step * 10; break;
                case Key.Home: value = slider.Minimum; break;
                case Key.End: value = slider.Maximum; break;
                default: return;
            }
            ApplySliderStep(slider, value);
            args.Handled = true;
        }
        void SliderWheel(object? sender, PointerWheelEventArgs args) {
            if (sender is not Slider slider || slider.Tag is not string || (args.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0) return;
            double step = slider.TickFrequency > 0 ? slider.TickFrequency : 1;
            if (args.KeyModifiers.HasFlag(KeyModifiers.Shift)) step *= .1;
            ApplySliderStep(slider, slider.Value + args.Delta.Y * step);
            args.Handled = true;
        }
        void ApplySliderStep(Slider slider, double value) {
            if (!slider.IsEffectivelyEnabled || slider.Tag is not string tag) return;
            using var edit = ViewModel.BeginInputEdit();
            edit.ApplyParameter(tag, (float)Math.Clamp(value, slider.Minimum, slider.Maximum));
            ViewModel.RefreshInputs();
        }
        void CancelInput() {
            CancelTextEdit();
            EndSliderEdit();
        }
        void VibratoEnableClicked(object sender, RoutedEventArgs e) {
            ViewModel.SetVibratoEnable();
        }

        void OnSavePortamentoPreset(object sender, RoutedEventArgs e) {
            if (VisualRoot is Window window) {
                var dialog = new TypeInDialog() {
                    Title = ThemeManager.GetString("notedefaults.preset.namenew"),
                    onFinish = name => ViewModel.SavePortamentoPreset(name),
                };
                dialog.ShowDialog(window);
            }
        }

        void OnRemovePortamentoPreset(object sender, RoutedEventArgs e) {
            ViewModel.RemoveAppliedPortamentoPreset();
        }

        void OnSaveVibratoPreset(object sender, RoutedEventArgs e) {
            if (VisualRoot is Window window) {
                var dialog = new TypeInDialog() {
                    Title = ThemeManager.GetString("notedefaults.preset.namenew"),
                    onFinish = name => ViewModel.SaveVibratoPreset(name),
                };
                dialog.ShowDialog(window);
            }
        }

        void OnRemoveVibratoPreset(object sender, RoutedEventArgs e) {
            ViewModel.RemoveAppliedVibratoPreset();
        }

        private void OnKeyDown(object? sender, KeyEventArgs e) {
            if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0 || editingBox == null) return;
            switch (e.Key) {
                case Key.Enter:
                    CommitTextEdit();
                    this.Focus();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    CancelTextEdit();
                    this.Focus();
                    e.Handled = true;
                    break;
                default:
                    break;
            }
        }

        public void OnNext(UCommand cmd, bool isUndo) {
            var lifetime = subscriptions;
            if (lifetime == null || ViewModel.IsDisposed) return;
            affinity.Post(() => {
                if (ReferenceEquals(subscriptions, lifetime) && !ViewModel.IsDisposed) OnNextCore(cmd, isUndo);
            });
        }

        void OnNextCore(UCommand cmd, bool isUndo) {
            if (cmd is UNotification notif) {
                if (cmd is LoadPartNotification) {
                    LoadPart(notif.part);
                } else if (cmd is LoadProjectNotification) {
                    LoadPart(null);
                } else if (cmd is SingersRefreshedNotification srn && srn.singer != null && ViewModel.Part != null) {
                    var singer = DocManager.Inst.Project.tracks[ViewModel.Part.trackNo].Singer;
                    if (singer != null && singer == srn.singer) {
                        LoadPart(ViewModel.Part);
                    }
                }
            } else if (cmd is TrackCommand) {
                if (cmd is RemoveTrackCommand removeTrack && ViewModel.Part != null && removeTrack.removedParts.Contains(ViewModel.Part)) {
                    LoadPart(null);
                } else if (cmd is TrackChangeRenderSettingCommand changeRenderer && ViewModel.Part != null && changeRenderer.track.TrackNo == ViewModel.Part.trackNo) {
                    LoadPart(ViewModel.Part);
                }
            } else if (cmd is ConfigureExpressionsCommand && ViewModel.Part != null) {
                LoadPart(ViewModel.Part);
            }
        }

        public void OnPhonemizerButtonClicked(object? sender, RoutedEventArgs e) {
            if (ViewModel.IsPhonemizerEnabled && sender is Button button && button.ContextMenu != null) {
                button.ContextMenu.PlacementTarget = button;
                button.ContextMenu.Open();
            }
        }
    }
}

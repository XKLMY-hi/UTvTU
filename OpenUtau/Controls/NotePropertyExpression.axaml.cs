using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using OpenUtau.App.ViewModels;

namespace OpenUtau.App.Controls {
    public partial class NotePropertyExpression : UserControl {
        NotePropertiesViewModel.NotePropertyEditSession? textEdit;
        NotePropertiesViewModel.NotePropertyEditSession? sliderEdit;
        NotePropertyExpViewModel? textModel;
        string originalText = string.Empty;
        readonly List<Thumb> sliderThumbs = new();

        public NotePropertyExpression() {
            InitializeComponent();
            slider.AddHandler(PointerPressedEvent, SliderPointerPressed, RoutingStrategies.Tunnel);
            slider.AddHandler(PointerReleasedEvent, SliderPointerReleased, RoutingStrategies.Tunnel);
            slider.AddHandler(PointerCaptureLostEvent, SliderCaptureLost, RoutingStrategies.Direct, handledEventsToo: true);
            slider.TemplateApplied += (_, _) => { EndSliderEdit(); ObserveSliderThumbs(); };
            slider.AddHandler(KeyDownEvent, SliderKeyDown, RoutingStrategies.Tunnel);
            slider.AddHandler(PointerWheelChangedEvent, SliderWheel, RoutingStrategies.Tunnel);
            slider.PropertyChanged += SliderValueChanged;
            comboBox.AddHandler(PointerPressedEvent, OnComboBoxPointerPressed, RoutingStrategies.Tunnel);
            DataContextChanged += (_, _) => CancelInput();
        }

        void OnTextBoxGotFocus(object? sender, FocusChangedEventArgs args) {
            textEdit?.Dispose();
            textModel = DataContext as NotePropertyExpViewModel;
            textEdit = textModel?.BeginInputEdit();
            originalText = ValueTextBox.Text ?? string.Empty;
        }
        void OnTextBoxLostFocus(object? sender, RoutedEventArgs args) => CommitTextEdit();
        void OnTextBoxKeyDown(object? sender, KeyEventArgs args) {
            if ((args.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0) return;
            if (args.Key == Key.Enter) {
                CommitTextEdit();
                Focus();
                args.Handled = true;
            } else if (args.Key == Key.Escape) {
                CancelTextEdit();
                Focus();
                args.Handled = true;
            }
        }
        void CommitTextEdit() {
            var edit = textEdit;
            var model = textModel;
            textEdit = null;
            textModel = null;
            try {
                if (edit != null && model != null && ReferenceEquals(DataContext, model) && originalText != ValueTextBox.Text)
                    edit.ApplyExpression(model, ValueTextBox.Text);
            } finally {
                edit?.Dispose();
                RefreshText();
            }
        }
        void CancelTextEdit() {
            textEdit?.Dispose();
            textEdit = null;
            textModel = null;
            RefreshText();
        }
        void RefreshText() {
            if (DataContext is NotePropertyExpViewModel model) {
                model.RefreshInput();
                ValueTextBox.SetCurrentValue(TextBox.TextProperty, model.Value.ToString());
            }
        }

        void SliderPointerPressed(object? sender, PointerPressedEventArgs args) {
            if (!slider.IsEffectivelyEnabled || DataContext is not NotePropertyExpViewModel model) return;
            var point = args.GetCurrentPoint(slider);
            if (point.Properties.IsLeftButtonPressed) {
                ObserveSliderThumbs();
                EndSliderEdit();
                sliderEdit = model.BeginInputEdit();
            } else if (point.Properties.IsRightButtonPressed) {
                using var edit = model.BeginInputEdit();
                edit.ApplyExpression(model, null);
                model.RefreshInput();
                args.Handled = true;
            }
        }
        void SliderValueChanged(object? sender, AvaloniaPropertyChangedEventArgs args) {
            if (args.Property == IsEffectivelyEnabledProperty && !slider.IsEffectivelyEnabled) {
                EndSliderEdit();
                return;
            }
            if (args.Property != RangeBase.ValueProperty || sliderEdit == null || DataContext is not NotePropertyExpViewModel model ||
                model.parentViewmodel.IsRefreshingInputs) return;
            sliderEdit.ApplyExpression(model, (float)slider.Value);
        }
        void SliderPointerReleased(object? sender, PointerReleasedEventArgs args) => EndSliderEdit();
        void SliderCaptureLost(object? sender, PointerCaptureLostEventArgs args) => EndSliderEdit();
        void ObserveSliderThumbs() {
            ReleaseSliderThumbs();
            foreach (var thumb in slider.GetVisualDescendants().OfType<Thumb>()) {
                sliderThumbs.Add(thumb);
                thumb.AddHandler(PointerCaptureLostEvent, SliderCaptureLost, RoutingStrategies.Direct, handledEventsToo: true);
            }
        }
        void ReleaseSliderThumbs() {
            foreach (var thumb in sliderThumbs) thumb.RemoveHandler(PointerCaptureLostEvent, SliderCaptureLost);
            sliderThumbs.Clear();
        }
        void EndSliderEdit() {
            var edit = sliderEdit;
            sliderEdit = null;
            edit?.Dispose();
            if (DataContext is NotePropertyExpViewModel model) model.RefreshInput();
        }
        void SliderKeyDown(object? sender, KeyEventArgs args) {
            if ((args.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0) return;
            double step = args.KeyModifiers.HasFlag(KeyModifiers.Shift) ? .1 : 1;
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
            ApplySliderStep(value);
            args.Handled = true;
        }
        void SliderWheel(object? sender, PointerWheelEventArgs args) {
            if ((args.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0) return;
            ApplySliderStep(slider.Value + args.Delta.Y * (args.KeyModifiers.HasFlag(KeyModifiers.Shift) ? .1 : 1));
            args.Handled = true;
        }
        void ApplySliderStep(double value) {
            if (!slider.IsEffectivelyEnabled || DataContext is not NotePropertyExpViewModel model) return;
            using var edit = model.BeginInputEdit();
            edit.ApplyExpression(model, (float)Math.Clamp(value, slider.Minimum, slider.Maximum));
            model.RefreshInput();
        }
        void OnComboBoxPointerPressed(object? sender, PointerPressedEventArgs args) {
            if (DataContext is NotePropertyExpViewModel model && args.GetCurrentPoint(comboBox).Properties.IsRightButtonPressed) {
                using var edit = model.BeginInputEdit();
                edit.ApplyExpression(model, null);
                model.RefreshInput();
                args.Handled = true;
            }
        }
        void CancelInput() {
            textEdit?.Dispose();
            textEdit = null;
            textModel = null;
            EndSliderEdit();
        }
        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs args) {
            CancelInput();
            ReleaseSliderThumbs();
            base.OnDetachedFromVisualTree(args);
        }
    }
}

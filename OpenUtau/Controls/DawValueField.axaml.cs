using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OpenUtau.App.Controls {
    /// <summary>One persistent editor with explicit commit/cancel boundaries.</summary>
    public partial class DawValueField : UserControl {
        public static readonly StyledProperty<double> ValueProperty =
            AvaloniaProperty.Register<DawValueField, double>(nameof(Value), validate: double.IsFinite);
        public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public double Minimum { get; set; } = -24;
        public double Maximum { get; set; } = 12;
        public bool IsEditing { get; private set; }
        public event EventHandler? EditingStarted;
        public event EventHandler? ValueCommitted;
        public DawValueField() { InitializeComponent(); }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            // An undo or another view supersedes this preview. LostFocus must not resurrect it.
            if (change.Property == ValueProperty && IsEditing) CancelEdit();
            if (change.Property == IsEffectivelyEnabledProperty && !IsEffectivelyEnabled) CancelEdit();
        }

        public void BeginEdit() {
            if (IsEditing || !IsEffectivelyEnabled) return;
            EditingStarted?.Invoke(this, EventArgs.Empty);
            IsEditing = true;
            Editor.Text = Value.ToString("0.0", CultureInfo.CurrentCulture);
            Readout.IsVisible = false;
            Editor.IsVisible = true;
            Editor.Focus();
            Editor.SelectAll();
        }

        public bool CommitEdit() {
            if (!IsEditing) return false;
            if (!double.TryParse(Editor.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) ||
                !double.IsFinite(value) || Maximum < Minimum) return false;
            // Close before dispatch: a host refresh or a LostFocus event must never commit twice.
            CancelEdit();
            SetCurrentValue(ValueProperty, Math.Clamp(value, Minimum, Maximum));
            ValueCommitted?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public void CancelEdit() {
            if (!IsEditing) return;
            IsEditing = false;
            Editor.IsVisible = false;
            Readout.IsVisible = true;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e) {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || IsEditing) return;
            Focus();
            if (e.ClickCount == 2) { BeginEdit(); e.Handled = true; }
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0) return;
            if (!IsEditing && (e.Key == Key.Enter || e.Key == Key.F2)) { BeginEdit(); e.Handled = true; }
        }

        void OnEditorKeyDown(object? sender, KeyEventArgs e) {
            if (e.Key == Key.Enter) { if (CommitEdit()) Focus(); e.Handled = true; }
            else if (e.Key == Key.Escape) { CancelEdit(); Focus(); e.Handled = true; }
        }

        void OnEditorLostFocus(object? sender, RoutedEventArgs e) {
            if (IsEditing && !CommitEdit()) CancelEdit();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            CancelEdit();
            base.OnDetachedFromVisualTree(e);
        }
    }
}

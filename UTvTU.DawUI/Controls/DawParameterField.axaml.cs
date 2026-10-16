using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace UTvTU.DawUI.Controls {
    /// <summary>One persistent editor with explicit commit/cancel boundaries.</summary>
    public partial class DawParameterField : UserControl {
        public static readonly StyledProperty<double> ValueProperty =
            AvaloniaProperty.Register<DawParameterField, double>(nameof(Value), validate: double.IsFinite);
        public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public static readonly StyledProperty<double> MinimumProperty = AvaloniaProperty.Register<DawParameterField, double>(nameof(Minimum), -24, validate: double.IsFinite);
        public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<DawParameterField, double>(nameof(Maximum), 12, validate: double.IsFinite);
        public static readonly StyledProperty<string> UnitProperty = AvaloniaProperty.Register<DawParameterField, string>(nameof(Unit), "");
        public static readonly StyledProperty<string> FormatProperty = AvaloniaProperty.Register<DawParameterField, string>(nameof(Format), "0.0");
        public static readonly StyledProperty<DawDensity> DensityProperty = DawStyle.DensityProperty.AddOwner<DawParameterField>();
        public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
        public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
        public string Unit { get => GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
        public string Format { get => GetValue(FormatProperty); set => SetValue(FormatProperty, value); }
        public DawDensity Density { get => GetValue(DensityProperty); set => SetValue(DensityProperty, value); }
        public TextBlock Readout { get; private set; } = null!;
        public TextBox Editor { get; private set; } = null!;
        Border surface = null!;
        bool legacy;
        public bool HasValidationError { get; private set; }
        public bool AutoFormat { get; set; } = true;
        public bool IsEditing { get; private set; }
        public event EventHandler? EditingStarted;
        public event EventHandler? ValueCommitted;
        public DawParameterField() {
            InitializeComponent();
            Readout = this.FindControl<TextBlock>("PART_Readout")!;
            Editor = this.FindControl<TextBox>("PART_Editor")!;
            surface = this.FindControl<Border>("PART_Surface")!;
            ApplyDensity(); UpdateReadout();
        }
        protected void UseLegacyPresentation() {
            legacy = true; AutoFormat = false;
            surface.Background = Brushes.Transparent; surface.BorderThickness = new Thickness(0); surface.Padding = new Thickness(0); surface.CornerRadius = new CornerRadius(0);
            MinHeight = surface.MinHeight = 16;
            Readout[!TextBlock.FontSizeProperty] = new DynamicResourceExtension("Md3DawTypeReadout");
            Editor[!TextBox.FontSizeProperty] = new DynamicResourceExtension("Md3DawTypeReadout"); Readout.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            Editor.Height = Editor.MinHeight = 16;
        }
        void ApplyDensity() {
            if (surface == null || legacy) return;
            double height = Density == DawDensity.Standard ? 40 : Density == DawDensity.Dense ? 24 : 32;
            MinHeight = surface.MinHeight = height;
            Readout.FontSize = Density == DawDensity.Dense ? 11 : 12;
            Editor.FontSize = Readout.FontSize;
            Editor.MinHeight = 0; Editor.Height = Math.Max(16, height - 8);
            Readout.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        }
        void UpdateReadout() {
            if (Readout == null || !AutoFormat) return;
            string value;
            try { value = Value.ToString(Format, CultureInfo.CurrentCulture); }
            catch (FormatException) { value = Value.ToString("0.0", CultureInfo.CurrentCulture); }
            Readout.Text = string.IsNullOrEmpty(Unit) ? value : value + " " + Unit;
        }
        void Validation(bool invalid) {
            HasValidationError = invalid; PseudoClasses.Set(":invalid", invalid);
            if (!legacy) surface[!Border.BorderBrushProperty] = new DynamicResourceExtension(invalid ? "md3.error" : IsEditing ? "md3.primary" : "md3.outline-variant");
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            // An undo or another view supersedes this preview. LostFocus must not resurrect it.
            if (change.Property == ValueProperty && IsEditing) CancelEdit();
            if (change.Property == ValueProperty || change.Property == FormatProperty || change.Property == UnitProperty) UpdateReadout();
            if (change.Property == DensityProperty) ApplyDensity();
            if (change.Property == IsEffectivelyEnabledProperty && !IsEffectivelyEnabled) CancelEdit();
        }

        public void BeginEdit() {
            if (IsEditing || !IsEffectivelyEnabled) return;
            EditingStarted?.Invoke(this, EventArgs.Empty);
            IsEditing = true;
            Validation(false);
            // Display formatting must not round the underlying value when an edit is opened and accepted unchanged.
            Editor.Text = Value.ToString(legacy ? "0.0" : "G", CultureInfo.CurrentCulture);
            Readout.IsVisible = false;
            Editor.IsVisible = true;
            Editor.Focus();
            Editor.SelectAll();
        }

        public bool CommitEdit() {
            if (!IsEditing) return false;
            if (!double.TryParse(Editor.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) ||
                !double.IsFinite(value) || Maximum < Minimum) { Validation(true); return false; }
            // Close before dispatch: a host refresh or a LostFocus event must never commit twice.
            CancelEdit();
            SetCurrentValue(ValueProperty, Math.Clamp(value, Minimum, Maximum));
            ValueCommitted?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public void CancelEdit() {
            if (!IsEditing) return;
            IsEditing = false;
            Validation(false);
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

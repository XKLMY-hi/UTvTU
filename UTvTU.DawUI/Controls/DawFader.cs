using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace UTvTU.DawUI.Controls {
    /// <summary>Owns fader input and geometry; document and audio writes belong to its host.</summary>
    public class DawFader : Canvas {
        public static readonly StyledProperty<double> ValueProperty =
            AvaloniaProperty.Register<DawFader, double>(nameof(Value), 0,
                validate: double.IsFinite);
        public static readonly StyledProperty<double> MinimumProperty =
            AvaloniaProperty.Register<DawFader, double>(nameof(Minimum), -24,
                validate: double.IsFinite);
        public static readonly StyledProperty<double> MaximumProperty =
            AvaloniaProperty.Register<DawFader, double>(nameof(Maximum), 12,
                validate: double.IsFinite);
        public static readonly StyledProperty<IBrush?> FocusBrushProperty =
            AvaloniaProperty.Register<DawFader, IBrush?>(nameof(FocusBrush));
        public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
        public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
        public IBrush? FocusBrush { get => GetValue(FocusBrushProperty); set => SetValue(FocusBrushProperty, value); }
        readonly Border focusFrame = new Border {
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
            IsHitTestVisible = false, IsVisible = false,
        };
        Border? handle;
        public Border? Handle { get => handle; set { handle = value; UpdateHandle(); } }
        public bool IsDragging { get; private set; }
        IPointer? capturedPointer;
        double grabOffset;
        public event EventHandler? GestureStarted;
        public event EventHandler? GestureEnded;
        public event EventHandler? ValueEdited;

        public DawFader() {
            Focusable = true;
            Background = Brushes.Transparent;
            Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
            Children.Add(focusFrame);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == ValueProperty || change.Property == MinimumProperty ||
                change.Property == MaximumProperty || change.Property == BoundsProperty) {
                UpdateHandle();
            }
            if (change.Property == IsFocusedProperty) focusFrame.IsVisible = IsFocused;
            if (change.Property == FocusBrushProperty) focusFrame.BorderBrush = FocusBrush;
            if (change.Property == IsEffectivelyEnabledProperty && !IsEffectivelyEnabled) EndGesture();
        }

        public void UpdateHandle() {
            if (Handle == null || Bounds.Height <= 0 || Maximum <= Minimum) return;
            double travel = Math.Max(0, Bounds.Height - Handle.Height);
            SetTop(Handle, travel * (Maximum - Math.Clamp(Value, Minimum, Maximum)) / (Maximum - Minimum));
            SetLeft(focusFrame, GetLeft(Handle) - 2);
            SetTop(focusFrame, GetTop(Handle) - 2);
            focusFrame.Width = Handle.Width + 4;
            focusFrame.Height = Handle.Height + 4;
        }

        void Edit(double value) {
            if (!IsEffectivelyEnabled || !double.IsFinite(value) || Maximum <= Minimum) return;
            value = Math.Clamp(value, Minimum, Maximum);
            if (Math.Abs(value - Value) < .000001) return;
            SetCurrentValue(ValueProperty, value);
            ValueEdited?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e) {
            base.OnPointerPressed(e);
            if (!IsEffectivelyEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Handle == null) return;
            Focus();
            EndGesture();
            double y = e.GetPosition(this).Y;
            double top = GetTop(Handle);
            grabOffset = y >= top && y <= top + Handle.Height ? y - top : Handle.Height / 2;
            IsDragging = true;
            capturedPointer = e.Pointer;
            GestureStarted?.Invoke(this, EventArgs.Empty);
            e.Pointer.Capture(this);
            EditAt(y);
            e.Handled = true;
        }

        void EditAt(double y) {
            double travel = Bounds.Height - (Handle?.Height ?? 0);
            if (travel > 0) Edit(Maximum - Math.Clamp((y - grabOffset) / travel, 0, 1) * (Maximum - Minimum));
        }

        protected override void OnPointerMoved(PointerEventArgs e) {
            base.OnPointerMoved(e);
            if (!IsDragging || e.Pointer != capturedPointer) return;
            EditAt(e.GetPosition(this).Y);
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e) {
            base.OnPointerReleased(e);
            if (!IsDragging || e.Pointer != capturedPointer) return;
            EndGesture();
            e.Handled = true;
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) {
            base.OnPointerCaptureLost(e);
            EndGesture();
        }

        public void EndGesture() {
            if (!IsDragging) return;
            IsDragging = false;
            var pointer = capturedPointer;
            capturedPointer = null;
            if (pointer?.Captured == this) pointer.Capture(null);
            GestureEnded?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            EndGesture();
            base.OnDetachedFromVisualTree(e);
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0) return;
            double step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? .1 : 1;
            switch (e.Key) {
                case Key.Up: case Key.Right: Edit(Value + step); break;
                case Key.Down: case Key.Left: Edit(Value - step); break;
                case Key.PageUp: Edit(Value + 6); break;
                case Key.PageDown: Edit(Value - 6); break;
                case Key.Home: Edit(Maximum); break;
                case Key.End: Edit(Minimum); break;
                case Key.D0: case Key.NumPad0: Edit(0); break;
                default: return;
            }
            e.Handled = true;
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e) {
            base.OnPointerWheelChanged(e);
            // Leave scrolling to the containing mixer until the user focuses this fader.
            if (!IsFocused) return;
            Edit(Value + Math.Sign(e.Delta.Y) * (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? .1 : 1));
            e.Handled = true;
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new FaderAutomationPeer(this);

        sealed class FaderAutomationPeer(DawFader owner) : ControlAutomationPeer(owner), IRangeValueProvider {
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
            protected override string GetClassNameCore() => nameof(DawFader);
            protected override object? GetProviderCore(Type providerType) =>
                providerType == typeof(IRangeValueProvider) ? this : base.GetProviderCore(providerType);
            public bool IsReadOnly => !owner.IsEffectivelyEnabled;
            public double LargeChange => 6;
            public double SmallChange => 1;
            public double Minimum => owner.Minimum;
            public double Maximum => owner.Maximum;
            public double Value => owner.Value;
            public void SetValue(double value) => owner.Edit(value);
        }
    }
}

using System;
using Avalonia;
using Avalonia.Controls;

namespace OpenUtau.App.Controls {
    /// <summary>A label and an editor, in reading order. Narrow settings columns stack
    /// them without reparenting the editor or losing its bindings and focus.</summary>
    public sealed class SettingsRow : Panel {
        public static readonly StyledProperty<double> EditorWidthProperty =
            AvaloniaProperty.Register<SettingsRow, double>(nameof(EditorWidth), 248,
                validate: value => double.IsFinite(value) && value > 0);
        public double EditorWidth { get => GetValue(EditorWidthProperty); set => SetValue(EditorWidthProperty, value); }
        static SettingsRow() { AffectsMeasure<SettingsRow>(EditorWidthProperty); }
        const double Gap = 16;
        bool IsStacked(double width) => width < EditorWidth + Gap + 156;

        protected override Size MeasureOverride(Size availableSize) {
            if (Children.Count != 2) return base.MeasureOverride(availableSize);
            double width = double.IsFinite(availableSize.Width) ? availableSize.Width : 520;
            bool stacked = IsStacked(width);
            Children[0].Measure(new Size(stacked ? width : Math.Max(0, width - EditorWidth - Gap), double.PositiveInfinity));
            Children[1].Measure(new Size(stacked ? width : EditorWidth, double.PositiveInfinity));
            return new Size(width, stacked
                ? Children[0].DesiredSize.Height + 8 + Children[1].DesiredSize.Height
                : Math.Max(58, Math.Max(Children[0].DesiredSize.Height, Children[1].DesiredSize.Height) + 12));
        }

        protected override Size ArrangeOverride(Size finalSize) {
            if (Children.Count != 2) return base.ArrangeOverride(finalSize);
            if (IsStacked(finalSize.Width)) {
                Children[0].Arrange(new Rect(0, 0, finalSize.Width, Children[0].DesiredSize.Height));
                double top = Children[0].DesiredSize.Height + 8;
                Children[1].Arrange(new Rect(0, top, finalSize.Width, Math.Max(0, finalSize.Height - top)));
            } else {
                double leftWidth = Math.Max(0, finalSize.Width - EditorWidth - Gap);
                Children[0].Arrange(new Rect(0, (finalSize.Height - Children[0].DesiredSize.Height) / 2,
                    leftWidth, Children[0].DesiredSize.Height));
                Children[1].Arrange(new Rect(leftWidth + Gap, (finalSize.Height - Children[1].DesiredSize.Height) / 2,
                    EditorWidth, Children[1].DesiredSize.Height));
            }
            return finalSize;
        }
    }
}

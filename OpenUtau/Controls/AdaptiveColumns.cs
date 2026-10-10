using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace OpenUtau.App.Controls {
    /// <summary>Settings groups retain readable fields: two columns when they fit,
    /// otherwise one column in reading order. Content is never reparented.</summary>
    public sealed class AdaptiveColumns : Panel {
        public static readonly StyledProperty<double> MinimumColumnWidthProperty =
            AvaloniaProperty.Register<AdaptiveColumns, double>(nameof(MinimumColumnWidth), 420,
                validate: value => double.IsFinite(value) && value > 0);
        public static readonly StyledProperty<double> SpacingProperty =
            AvaloniaProperty.Register<AdaptiveColumns, double>(nameof(Spacing), 16,
                validate: value => double.IsFinite(value) && value >= 0);

        static AdaptiveColumns() {
            AffectsMeasure<AdaptiveColumns>(MinimumColumnWidthProperty, SpacingProperty);
        }

        public double MinimumColumnWidth { get => GetValue(MinimumColumnWidthProperty); set => SetValue(MinimumColumnWidthProperty, value); }
        public double Spacing { get => GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

        int Columns(double width, int count) => count > 1 && width >= MinimumColumnWidth * 2 + Spacing ? 2 : 1;

        protected override Size MeasureOverride(Size availableSize) {
            var children = Children.Where(c => c.IsVisible).ToArray();
            if (children.Length == 0) return default;
            int columns = Columns(availableSize.Width, children.Length);
            double width = double.IsFinite(availableSize.Width)
                ? Math.Max(0, (availableSize.Width - Spacing * (columns - 1)) / columns) : MinimumColumnWidth;
            double desiredWidth = 0, height = 0;
            for (int row = 0; row < children.Length; row += columns) {
                double rowHeight = 0;
                for (int i = row; i < Math.Min(row + columns, children.Length); i++) {
                    children[i].Measure(new Size(width, double.PositiveInfinity));
                    rowHeight = Math.Max(rowHeight, children[i].DesiredSize.Height);
                    desiredWidth = Math.Max(desiredWidth, children[i].DesiredSize.Width);
                }
                height += rowHeight + (row == 0 ? 0 : Spacing);
            }
            return new Size(desiredWidth * columns + Spacing * (columns - 1), height);
        }

        protected override Size ArrangeOverride(Size finalSize) {
            var children = Children.Where(c => c.IsVisible).ToArray();
            int columns = Columns(finalSize.Width, children.Length);
            double width = Math.Max(0, (finalSize.Width - Spacing * (columns - 1)) / columns);
            double y = 0;
            for (int row = 0; row < children.Length; row += columns) {
                double height = children.Skip(row).Take(columns).Max(c => c.DesiredSize.Height);
                for (int i = row; i < Math.Min(row + columns, children.Length); i++) {
                    children[i].Arrange(new Rect((i - row) * (width + Spacing), y, width, height));
                }
                y += height + Spacing;
            }
            return finalSize;
        }
    }
}

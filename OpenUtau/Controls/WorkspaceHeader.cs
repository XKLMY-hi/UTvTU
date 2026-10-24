using System;
using Avalonia;
using Avalonia.Controls;

namespace OpenUtau.App.Controls {
    /// <summary>
    /// Arranges the brand, transport, view switcher and tools as intact groups.
    /// Wide windows use the original 56 DIP app bar; narrower windows move the
    /// view switcher and tools together to a second row rather than clipping controls.
    /// </summary>
    public sealed class WorkspaceHeader : Panel {
        public static readonly StyledProperty<double> CaptionInsetProperty =
            AvaloniaProperty.Register<WorkspaceHeader, double>(nameof(CaptionInset), 0,
                validate: value => double.IsFinite(value) && value >= 0);
        public double CaptionInset { get => GetValue(CaptionInsetProperty); set => SetValue(CaptionInsetProperty, value); }
        static WorkspaceHeader() { AffectsMeasure<WorkspaceHeader>(CaptionInsetProperty); }
        const double Inset = 16;
        const double Gap = 12;
        const double MainHeight = 56;
        const double ToolsHeight = 44;

        double GroupWidth(int index) => Children[index].DesiredSize.Width;
        double FullWidth => Inset * 2 + Gap * 3 + GroupWidth(0) + GroupWidth(1) + GroupWidth(2) + GroupWidth(3);
        int Rows(double width) => FullWidth + CaptionInset <= width ? 1
            : Inset * 2 + Gap + GroupWidth(0) + GroupWidth(1) + CaptionInset <= width ? 2 : 3;

        protected override Size MeasureOverride(Size availableSize) {
            if (Children.Count != 4) {
                throw new InvalidOperationException("WorkspaceHeader requires brand, transport, views and tools groups.");
            }
            foreach (var child in Children) {
                child.Measure(new Size(double.PositiveInfinity, MainHeight));
            }
            int rows = Rows(availableSize.Width);
            double width = rows == 1 ? FullWidth + CaptionInset : rows == 2
                ? Math.Max(Inset * 2 + Gap + GroupWidth(0) + GroupWidth(1) + CaptionInset,
                    Inset * 2 + Gap + GroupWidth(2) + GroupWidth(3))
                : Inset * 2 + Math.Max(GroupWidth(0) + CaptionInset, Math.Max(GroupWidth(1), GroupWidth(2) + Gap + GroupWidth(3)));
            return new Size(width, MainHeight + (rows - 1) * ToolsHeight);
        }

        protected override Size ArrangeOverride(Size finalSize) {
            int rows = Rows(finalSize.Width);
            Place(0, Inset, 0, MainHeight);
            Place(1, rows == 3 ? Inset : Inset + GroupWidth(0) + Gap, rows == 3 ? MainHeight : 0,
                rows == 3 ? ToolsHeight : MainHeight);
            double toolsX = finalSize.Width - Inset - GroupWidth(3) - (rows == 1 ? CaptionInset : 0);
            if (rows == 1) {
                Place(2, toolsX - Gap - GroupWidth(2), 0, MainHeight);
                Place(3, toolsX, 0, MainHeight);
            } else {
                double y = MainHeight + (rows - 2) * ToolsHeight;
                Place(2, Inset, y, ToolsHeight);
                Place(3, toolsX, y, ToolsHeight);
            }
            return finalSize;
        }

        void Place(int index, double x, double y, double height) {
            Children[index].Arrange(new Rect(x, y, GroupWidth(index), height));
        }
    }
}

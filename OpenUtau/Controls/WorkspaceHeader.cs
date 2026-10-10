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
        const double Inset = 16;
        const double Gap = 12;
        const double MainHeight = 56;
        const double ToolsHeight = 44;

        double GroupWidth(int index) => Children[index].DesiredSize.Width;
        double FullWidth => Inset * 2 + Gap * 3 + GroupWidth(0) + GroupWidth(1) + GroupWidth(2) + GroupWidth(3);

        protected override Size MeasureOverride(Size availableSize) {
            if (Children.Count != 4) {
                throw new InvalidOperationException("WorkspaceHeader requires brand, transport, views and tools groups.");
            }
            foreach (var child in Children) {
                child.Measure(new Size(double.PositiveInfinity, MainHeight));
            }
            bool singleRow = FullWidth <= availableSize.Width;
            double width = singleRow ? FullWidth : Inset * 2 + Gap +
                Math.Max(GroupWidth(0) + GroupWidth(1), GroupWidth(2) + GroupWidth(3));
            return new Size(width, singleRow ? MainHeight : MainHeight + ToolsHeight);
        }

        protected override Size ArrangeOverride(Size finalSize) {
            bool singleRow = FullWidth <= finalSize.Width;
            Place(0, Inset, 0, MainHeight);
            Place(1, Inset + GroupWidth(0) + Gap, 0, MainHeight);
            double toolsX = finalSize.Width - Inset - GroupWidth(3);
            if (singleRow) {
                Place(2, toolsX - Gap - GroupWidth(2), 0, MainHeight);
                Place(3, toolsX, 0, MainHeight);
            } else {
                Place(2, Inset, MainHeight, ToolsHeight);
                Place(3, toolsX, MainHeight, ToolsHeight);
            }
            return finalSize;
        }

        void Place(int index, double x, double y, double height) {
            Children[index].Arrange(new Rect(x, y, GroupWidth(index), height));
        }
    }
}

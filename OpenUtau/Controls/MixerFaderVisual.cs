using System;
using Avalonia.Controls;

namespace OpenUtau.App.Controls;

/// <summary>Positions mixer scale marks in the same coordinates as the existing fader handle.</summary>
internal static class MixerFaderVisual {
    private static readonly double[] TickDb = { 12, 6, 0, -6, -12, -18, -24, 3, -15 };

    internal static void Arrange(DawFader fader) {
        double height = fader.Bounds.Height;
        double handleHeight = fader.Handle?.Height ?? 20;
        double travel = Math.Max(0, height - handleHeight);
        double range = fader.Maximum - fader.Minimum;
        if (height <= 0 || range <= 0) return;

        foreach (var child in fader.Children) {
            string? name = child.Name;
            bool label = name?.StartsWith("FaderLabel", StringComparison.Ordinal) == true;
            string prefix = label ? "FaderLabel" : "FaderTick";
            if (name == null || !name.StartsWith(prefix, StringComparison.Ordinal) ||
                !int.TryParse(name.AsSpan(prefix.Length), out int number) ||
                number < 1 || number > TickDb.Length) continue;

            double center = handleHeight / 2 + travel *
                (fader.Maximum - Math.Clamp(TickDb[number - 1], fader.Minimum, fader.Maximum)) / range;
            double top = label ? Math.Clamp(center - 6, 0, Math.Max(0, height - 12)) : center - .5;
            if (Math.Abs(Canvas.GetTop(child) - top) > .01) Canvas.SetTop(child, top);
        }
    }
}

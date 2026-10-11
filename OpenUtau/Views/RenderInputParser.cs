using System;
using System.Globalization;
using OpenUtau.Core.Ustx;

namespace OpenUtau.App.Views {
    internal static class RenderInputParser {
        internal static bool TrySeconds(string? text, out double seconds) {
            return (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out seconds) ||
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)) &&
                double.IsFinite(seconds) && seconds >= 0;
        }
        internal static bool TryRange(UProject project, int mode, string? startText, string? endText,
            int selectionStart, int selectionEnd, out int start, out int end) {
            start = 0;
            end = project.EndTick;
            if (mode == 1) { start = selectionStart; end = selectionEnd; }
            else if (mode == 2) {
                if (!TrySeconds(startText, out var startSeconds) || !TrySeconds(endText, out var endSeconds) ||
                    endSeconds <= startSeconds || endSeconds > int.MaxValue / 1000d) return false;
                start = project.timeAxis.MsPosToTickPos(startSeconds * 1000);
                end = project.timeAxis.MsPosToTickPos(endSeconds * 1000);
            }
            return start >= 0 && end > start;
        }
    }
}

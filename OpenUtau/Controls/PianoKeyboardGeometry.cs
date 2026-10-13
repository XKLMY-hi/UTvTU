using System;
using Avalonia;
using OpenUtau.Core;
using OpenUtau.Core.Util;

namespace OpenUtau.App.Controls {
    /// <summary>Shared geometry for drawn piano keys and pointer audition.</summary>
    internal static class PianoKeyboardGeometry {
        static bool IsBlack(int tone) => MusicMath.IsBlackKey((tone % 12 + 12) % 12);
        static double Center(int tone, double height, double offset) =>
            (ViewConstants.MaxTone - 1 - tone - offset + .5) * height;

        internal static Rect KeyBounds(int tone, double width, double height, double offset) {
            double center = Center(tone, height, offset);
            if (IsBlack(tone)) {
                double inset = Math.Min(1.5, height * .12);
                return new Rect(0, center - height / 2 + inset, width * .62, height - inset * 2);
            }
            int above = tone + 1, below = tone - 1;
            while (IsBlack(above)) above++;
            while (IsBlack(below)) below--;
            double top = (center + Center(above, height, offset)) / 2;
            double bottom = (center + Center(below, height, offset)) / 2;
            return new Rect(0, top, width, bottom - top);
        }

        internal static int ToneAt(Point point, double width, double height, double offset) {
            if (height <= 0 || !double.IsFinite(height) || !double.IsFinite(offset)) return 60;
            int rowTone = Math.Clamp(ViewConstants.MaxTone - 1 - (int)Math.Floor(point.Y / height + offset), 0, ViewConstants.MaxTone - 1);
            if (IsBlack(rowTone) && KeyBounds(rowTone, width, height, offset).Contains(point)) return rowTone;
            for (int tone = Math.Max(0, rowTone - 2); tone <= Math.Min(ViewConstants.MaxTone - 1, rowTone + 2); tone++) {
                if (!IsBlack(tone)) {
                    var rect = KeyBounds(tone, width, height, offset);
                    // Captured drags may leave the keyboard horizontally; keep the same white key.
                    if (point.Y >= rect.Top && point.Y < rect.Bottom) return tone;
                }
            }
            return rowTone;
        }
    }
}

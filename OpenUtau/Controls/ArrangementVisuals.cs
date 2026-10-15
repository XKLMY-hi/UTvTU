using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpenUtau.Core.Util;
using OpenUtau.Core.Ustx;

namespace OpenUtau.App.Controls {
    // Presentation only. Stable brushes also keep TextLayoutCache keys bounded while scrolling.
    internal sealed class ArrangementVisuals {
        static readonly Dictionary<(Color, Color, Color, bool, bool), ArrangementVisuals> clips = new();
        internal readonly SolidColorBrush Body, Header, Edge, Ink, Detail, Accent;
        ArrangementVisuals(Color accent, Color surface, Color ink, bool selected, bool muted) {
            if (muted) accent = Mix(surface, ink, .28);
            Body = new(Mix(surface, accent, selected ? .24 : .12));
            Header = new(Mix(surface, accent, selected ? .42 : .26));
            Edge = new(Mix(surface, accent, selected ? .95 : .52));
            Ink = new(muted ? Mix(surface, ink, .55) : ink);
            Detail = new(Mix(accent, ink, muted ? .20 : ThemeManager.IsDarkMode ? .12 : .30));
            Accent = new(accent);
        }
        internal static ArrangementVisuals For(UTrack? track, bool selected) {
            var surface = ColorOf("md3.surface-container", ThemeManager.BackgroundBrush);
            var ink = ColorOf("md3.on-surface", ThemeManager.ForegroundBrush);
            var accent = Preferences.Default.UseTrackColor && track != null
                ? ThemeManager.GetTrackColor(track.TrackColor).AccentColor.Color
                : (ThemeManager.AccentBrush1 as ISolidColorBrush)?.Color ?? ink;
            var key = (accent, surface, ink, selected, track?.Muted == true);
            if (!clips.TryGetValue(key, out var result)) {
                // Color edits / theme changes should not retain an unlimited number of brushes.
                if (clips.Count > 256) clips.Clear();
                clips[key] = result = new(accent, surface, ink, selected, track?.Muted == true);
            }
            return result;
        }
        internal static IBrush Brush(string key, IBrush? fallback) =>
            Application.Current?.TryFindResource(key, out var value) == true && value is IBrush brush ? brush : fallback ?? Brushes.Gray;
        internal static Color ColorOf(string key, IBrush? fallback) =>
            (Brush(key, fallback) as ISolidColorBrush)?.Color ?? Avalonia.Media.Colors.Gray;
        internal static Color Mix(Color background, Color foreground, double amount) => Color.FromRgb(
            (byte)Math.Round(background.R + (foreground.R - background.R) * amount),
            (byte)Math.Round(background.G + (foreground.G - background.G) * amount),
            (byte)Math.Round(background.B + (foreground.B - background.B) * amount));
    }
}

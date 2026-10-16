using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpenUtau.Core.Util;
using OpenUtau.Core.Theming;
using OpenUtau.Theming;
using OpenUtau.Core.Ustx;

namespace OpenUtau.App.Controls {
    // Presentation only. Stable brushes also keep TextLayoutCache keys bounded while scrolling.
    internal sealed class ArrangementVisuals {
        // Palette generation happens once per seed/theme/contrast, never per rendered frame.
        static readonly Dictionary<(Color, Color, Color, Color, bool, bool), ArrangementVisuals> clips = new();
        internal readonly SolidColorBrush Body, Edge, Ink, Detail, Accent;

        ArrangementVisuals(Color accent, Color container, Color ink, Color detail, Color edge) {
            Body = new(container);
            Ink = new(ink);
            Detail = new(detail);
            Edge = new(edge);
            Accent = new(accent);
        }

        internal static ArrangementVisuals For(UTrack? track, bool selected) {
            var pool = ColorPool.Current;
            bool dark = ThemeManager.IsDarkMode;
            bool muted = track?.Muted == true;
            var accent = Preferences.Default.UseTrackColor && track != null
                ? ThemeManager.GetTrackColor(track.TrackColor).AccentColor.Color
                : pool.Color(Md3Role.Primary);
            Color Role(Md3Role role) {
                if (!Preferences.Default.UseTrackColor || track == null) return pool.Color(role);
                uint seed = 0xFF000000u | (uint)(accent.R << 16) | (uint)(accent.G << 8) | accent.B;
                return Md3ColorPool.ToColor(ColorPool.Derived(seed).Get(role));
            }
            // Fixed tonal roles keep musical clips soft and filled on a dark editing surface.
            // Light mode uses the corresponding container pair. Selection swaps the tone, not the hue.
            var container = Role(dark
                ? selected ? Md3Role.PrimaryFixed : Md3Role.PrimaryFixedDim
                : selected ? Md3Role.PrimaryFixedDim : Md3Role.PrimaryContainer);
            var ink = Role(dark || selected ? Md3Role.OnPrimaryFixed : Md3Role.OnPrimaryContainer);
            var detail = Role(Md3Role.OnPrimaryFixedVariant);
            var edge = Role(Md3Role.OnPrimaryFixedVariant);
            if (muted) {
                container = pool.Color(selected ? Md3Role.SurfaceContainerHighest : Md3Role.SurfaceContainerHigh);
                ink = pool.Color(Md3Role.OnSurfaceVariant);
                detail = Mix(container, ink, .55);
                edge = ink;
            }
            var cacheKey = (accent, container, ink, detail, selected, muted);
            if (!clips.TryGetValue(cacheKey, out var result)) {
                if (clips.Count > 256) clips.Clear();
                clips[cacheKey] = result = new(accent, container, ink, detail, edge);
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

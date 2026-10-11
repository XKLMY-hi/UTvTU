using System;
using System.Globalization;
using System.Runtime.InteropServices;
using OpenUtau.App;
using OpenUtau.Core.Theming;
using OpenUtau.Core.Util;

namespace OpenUtau.Theming;

/// <summary>Stable desktop theme source; musical data colors remain independent.</summary>
public static class Md3Appearance {
    [DllImport("dwmapi.dll", PreserveSig = true)]
    static extern int DwmGetColorizationColor(out uint color, [MarshalAs(UnmanagedType.Bool)] out bool opaque);

    public static bool TryWindowsAccent(out uint seed) {
        seed = Preferences.Default.ThemeSeed;
        if (!OperatingSystem.IsWindows()) return false;
        try {
            if (DwmGetColorizationColor(out uint color, out _) != 0) return false;
            // DWM returns AARRGGBB. Theme seeds are always opaque.
            seed = color | 0xFF000000;
            return true;
        } catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    public static Md3SchemeVariant Scheme => Enum.TryParse<Md3SchemeVariant>(
        Preferences.Default.ThemeScheme, out var value) && Enum.IsDefined(value) ? value : Md3SchemeVariant.TonalSpot;
    public static double Contrast => Math.Clamp(Preferences.Default.ThemeContrast, 0, 2) * 0.5;

    public static bool TryParseSeed(string? text, out uint seed) {
        string value = (text ?? "").Trim().TrimStart('#');
        if (value.Length == 6 && uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out seed)) {
            seed |= 0xFF000000;
            return true;
        }
        seed = 0;
        return false;
    }

    public static void Apply(bool isDark) {
        uint seed = Preferences.Default.ThemeSeed | 0xFF000000;
        if (Preferences.Default.ThemeColorSource == "Windows" && TryWindowsAccent(out uint accent)) seed = accent;
        ColorPool.Initialize(seed, Scheme, isDark, Contrast);
        Md3Motion.ApplyPreference();
    }

    public static void Refresh() => ThemeManager.Apply(Preferences.Default.ThemeName);
}

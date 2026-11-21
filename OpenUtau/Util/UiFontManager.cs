using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.App;

/// <summary>
/// Loads an optional custom UI font (file or installed system family) into Avalonia
/// and exposes it through UTvTU's <c>PlusFontFamily</c> / <c>ui.fontfamily</c> resources,
/// preserving HarmonyOS Sans SC as the default and glyph fallback.
/// </summary>
public static class UiFontManager {
    private static readonly Uri CollectionKey = new("fonts:OpenUtau.CustomUiFont", UriKind.Absolute);
    private const string DefaultFallbackStack = "HarmonyOS Sans SC, Segoe UI, Microsoft YaHei UI, sans-serif";
    public static readonly FontFamily DefaultFontFamily = new(DefaultFallbackStack);

    private static bool customFontApplied;
    private static string? loadedFontPath;
    private static string? loadedFamilyName;

    public static bool IsSupportedFontPath(string? path) {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
            return false;
        }
        var extension = Path.GetExtension(path);
        return extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".otf", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetSuggestedFamilyName(string path) {
        return Path.GetFileNameWithoutExtension(path).Trim();
    }

    public static List<string> GetAvailableFontFamilies() {
        var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try {
            foreach (var family in FontManager.Current.SystemFonts) {
                if (!string.IsNullOrWhiteSpace(family.Name)) {
                    families.Add(family.Name);
                }
            }
        } catch (Exception e) {
            Log.Debug(e, "Failed to enumerate system UI fonts");
        }
        if (!string.IsNullOrWhiteSpace(loadedFamilyName)) {
            families.Add(loadedFamilyName);
        }
        var result = new List<string>(families);
        result.Sort(StringComparer.CurrentCultureIgnoreCase);
        return result;
    }

    /// <summary>
    /// Re-applies the configured font after startup, language switch, or preference edit.
    /// </summary>
    public static void Apply() {
        if (Application.Current == null) {
            return;
        }

        var resources = Application.Current.Resources;
        if (customFontApplied) {
            resources.Remove("PlusFontFamily");
            resources.Remove("ui.fontfamily");
            customFontApplied = false;
            Controls.TextLayoutCache.Clear();
        }

        var path = Preferences.Default.UiFontPath;
        if (!IsSupportedFontPath(path)) {
            if (loadedFontPath != null) {
                FontManager.Current.RemoveFontCollection(CollectionKey);
                loadedFontPath = null;
                loadedFamilyName = null;
            }
            var systemFamilyName = Preferences.Default.UiFontFamily?.Trim();
            if (!string.IsNullOrWhiteSpace(systemFamilyName)) {
                try {
                    var fontFamily = new FontFamily($"{systemFamilyName}, {DefaultFallbackStack}");
                    resources["PlusFontFamily"] = fontFamily;
                    resources["ui.fontfamily"] = fontFamily;
                    customFontApplied = true;
                    Controls.TextLayoutCache.Clear();
                } catch (Exception e) {
                    Log.Warning(e, "Failed to apply UI font family {family}", systemFamilyName);
                    resources.Remove("PlusFontFamily");
                    resources.Remove("ui.fontfamily");
                }
            }
            return;
        }

        try {
            if (!string.Equals(loadedFontPath, path, StringComparison.OrdinalIgnoreCase)) {
                if (loadedFontPath != null) {
                    FontManager.Current.RemoveFontCollection(CollectionKey);
                }
                var collection = new LocalFontCollection(CollectionKey, path!);
                FontManager.Current.AddFontCollection(collection);
                if (collection.Count == 0 || string.IsNullOrWhiteSpace(collection.FamilyName)) {
                    throw new InvalidDataException("The font file does not contain a readable typeface.");
                }
                loadedFontPath = path;
                loadedFamilyName = collection.FamilyName;
            }
            if (string.IsNullOrWhiteSpace(loadedFamilyName)) {
                throw new InvalidDataException("The font file does not contain a readable typeface.");
            }

            var familyName = string.IsNullOrWhiteSpace(Preferences.Default.UiFontFamily)
                ? loadedFamilyName!
                : Preferences.Default.UiFontFamily.Trim();
            var primaryFamily = new FontFamily($"{CollectionKey.AbsoluteUri}#{familyName}");
            if (!FontManager.Current.TryGetGlyphTypeface(
                    new Typeface(primaryFamily, FontStyle.Normal, FontWeight.Normal, FontStretch.Normal),
                    out _)) {
                familyName = loadedFamilyName!;
            }

            var fontFamily = new FontFamily($"{CollectionKey.AbsoluteUri}#{familyName}, {DefaultFallbackStack}");
            Preferences.Default.UiFontFamily = familyName;
            resources["PlusFontFamily"] = fontFamily;
            resources["ui.fontfamily"] = fontFamily;
            customFontApplied = true;
            Controls.TextLayoutCache.Clear();
        } catch (Exception e) {
            FontManager.Current.RemoveFontCollection(CollectionKey);
            loadedFontPath = null;
            loadedFamilyName = null;
            Log.Warning(e, "Failed to load custom UI font {path}", path);
            resources.Remove("PlusFontFamily");
            resources.Remove("ui.fontfamily");
            Controls.TextLayoutCache.Clear();
        }
    }

    private sealed class LocalFontCollection : FontCollectionBase {
        private readonly Uri key;
        public string FamilyName { get; private set; } = string.Empty;

        public LocalFontCollection(Uri key, string path) {
            this.key = key;
            using var stream = File.OpenRead(path);
            if (!TryAddGlyphTypeface(stream, out var glyphTypeface)) {
                return;
            }
            FamilyName = glyphTypeface.FamilyName;
            AddFontFamily(new FontFamily(key, FamilyName));
        }

        public override Uri Key => key;
    }
}

using System;
using System.Collections.Generic;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace OpenUtau.App.Controls {
    static class TextLayoutCache {
        private static readonly Dictionary<Tuple<string, IBrush, double, bool, FontFamily>, TextLayout> cache
            = new Dictionary<Tuple<string, IBrush, double, bool, FontFamily>, TextLayout>();

        public static void Clear() {
            cache.Clear();
        }

        public static TextLayout Get(string text, IBrush brush, double fontSize, bool bold = false, bool useUiFont = true) {
            var fontFamily = useUiFont ? ThemeManager.UiFontFamily : UiFontManager.DefaultFontFamily;
            var key = Tuple.Create(text, brush, fontSize, bold, fontFamily);
            if (!cache.TryGetValue(key, out var textLayout)) {
                var fontWeight = bold ? FontWeight.Bold : FontWeight.Normal;
                textLayout = new TextLayout(
                    text,
                    new Typeface(fontFamily, weight: fontWeight),
                    fontSize,
                    brush,
                    TextAlignment.Left,
                    TextWrapping.NoWrap);
                cache.Add(key, textLayout);
            }
            return textLayout;
        }
    }
}

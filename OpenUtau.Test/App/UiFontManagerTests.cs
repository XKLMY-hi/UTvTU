using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using OpenUtau.App;
using OpenUtau.App.Controls;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Test.App;

[Collection("WorkspaceSession")]
public class UiFontManagerTests {
    [Fact]
    public void RejectsMissingOrNonFontPaths() {
        Assert.False(UiFontManager.IsSupportedFontPath(null));
        Assert.False(UiFontManager.IsSupportedFontPath(""));
        Assert.False(UiFontManager.IsSupportedFontPath("nonexistent-font.ttf"));
        Assert.Equal("MyFont", UiFontManager.GetSuggestedFamilyName(@"C:\Fonts\MyFont.otf"));
    }

    [AvaloniaFact]
    public void ApplyAndResetPreserveHarmonyOsFallbackAndSwitchTextLayoutCache() {
        var origPath = Preferences.Default.UiFontPath;
        var origFamily = Preferences.Default.UiFontFamily;
        try {
            Preferences.Default.UiFontPath = string.Empty;
            Preferences.Default.UiFontFamily = "Segoe UI";
            UiFontManager.Apply();

            Assert.True(Application.Current!.Resources.TryGetValue("PlusFontFamily", out var overrideVal));
            var overrideFont = Assert.IsType<FontFamily>(overrideVal);
            Assert.Equal("Segoe UI", overrideFont.Name);
            Assert.Contains("HarmonyOS Sans SC", overrideFont.FamilyNames);

            var uiLayout = TextLayoutCache.Get("a", Brushes.White, 12, useUiFont: true);
            var defaultLayout = TextLayoutCache.Get("a", Brushes.White, 12, useUiFont: false);
            Assert.NotSame(uiLayout, defaultLayout);

            Preferences.Default.UiFontFamily = string.Empty;
            UiFontManager.Apply();

            Assert.False(Application.Current.Resources.ContainsKey("PlusFontFamily"));
            Assert.Contains("HarmonyOS Sans SC", ThemeManager.UiFontFamily.Name);
        } finally {
            Preferences.Default.UiFontPath = origPath;
            Preferences.Default.UiFontFamily = origFamily;
            UiFontManager.Apply();
        }
    }
}

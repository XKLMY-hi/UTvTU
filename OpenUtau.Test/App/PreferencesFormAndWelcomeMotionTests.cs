using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App.Controls;
using OpenUtau.App.Views;
using OpenUtau.Theming;
using ReactiveUI;
using Xunit;

namespace OpenUtau.Test.App {
    [Collection("Theme")]
    public class PreferencesFormAndWelcomeMotionTests {
        static void Settle(Window window) {
            for (int i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        }
        static void Activate(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        [AvaloniaTheory]
        [InlineData(800)]
        [InlineData(1440)]
        public void SettingsAreFlatForms_AndAppearanceAsideReflowsWithoutReplacingFields(double width) {
            var view = new PreferencesView();
            var window = new Window { Width = width, Height = 800, Content = view };
            try {
                window.Show(); Settle(window);
                foreach (string category in new[] { "Audio", "Playback", "Library", "Appearance", "Editor", "General", "Midi", "About" }) {
                    Activate(view.FindControl<Button>("Nav" + category)!); Settle(window);
                    var page = view.FindControl<Control>("Page" + category)!;
                    foreach (var section in page.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("settingsSection"))) {
                        Assert.Equal(default, section.CornerRadius);
                        Assert.Equal(default, section.BorderThickness);
                        Assert.Equal(default, section.Padding);
                        Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(section.Background).Color.A);
                    }
                    if (page is AdaptiveColumns form && category != "Appearance") {
                        Assert.Equal(1, form.MaximumColumns);
                        if (form.Children.Count == 2) Assert.True(form.Children[1].Bounds.Top >= form.Children[0].Bounds.Bottom);
                    }
                }
                Activate(view.FindControl<Button>("NavAppearance")!); Settle(window);
                var appearance = view.FindControl<AdaptiveColumns>("PageAppearance")!;
                var left = appearance.Children[0]; var aside = appearance.Children[1];
                if (width >= 1440) {
                    Assert.Equal(340, aside.Bounds.Width);
                    Assert.True(left.Bounds.Width > aside.Bounds.Width);
                } else Assert.True(aside.Bounds.Top >= left.Bounds.Bottom);
                window.Width = width == 800 ? 1440 : 800; Settle(window);
                Assert.Same(left, appearance.Children[0]); Assert.Same(aside, appearance.Children[1]);
                Assert.True(aside.Bounds.Right <= appearance.Bounds.Width);
            } finally { window.Close(); view.ReleaseSession(); }
        }

        public sealed class ThemeFields : ReactiveObject {
            string name = "Dark";
            int scheme, contrast;
            public string ThemeName { get => name; set => this.RaiseAndSetIfChanged(ref name, value); }
            public string[] ThemeItems { get; } = new[] { "Light", "Dark", "Installed theme" };
            public bool HasAdditionalThemes => true;
            public int ThemeSchemeIndex { get => scheme; set => this.RaiseAndSetIfChanged(ref scheme, value); }
            public int ThemeContrastIndex { get => contrast; set => this.RaiseAndSetIfChanged(ref contrast, value); }
            public string[] ThemeSchemeItems { get; } = new[] { "Tonal", "Vibrant", "Neutral" };
            public string[] ThemeContrastItems { get; } = new[] { "Standard", "Medium", "High" };
        }
        [AvaloniaFact]
        public void ThemeSegmentsAndInstalledThemePickerShareTheOriginalValue() {
            var model = new ThemeFields(); var view = new PreferencesView { DataContext = model };
            var window = new Window { Width = 1440, Height = 900, Content = view };
            try {
                window.Show(); Activate(view.FindControl<Button>("NavAppearance")!); Settle(window);
                var themes = view.GetVisualDescendants().OfType<RadioButton>().Where(r => r.GroupName == "AppearanceTheme").ToArray();
                Assert.Equal(2, themes.Length); Assert.True(themes[1].IsChecked);
                themes[0].IsChecked = true; Settle(window); Assert.Equal("Light", model.ThemeName);
                var picker = view.FindControl<ComboBox>("ThemeSelector")!;
                Assert.True(picker.IsVisible);
                picker.SelectedItem = "Installed theme"; Settle(window);
                Assert.Equal("Installed theme", model.ThemeName);
                Assert.All(themes, theme => Assert.False(theme.IsChecked));
                var choices = view.GetVisualDescendants().OfType<RadioButton>().ToArray();
                choices.Where(r => r.GroupName == "AppearanceScheme").ElementAt(1).IsChecked = true;
                choices.Where(r => r.GroupName == "AppearanceContrast").ElementAt(2).IsChecked = true;
                Settle(window);
                Assert.Equal(1, model.ThemeSchemeIndex); Assert.Equal(2, model.ThemeContrastIndex);
                Assert.Equal("Installed theme", model.ThemeName);
            } finally { window.Close(); view.ReleaseSession(); }
        }

        [AvaloniaTheory]
        [InlineData(800)]
        [InlineData(1440)]
        public void WelcomeMorphIsVisible_ChangesWithNavigation_AndStopsWithReducedMotionAndDetach(double width) {
            var view = new WelcomeView();
            var window = new Window { Width = width, Height = 800, Content = view };
            Md3Motion.SetReduced(window, false);
            try {
                window.Show(); Settle(window);
                var shape = view.FindControl<MaterialMorph>("HeaderShape")!;
                var art = view.FindControl<Viewbox>("HeroIllustration")!;
                Assert.True(art.IsEffectivelyVisible); Assert.False(art.IsHitTestVisible);
                Assert.Equal(MaterialShapeKind.Cookie7, shape.Kind);
                Activate(view.FindControl<Button>("NavNew")!);
                Assert.Equal(MaterialShapeKind.Arch, shape.Kind);
                Assert.True(shape.HasActiveAnimation);
                Md3Motion.SetReduced(window, true); Settle(window);
                Assert.False(shape.HasActiveAnimation);
                Activate(view.FindControl<Button>("NavTemplates")!); Settle(window);
                Assert.Equal(MaterialShapeKind.Square, shape.Kind); Assert.False(shape.HasActiveAnimation);
                Md3Motion.SetReduced(window, false);
                Activate(view.FindControl<Button>("NavOpen")!);
                Assert.True(shape.HasActiveAnimation);
                window.Content = null; Settle(window);
                Assert.False(shape.HasActiveAnimation);
            } finally { window.Close(); }
        }
    }
}

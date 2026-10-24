using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App;
using OpenUtau.App.Controls;
using OpenUtau.App.Views;
using OpenUtau.Core.Theming;
using OpenUtau.Theming;
using Xunit;

namespace OpenUtau.Test.App {
    [Collection("Theme")]
    public class PenPageInteractionTests {
        static void Settle(Window window) {
            for (int i = 0; i < 3; i++) {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
            }
        }

        [AvaloniaFact]
        public void SettingsSearch_FindsRealLabels_HandlesNoResults_AndRestoresNavigation() {
            var view = new PreferencesView();
            var window = new Window { Width = 1000, Height = 700, Content = view };
            try {
                window.Show(); Settle(window);
                var arrow = Assert.Single(view.FindControl<Button>("SettingsBack")!.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>());
                Assert.NotNull(arrow.Data);
                var search = view.FindControl<TextBox>("SettingsSearch")!;
                search.Text = ThemeManager.GetString("prefs.motion.reduce"); Settle(window);
                Assert.True(view.FindControl<AdaptiveColumns>("PageAppearance")!.IsVisible);
                Assert.True(view.FindControl<Button>("NavAppearance")!.Classes.Contains("selected"));
                Assert.False(view.FindControl<Button>("NavAudio")!.IsVisible);
                search.Text = "no-setting-with-this-name-725194"; Settle(window);
                Assert.True(view.FindControl<TextBlock>("SettingsNoResults")!.IsVisible);
                Assert.False(view.FindControl<AdaptiveColumns>("PageAppearance")!.IsVisible);
                search.Text = ""; Settle(window);
                Assert.False(view.FindControl<TextBlock>("SettingsNoResults")!.IsVisible);
                Assert.True(view.FindControl<AdaptiveColumns>("PageAppearance")!.IsVisible);
                Assert.All(view.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("navItem")), b => Assert.True(b.IsVisible));
            } finally { window.Close(); view.ReleaseSession(); }
        }

        [AvaloniaFact]
        public void WelcomeSearch_ReturnsToRecent_AndKeepsKeyboardNavigationAvailable() {
            var view = new WelcomeView();
            var window = new Window { Width = 1000, Height = 700, Content = view };
            try {
                window.Show(); Settle(window);
                view.FindControl<Button>("NavTemplates")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(window);
                Assert.True(view.FindControl<Grid>("PageTemplates")!.IsVisible);
                var search = view.FindControl<TextBox>("SearchBox")!;
                search.Text = "a real project query"; Settle(window);
                Assert.True(view.FindControl<Grid>("PageRecent")!.IsVisible);
                Assert.Equal("a real project query", search.Text);
                Assert.True(search.IsFocused);
            } finally { window.Close(); }
        }

        [AvaloniaTheory]
        [InlineData(false)]
        [InlineData(true)]
        public void AppearanceSelection_UsesReadableRolePairs_AndPreviewUpdatesInPlace(bool dark) {
            ColorPool.Initialize(0xFF5B5BD6, Md3SchemeVariant.TonalSpot, dark);
            var view = new PreferencesView();
            var window = new Window { Width = 1440, Height = 900, Content = view };
            try {
                window.Show();
                view.FindControl<Button>("NavAppearance")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(window);
                var page = view.FindControl<AdaptiveColumns>("PageAppearance")!;
                var chip = page.GetVisualDescendants().OfType<RadioButton>().First(b => b.Classes.Contains("chip"));
                chip.IsChecked = true; Settle(window);
                Assert.Equal(ColorPool.Current.Color(Md3Role.SecondaryContainer), Assert.IsAssignableFrom<ISolidColorBrush>(chip.Background).Color);
                Assert.Equal(ColorPool.Current.Color(Md3Role.OnSecondaryContainer), Assert.IsAssignableFrom<ISolidColorBrush>(chip.Foreground).Color);
                var preview = view.FindControl<Border>("AppearancePreview")!;
                ColorPool.Initialize(0xFF00897B, Md3SchemeVariant.Vibrant, !dark); Settle(window);
                Assert.Same(preview, view.FindControl<Border>("AppearancePreview"));
                Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(preview.Background).Color.A);
                var note = preview.GetVisualDescendants().OfType<TextBlock>()
                    .Single(t => t.Text == ThemeManager.GetString("prefs.preview.note"));
                Assert.Equal(ColorPool.Current.Color(Md3Role.PrimaryContainer), Assert.IsAssignableFrom<ISolidColorBrush>(note.GetVisualAncestors().OfType<Border>().First().Background).Color);
                window.Width = 800; Settle(window);
                var first = page.Children[0]; var second = page.Children[1];
                Assert.True(second.Bounds.Top >= first.Bounds.Bottom);
                Assert.All(page.GetVisualDescendants().OfType<SettingsRow>(), row => {
                    foreach (var child in row.Children) {
                        Assert.True(child.Bounds.X >= 0 && child.Bounds.Right <= row.Bounds.Width + 0.1);
                    }
                });
            } finally { window.Close(); view.ReleaseSession(); }
        }

        [AvaloniaTheory]
        [InlineData(340)]
        [InlineData(520)]
        public void SettingsRow_ReflowsTheSameEditor_WithoutLosingText(double width) {
            var label = new TextBlock { Text = "A long localized settings label", TextWrapping = TextWrapping.Wrap };
            var editor = new TextBox { Text = "unsaved field value", Height = 40, MinHeight = 40, Margin = default };
            var row = new SettingsRow { Children = { label, editor } };
            row.Measure(new Size(width, double.PositiveInfinity));
            row.Arrange(new Rect(0, 0, width, row.DesiredSize.Height));
            if (width < 420) Assert.True(editor.Bounds.Top >= label.Bounds.Bottom + 8);
            else Assert.True(editor.Bounds.Left > label.Bounds.Right);
            row.Measure(new Size(width == 340 ? 520 : 340, double.PositiveInfinity));
            row.Arrange(new Rect(0, 0, width == 340 ? 520 : 340, row.DesiredSize.Height));
            Assert.Same(editor, row.Children[1]);
            Assert.Equal("unsaved field value", editor.Text);
            Assert.True(editor.Bounds.Right <= row.Bounds.Width);
        }
    }
}

using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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
    public class WorkspacePageLayoutTests {
        static void Settle(Window window) {
            for (int i = 0; i < 3; i++) {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
            }
        }

        static void Activate(Button button) {
            button.Focus(NavigationMethod.Tab);
            button.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space });
            button.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.Space });
        }

        static void NavPresentation(UserControl view) {
            foreach (var button in view.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("navItem"))) {
                Assert.Equal(40, button.Bounds.Height);
                var label = button.GetVisualDescendants().OfType<TextBlock>().Single();
                var icon = button.GetVisualDescendants().OfType<DawIcon>().Single();
                Assert.Equal(13, label.FontSize);
                Assert.Equal(button.Foreground, label.Foreground);
                Assert.Equal(button.Foreground, icon.Fill);
                var center = label.TranslatePoint(new Point(0, label.Bounds.Height / 2), button)!.Value;
                Assert.Equal(button.Bounds.Height / 2, center.Y, 3);
            }
        }

        [AvaloniaTheory]
        [InlineData(true)]
        [InlineData(false)]
        public void SelectedPageTitlesFollowResourceChanges_WithoutRecreatingTheView(bool preferences) {
            UserControl view = preferences ? new PreferencesView() : new WelcomeView();
            var window = new Window { Width = 800, Height = 660, Content = view };
            string key = preferences ? "prefs.playback" : "welcome.new";
            string heading = preferences ? "PageHeading" : "PageTitle";
            try {
                window.Resources[key] = "First title";
                window.Show(); Settle(window);
                Activate(view.FindControl<Button>(preferences ? "NavPlayback" : "NavNew")!); Settle(window);
                var title = view.FindControl<TextBlock>(heading)!;
                Assert.Equal("First title", title.Text);
                window.Resources[key] = "Updated title"; Settle(window);
                Assert.Equal("Updated title", title.Text);
                window.Content = null;
                window.Resources[key] = "After remount";
                window.Content = view; Settle(window);
                Assert.Equal("After remount", title.Text);
                if (!preferences) {
                    window.Resources["welcome.new.description"] = "Updated subtitle"; Settle(window);
                    Assert.Equal("Updated subtitle", view.FindControl<TextBlock>("PageSubtitle")!.Text);
                }
            } finally { window.Close(); if (view is PreferencesView settings) settings.ReleaseSession(); }
        }

        [AvaloniaTheory]
        [InlineData(800, 660)]
        [InlineData(1440, 900)]
        public void Preferences_CategoriesRemainReadable_AndReflowWithoutRecreatingControls(double width, double height) {
            var view = new PreferencesView();
            var window = new Window { Width = width, Height = height, Content = view };
            try {
                window.Show(); Settle(window);
                NavPresentation(view);
                var nav = view.FindControl<Button>("NavPlayback")!;
                Activate(nav); Settle(window);
                Assert.Equal(ThemeManager.GetString("prefs.playback"), view.FindControl<TextBlock>("PageHeading")!.Text);
                var page = view.FindControl<AdaptiveColumns>("PagePlayback")!;
                Assert.True(page.IsVisible);
                var left = page.Children[0]; var right = page.Children[1];
                if (width == 800) {
                    Assert.Equal(left.Bounds.X, right.Bounds.X);
                    Assert.True(right.Bounds.Top >= left.Bounds.Bottom + page.Spacing - 0.01);
                } else {
                    Assert.Equal(left.Bounds.Y, right.Bounds.Y);
                    Assert.True(right.Bounds.Left >= left.Bounds.Right + page.Spacing - 0.01);
                }
                var chips = page.GetVisualDescendants().OfType<RadioButton>().Where(b => b.Classes.Contains("chip")).ToArray();
                Assert.True(chips.Length >= 6);
                foreach (var chip in chips) {
                    var label = chip.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Label");
                    var before = label.Bounds;
                    chip.IsChecked = true; Settle(window);
                    Assert.Equal(before, label.Bounds);
                    Assert.Equal(chip.Foreground, label.Foreground);
                    Assert.Equal(ColorPool.Current.Color(Md3Role.OnSecondaryContainer), Assert.IsAssignableFrom<ISolidColorBrush>(chip.Foreground).Color);
                    Assert.Equal(ColorPool.Current.Color(Md3Role.SecondaryContainer), Assert.IsAssignableFrom<ISolidColorBrush>(chip.Background).Color);
                    Assert.Single(chip.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_StateLayer").Transitions!);
                    Assert.True(chip.GetVisualDescendants().OfType<DawIcon>().Single().IsVisible);
                    var center = label.TranslatePoint(new Point(label.Bounds.Width / 2, label.Bounds.Height / 2), chip)!.Value;
                    Assert.Equal(chip.Bounds.Width / 2, center.X, 3);
                    Assert.Equal(chip.Bounds.Height / 2, center.Y, 3);
                }
                window.Width = width == 800 ? 1440 : 800; Settle(window);
                Assert.Same(left, page.Children[0]); Assert.Same(right, page.Children[1]);
                Assert.True(left.Bounds.Width >= page.MinimumColumnWidth);
                Assert.True(right.Bounds.Right <= page.Bounds.Width + 0.01);
                foreach (var name in new[] { "NavAudio", "NavLibrary", "NavAppearance", "NavEditor", "NavMidi", "NavGeneral", "NavAbout" }) {
                    Activate(view.FindControl<Button>(name)!); Settle(window);
                    Assert.Single(view.GetVisualDescendants().OfType<Control>()
                        .Where(c => c.Name is "PageAudio" or "PageLibrary" or "PagePlayback" or "PageAppearance" or "PageEditor" or "PageMidi" or "PageGeneral" or "PageAbout")
                        .Where(c => c.IsVisible));
                }
            } finally { window.Close(); view.ReleaseSession(); }
        }

        [AvaloniaTheory]
        [InlineData(800, 660)]
        [InlineData(1440, 900)]
        public void Welcome_QuickActionsAndSearchFit_AndNavigationUsesTheSamePresentation(double width, double height) {
            var view = new WelcomeView();
            var window = new Window { Width = width, Height = height, Content = view };
            try {
                window.Show(); Settle(window);
                NavPresentation(view);
                var search = view.FindControl<TextBox>("SearchBox")!;
                Assert.Equal(40, search.Bounds.Height);
                var actions = view.FindControl<StackPanel>("RecentActions")!;
                var newButton = view.FindControl<Button>("QuickNew")!;
                var openButton = view.FindControl<Button>("QuickOpen")!;
                Assert.True(newButton.Bounds.Width > 0 && openButton.Bounds.Width > 0);
                Assert.True(openButton.Bounds.Right <= actions.Bounds.Width + 0.01);
                var actionsBottom = actions.TranslatePoint(new Point(0, actions.Bounds.Height), view)!.Value.Y;
                var searchTop = search.TranslatePoint(default, view)!.Value.Y;
                Assert.True(actionsBottom <= searchTop);
                Activate(view.FindControl<Button>("NavNew")!); Settle(window);
                Assert.False(view.FindControl<Grid>("PageRecent")!.IsVisible);
                Assert.True(view.FindControl<StackPanel>("PageNew")!.IsVisible);
                Activate(view.FindControl<Button>("NavRecent")!); Settle(window);
                Assert.True(view.FindControl<Grid>("PageRecent")!.IsVisible);
                Assert.Equal(ThemeManager.GetString("welcome.recent"), view.FindControl<TextBlock>("PageTitle")!.Text);
            } finally { window.Close(); }
        }
    }
}

using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.App.Views;
using OpenUtau.Core.Theming;
using OpenUtau.Theming;
using Xunit;

namespace OpenUtau.Test.App {
    [Collection("Theme")]
    public class WorkspaceWindowChromeTests {
        internal static void Settle(Window window) {
            for (int i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        }
        [AvaloniaFact]
        public void OptInIsScoped_AndOnlyInsetsTheFrame() {
            var plain = new WindowEx();
            var custom = new WindowEx { UseWorkspaceChrome = true, Width = 1000, Height = 660, Content = new Border() };
            try {
                custom.Show(); Settle(custom);
                Assert.False(plain.ExtendClientAreaToDecorationsHint);
                Assert.Null(plain.WindowDecorationsTheme);
                Assert.True(custom.ExtendClientAreaToDecorationsHint);
                Assert.Equal(WindowDecorations.Full, custom.WindowDecorations);
                Assert.Equal(0, custom.ExtendClientAreaTitleBarHeightHint);
                Assert.NotNull(custom.WindowDecorationsTheme);
                Assert.Equal(new[] { WindowTransparencyLevel.None }, custom.TransparencyLevelHint);
                var body = Assert.IsType<Border>(custom.Content);
                Assert.InRange(body.TranslatePoint(default, custom)!.Value.Y, 0, 2);
            } finally { custom.Close(); plain.Close(); }
        }

        // The headless backend does not request managed window decorations.
        // Caption rendering, hit-testing and button behavior are checked by the
        // isolated Win32 native probe; these tests cover content and layout.
        [AvaloniaTheory]
        [InlineData(800)]
        [InlineData(1440)]
        public void PageSearchFields_StayClearOfCaptionButtons(double width) {
            var preferences = new PreferencesView();
            var welcome = new WelcomeView();
            var window = new WindowEx { UseWorkspaceChrome = true, Width = width, Height = 700, Content = preferences };
            try {
                window.Show(); Settle(window);
                Check(preferences.FindControl<TextBox>("SettingsSearch")!);
                window.Content = welcome; Settle(window);
                Check(welcome.FindControl<TextBox>("SearchBox")!);
                void Check(Control field) {
                    double captionX = window.ClientSize.Width - 128;
                    var pos = field.TranslatePoint(default, window)!.Value;
                    Assert.True(pos.X + field.Bounds.Width <= captionX, $"{width}: field ends at {pos.X + field.Bounds.Width}, caption starts {captionX}");
                    Assert.Equal(WindowDecorationsElementRole.User, WindowDecorationProperties.GetElementRole(field));
                }
            } finally { window.Close(); preferences.ReleaseSession(); }
        }

        [AvaloniaFact]
        public async Task WindowStateChanges_RetainContentAndReleaseFullscreenInset() {
            var body = new TextBox { Text = "keep editing", Margin = new Thickness(0, 56, 0, 0) };
            var window = new WindowEx { UseWorkspaceChrome = true, Width = 1000, Height = 660, Content = body };
            try {
                window.Show(); Settle(window);
                for (int i = 0; i < 5; i++) {
                    window.WindowState = WindowState.Minimized;
                    Assert.Equal(WindowState.Minimized, window.WindowState);
                    await Task.Run(() => { });
                    window.WindowState = WindowState.Normal; Settle(window);
                    Assert.Same(body, window.Content);
                }
                window.WindowState = WindowState.FullScreen; Settle(window);
                Assert.Equal(new Thickness(), window.Resources["Md3WorkspaceCaptionInset"]);
                window.WindowState = WindowState.Normal; Settle(window);
                Assert.Equal(new Thickness(0, 0, 128, 0), window.Resources["Md3WorkspaceCaptionInset"]);
                Assert.Equal("keep editing", body.Text);
            } finally { window.Close(); }
        }

        [AvaloniaFact]
        public void ProjectTitle_UsesFileNameAndUnsavedMarker() {
            Assert.Equal("* Ballad", MainWindowViewModel.FormatProjectTitle(@"C:\Songs\Ballad.ustxp", "other", false));
            Assert.Equal("Imported song", MainWindowViewModel.FormatProjectTitle(null, "Imported song", true));
            Assert.Equal("* " + ThemeManager.GetString("window.untitled"), MainWindowViewModel.FormatProjectTitle(null, "New Project", false));
        }

        [AvaloniaTheory]
        [InlineData(800)]
        [InlineData(1000)]
        [InlineData(1440)]
        public void HeaderGroups_ReflowAroundCaptionAreaWithoutReparenting(double width) {
            var header = new WorkspaceHeader { CaptionInset = 128 };
            foreach (double groupWidth in new double[] { 210, 470, 200, 310 }) header.Children.Add(new Border { Width = groupWidth, Height = 40 });
            var original = header.Children.ToArray();
            header.Measure(new Size(width, 300)); header.Arrange(new Rect(0, 0, width, header.DesiredSize.Height));
            foreach (var group in header.Children) {
                Assert.InRange(group.Bounds.Right, 0, width);
                if (group.Bounds.Top < 56) Assert.True(group.Bounds.Right <= width - 128);
            }
            for (int i = 0; i < 4; i++) for (int j = i + 1; j < 4; j++) Assert.False(header.Children[i].Bounds.Intersects(header.Children[j].Bounds));
            header.CaptionInset = 0; header.Measure(new Size(1800, 300)); header.Arrange(new Rect(0, 0, 1800, header.DesiredSize.Height));
            Assert.Equal(original, header.Children);
            Assert.Equal(56, header.Bounds.Height);
        }

        [AvaloniaTheory]
        [InlineData(800)]
        [InlineData(1440)]
        [InlineData(1800)]
        public void ActualWorkspaceHeader_WithLongProjectTitleKeepsButtonsClearOfCaptionArea(double width) {
            using var fixture = new WorkspaceShellLayoutTests.Fixture(width, 700, Avalonia.Styling.ThemeVariant.Dark);
            fixture.Window.Resources["Md3WorkspaceCaptionWidth"] = 128d;
            ((TextBlock)fixture.NamedControls["ScreenTitle"]).Text = new string('W', 60);
            Settle(fixture.Window);
            var caption = new Rect(width - 128, 0, 128, 56);
            var groups = new[] { "AppMenuButton", "TransportGroup", "ViewSwitcherGroup", "TopRightCluster" }
                .Select(n => fixture.InShell(fixture.NamedControls[n])).ToArray();
            foreach (var bounds in groups) {
                Assert.True(bounds.Right <= width);
                Assert.False(bounds.Intersects(caption), $"{width}: {bounds} overlaps caption area");
            }
            for (int i = 0; i < groups.Length; i++) for (int j = i + 1; j < groups.Length; j++) Assert.False(groups[i].Intersects(groups[j]));
        }
    }
}

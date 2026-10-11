#nullable enable
using System;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App;
using OpenUtau.App.Controls;
using OpenUtau.Core.Theming;
using OpenUtau.Core.Util;
using OpenUtau.Theming;
using Xunit;

namespace OpenUtau.Test.App;

[Collection("Theme")]
public class Md3DesignSystemTests {
    [Theory]
    [InlineData("#6750A4", 0xFF6750A4u)]
    [InlineData(" 006c4c ", 0xFF006C4Cu)]
    public void SeedInputIsOpaqueAndExact(string text, uint expected) {
        Assert.True(Md3Appearance.TryParseSeed(text, out uint seed));
        Assert.Equal(expected, seed);
    }

    [Theory]
    [InlineData("#123")]
    [InlineData("FF6750A4")]
    [InlineData("#GG0000")]
    public void InvalidSeedCannotChangeTheme(string text) => Assert.False(Md3Appearance.TryParseSeed(text, out _));

    [Theory]
    [InlineData(3800, 1)]
    [InlineData(1600, 1)]
    [InlineData(800, 1)]
    [InlineData(1400, 0.9)]
    [InlineData(700, 0.9)]
    [InlineData(300, 0.9)]
    public void StandardSpringHasFiniteEndpointsAndEffectsNeverOvershoot(double stiffness, double damping) {
        var easing = new Md3StandardSpringEasing { Stiffness = stiffness, Damping = damping };
        Assert.Equal(0, easing.Ease(0));
        Assert.Equal(1, easing.Ease(1));
        double previous = 0;
        foreach (int step in Enumerable.Range(0, 101)) {
            double value = easing.Ease(step / 100.0);
            Assert.True(double.IsFinite(value));
            Assert.InRange(value, 0, damping == 1 ? 1 : 1.01);
            if (damping == 1) Assert.True(value >= previous);
            previous = value;
        }
        Assert.InRange(Md3StandardSpringEasing.SettlingDuration(stiffness, damping).TotalMilliseconds, 100, 600);
    }

    [AvaloniaFact]
    public void ThemeListenersSurviveRepeatedRecolorAndObserveInstalledResources() {
        var old = ColorPool.Current;
        int notifications = 0;
        EventHandler listener = (_, _) => {
            notifications++;
            Assert.True(Application.Current!.TryFindResource("md3.primary", out var resource));
            Assert.Equal(ColorPool.Current.Color(Md3Role.Primary), Assert.IsAssignableFrom<ISolidColorBrush>(resource).Color);
        };
        ColorPool.Changed += listener;
        try {
            ColorPool.Initialize(0xFF6750A4, Md3SchemeVariant.TonalSpot, false);
            ColorPool.Initialize(0xFF006C4C, Md3SchemeVariant.Vibrant, true);
            ColorPool.SetDark(false);
            Assert.Equal(3, notifications);
        } finally {
            ColorPool.Changed -= listener;
            ColorPool.Initialize(old.Seed, old.Scheme, old.IsDark, old.ContrastLevel);
        }
    }

    [AvaloniaFact]
    public void ExistingWindowsAndSwitchTransitionsObserveLiveReducedMotion() {
        bool old = Preferences.Default.ReduceMotion;
        WindowEx? window = null;
        try {
            Preferences.Default.ReduceMotion = false;
            Md3Motion.ApplyPreference();
            var toggle = new ToggleSwitch();
            var progress = new ProgressBar { IsIndeterminate = true, Width = 240 };
            window = new WindowEx { Width = 320, Height = 180, Content = new StackPanel { Children = { toggle, progress } } };
            window.Show();
            toggle.ApplyTemplate(); progress.ApplyTemplate(); Dispatcher.UIThread.RunJobs();
            Preferences.Default.ReduceMotion = true;
            Md3Motion.ApplyPreference(); Dispatcher.UIThread.RunJobs();
            Assert.Contains("no-motion", window.Classes);
            Assert.True(Md3Motion.GetReduced(progress));
            Assert.Contains(":reduced-motion", progress.Classes);
            Assert.True(progress.IsIndeterminate); // Reduce visual motion, never change busy semantics.
            Assert.Equal(TimeSpan.Zero, Md3Motion.EffectsFast);
            foreach (var node in toggle.GetVisualDescendants().OfType<Control>()) {
                if (node.Transitions != null)
                    foreach (var transition in node.Transitions) Assert.Equal(TimeSpan.Zero, Assert.IsAssignableFrom<TransitionBase>(transition).Duration);
            }
            Preferences.Default.ReduceMotion = false;
            Md3Motion.ApplyPreference(); Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("no-motion", window.Classes);
            Assert.False(Md3Motion.GetReduced(progress));
            Assert.True(Md3Motion.EffectsFast > TimeSpan.Zero);
        } finally {
            window?.Close();
            Preferences.Default.ReduceMotion = old;
            Md3Motion.ApplyPreference();
        }
    }

    [AvaloniaFact]
    public void SemanticButtonsKeepRichContentAndPairedRolesInBothThemes() {
        var old = ColorPool.Current;
        try {
            foreach (bool dark in new[] { false, true }) {
                ColorPool.Initialize(0xFF006C4C, Md3SchemeVariant.TonalSpot, dark);
                var label = new TextBlock { Text = "Export" };
                var content = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { new Border { Width = 16, Height = 16 }, label } };
                var primary = new Button { Content = content, Classes = { "primary" } };
                var tonal = new Button { Content = "Preview", Classes = { "tonal" } };
                var panel = new StackPanel { Children = { primary, tonal } };
                var window = new WindowEx { Width = 360, Height = 180, Content = panel };
                window.Show(); primary.ApplyTemplate(); tonal.ApplyTemplate(); Dispatcher.UIThread.RunJobs();
                try {
                    Assert.Same(content, primary.Content);
                    Assert.Equal(ColorPool.Current.Color(Md3Role.Primary), Assert.IsAssignableFrom<ISolidColorBrush>(primary.Background).Color);
                    Assert.Equal(ColorPool.Current.Color(Md3Role.OnPrimary), Assert.IsAssignableFrom<ISolidColorBrush>(primary.Foreground).Color);
                    Assert.Equal(ColorPool.Current.Color(Md3Role.OnPrimary), Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color);
                    Assert.Equal(ColorPool.Current.Color(Md3Role.SecondaryContainer), Assert.IsAssignableFrom<ISolidColorBrush>(tonal.Background).Color);
                    Assert.Equal(ColorPool.Current.Color(Md3Role.OnSecondaryContainer), Assert.IsAssignableFrom<ISolidColorBrush>(tonal.Foreground).Color);
                    Assert.NotNull(primary.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "PART_StateLayer"));
                    Assert.Equal(40, primary.MinHeight);
                } finally { window.Close(); }
            }
        } finally { ColorPool.Initialize(old.Seed, old.Scheme, old.IsDark, old.ContrastLevel); }
    }
}

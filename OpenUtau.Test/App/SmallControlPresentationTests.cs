using System;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using Xunit;

namespace OpenUtau.Test.App {
    [Collection("Theme")]
    public class SmallControlPresentationTests {
        static IDisposable RealGeometry() => (IDisposable)typeof(DawPresentationTests)
            .GetMethod("RealGeometry", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
        static void Settle(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        static void Center(DawIcon icon, Control host) {
            var point = icon.TranslatePoint(icon.RenderedInkBounds.Center, host)!.Value;
            Assert.Equal(host.Bounds.Width / 2, point.X, 3);
            Assert.Equal(host.Bounds.Height / 2, point.Y, 3);
        }

        [AvaloniaTheory]
        [InlineData(false)]
        [InlineData(true)]
        public void RevealLabelAndIcon_AreSeparateAndCentered_InBothOrientations(bool horizontal) {
            using var geometry = RealGeometry();
            var tab = new PanelRevealTab {
                AnchorBottom = horizontal, PanelLabel = "Tools", GlyphData = Geometry.Parse("M4,1 L12,1 L12,7 L4,7 Z"),
            };
            var window = new Window { Width = 150, Height = 100, Content = new Canvas { Children = { tab } } };
            try {
                window.Show(); Settle(window);
                var button = tab.FindControl<Button>("TabButton")!;
                var icon = tab.FindControl<DawIcon>("RevealGlyph")!;
                var label = tab.FindControl<TextBlock>("RevealLabel")!;
                Center(icon, button);
                button.Focus(NavigationMethod.Tab); Settle(window);
                Assert.True(label.IsVisible);
                var topLeft = label.TranslatePoint(default, button)!.Value;
                var ink = icon.TranslatePoint(icon.RenderedInkBounds.BottomRight, button)!.Value;
                Assert.True(horizontal ? topLeft.Y >= ink.Y : topLeft.X >= ink.X);
                Assert.True(topLeft.X + label.Bounds.Width <= button.Bounds.Width + 0.001);
                Assert.True(topLeft.Y + label.Bounds.Height <= button.Bounds.Height + 0.001);
            } finally { window.Close(); }
        }

        [AvaloniaFact]
        public void ExpressionPicker_IsKeyboardFocusable_AndItsChevronHasACenteredHitCell() {
            using var geometry = RealGeometry();
            var selector = new ExpSelector();
            var window = new Window { Width = 100, Height = 60, Content = selector };
            try {
                window.Show(); Settle(window);
                var picker = selector.FindControl<ComboBox>("DescriptorPicker")!;
                Assert.True(picker.Focusable);
                picker.Focus(NavigationMethod.Tab); Settle(window);
                Assert.True(picker.IsFocused);
                var icon = picker.GetVisualDescendants().OfType<DawIcon>().Single();
                Center(icon, picker);
                Assert.Equal(1, picker.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PickerFocus").Opacity);
            } finally { ((ExpSelectorViewModel)selector.DataContext!).Dispose(); window.Close(); }
        }

        [AvaloniaFact]
        public void ToolRail_UsesNativeInkBoundsAndPreservesSelectionAndKeyboardFocus() {
            using var geometry = RealGeometry();
            var item = new ListBoxItem { Classes = { "railTool", "cursorTool" }, IsSelected = true };
            var window = new Window { Width = 80, Height = 80, Content = new Canvas { Children = { item } } };
            window.Styles.Add(new StyleInclude(new Uri("avares://OpenUtau/")) {
                Source = new Uri("avares://OpenUtau/Controls/PianoRollStyles.axaml"),
            });
            try {
                window.Show(); Settle(window);
                var icon = item.GetVisualDescendants().OfType<DawIcon>().Single();
                Assert.NotNull(icon.Data);
                Assert.NotNull(icon.Fill);
                Center(icon, item);
                Assert.Equal(36, item.Bounds.Width);
                item.Focus(NavigationMethod.Tab); Settle(window);
                var border = item.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_LayoutRoot");
                Assert.Equal(new Thickness(1), border.BorderThickness);
                Assert.True(item.IsSelected);
            } finally { window.Close(); }
        }
    }
}

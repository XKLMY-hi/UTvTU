using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.App {
    [Collection("Theme")]
    public class DawPresentationTests {
        public class GeometryOnlyProvider : DispatchProxy {
            public IPlatformRenderInterface Headless = null!;
            public IPlatformRenderInterface Skia = null!;

            protected override object? Invoke(MethodInfo? method, object?[]? args) {
                // Keep the headless font/bitmap backend intact. Only vector geometry
                // needs real ink bounds; replacing the whole backend breaks typefaces.
                bool geometry = method!.Name.StartsWith("Create", StringComparison.Ordinal) &&
                    (method.Name.EndsWith("Geometry", StringComparison.Ordinal) || method.Name == "CreateGeometryGroup");
                return method.Invoke(geometry ? Skia : Headless, args);
            }
        }
        // The normal headless geometry provider reports empty path bounds. These checks
        // scope the real Windows Skia geometry provider without changing the test app,
        // capturing product screenshots or pretending stub path rectangles are vector ink.
        static IDisposable RealGeometry() {
            // Avalonia 12 hides its mutable locator and concrete geometry provider from
            // reference assemblies. This test-only bridge scopes the installed runtime's
            // provider; no production code depends on these private registration APIs.
            var locator = typeof(AvaloniaLocator);
            var current = locator.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
            var headless = (IPlatformRenderInterface)current.GetType().GetMethod("GetService")!
                .Invoke(current, new object[] { typeof(IPlatformRenderInterface) })!;
            var scope = (IDisposable)locator.GetMethod("EnterScope", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!;
            try {
                var providerType = typeof(SkiaOptions).Assembly.GetType("Avalonia.Skia.PlatformRenderInterface", true)!;
                var provider = DispatchProxy.Create<IPlatformRenderInterface, GeometryOnlyProvider>();
                var bridge = (GeometryOnlyProvider)provider;
                bridge.Headless = headless;
                bridge.Skia = (IPlatformRenderInterface)Activator.CreateInstance(providerType, new object?[] { null, null })!;
                var mutable = locator.GetProperty("CurrentMutable", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
                var registration = locator.GetMethod("Bind")!.MakeGenericMethod(typeof(IPlatformRenderInterface)).Invoke(mutable, null)!;
                registration.GetType().GetMethod("ToConstant")!.MakeGenericMethod(typeof(IPlatformRenderInterface))
                    .Invoke(registration, new object[] { provider });
                return scope;
            } catch { scope.Dispose(); throw; }
        }

        static Geometry Asset(string key) {
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var root = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "Icons.axaml"));
            return Geometry.Parse(root.Descendants().Single(e => (string?)e.Attribute(x + "Key") == key).Value);
        }

        static void Settle(Window window) {
            for (int i = 0; i < 3; i++) {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
            }
        }

        static void Center(Control child, Control parent, Point? inkCenter = null) {
            var point = child.TranslatePoint(inkCenter ?? new Point(child.Bounds.Width / 2, child.Bounds.Height / 2), parent);
            Assert.NotNull(point);
            var detail = $"{parent.Name}/{parent.GetType().Name}: cell={parent.Bounds}, child={child.Bounds}, ink={inkCenter}, actual={point}";
            Assert.True(Math.Abs(parent.Bounds.Width / 2 - point!.Value.X) < 0.001, detail);
            Assert.True(Math.Abs(parent.Bounds.Height / 2 - point.Value.Y) < 0.001, detail);
        }

        static void Contained(DawIcon icon) {
            var ink = icon.RenderedInkBounds;
            Assert.True(ink.Width > 0 && ink.Height > 0);
            Assert.True(ink.Left >= -0.01 && ink.Top >= -0.01 &&
                ink.Right <= icon.Bounds.Width + 0.01 && ink.Bottom <= icon.Bounds.Height + 0.01,
                $"Vector ink escapes cell: {ink} / {icon.Bounds}");
        }

        [AvaloniaTheory]
        [InlineData("icon-play")]
        [InlineData("icon-pause")]
        [InlineData("icon-metronome")]
        [InlineData("icon-settings")]
        [InlineData("icon-save")]
        public void SourceVectorInk_IsCentered_WithoutMutatingSharedAsset(string key) {
            using var geometry = RealGeometry();
            var data = Asset(key);
            var originalBounds = data.Bounds;
            var originalTransform = data.Transform;
            var icon = new DawIcon { Data = data, Fill = Brushes.White, Width = 16, Height = 16 };
            icon.Measure(new Size(16, 16));
            icon.Arrange(new Rect(0, 0, 16, 16));
            Contained(icon);
            Assert.Equal(8, icon.RenderedInkBounds.Center.X, 3);
            Assert.Equal(8, icon.RenderedInkBounds.Center.Y, 3);
            Assert.Equal(originalBounds, data.Bounds);
            Assert.Same(originalTransform, data.Transform);
            Assert.False(icon.IsHitTestVisible);
        }

        [AvaloniaFact]
        public void StrokedAndNegativeOriginVectors_FitWithConsistentDipStroke() {
            using var geometry = RealGeometry();
            foreach (var path in new[] { "M-6.5 1 L-2 2.5 L0.5 0 L6.5 -1", "M100 30 V80" }) {
                var icon = new DawIcon { Data = Geometry.Parse(path), Stroke = Brushes.White, Width = 14, Height = 14 };
                icon.Measure(new Size(14, 14));
                icon.Arrange(new Rect(0, 0, 14, 14));
                Contained(icon);
                Assert.Equal(7, icon.RenderedInkBounds.Center.X, 3);
                Assert.Equal(7, icon.RenderedInkBounds.Center.Y, 3);
                Assert.Equal(1.5, icon.StrokeThickness);
            }
        }

        [AvaloniaTheory]
        [InlineData(800)]
        [InlineData(1440)]
        public void ActualShell_IconInkAndTabLabelsAreCentered(double width) {
            using var geometry = RealGeometry();
            using var f = new WorkspaceShellLayoutTests.Fixture(width, 900, ThemeVariant.Dark);
            var icons = f.Shell.GetVisualDescendants().OfType<DawIcon>().ToArray();
            Assert.True(icons.Length >= 12);
            foreach (var icon in icons) {
                var button = icon.GetVisualAncestors().OfType<Button>().FirstOrDefault();
                if (button == null) continue;
                Contained(icon);
                Center(icon, button, icon.RenderedInkBounds.Center);
            }
            foreach (var name in new[] { "ViewTabWorkspace", "ViewTabPianoRoll", "ViewTabMixer" }) {
                var button = (Button)f.NamedControls[name];
                var label = button.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Label");
                Center(label, button);
                Assert.Equal(button.FontSize, label.FontSize);
                Assert.Equal(button.Foreground, label.Foreground);
                var before = label.Bounds;
                button.Classes.Add("selected");
                button.Focus(NavigationMethod.Tab);
                Settle(f.Window);
                Assert.Equal(before, label.Bounds);
                Assert.Equal(button.Foreground, label.Foreground);
            }
        }

        [AvaloniaFact]
        public void MixerAndTrackHeader_MuteLabelsShareTypographyAndStateColors() {
            var (window, _, header, items) = TrackHeaderLayoutTests.Setup(105, 240);
            var strip = new MixerTrackStrip(items[0]);
            try {
                // Attach a real strip to the same window so both resolve the same theme resources.
                var content = window.Content;
                window.Content = null;
                window.Height = 900;
                window.Content = new StackPanel { Children = { (Control)content!, strip } };
                Settle(window);
                var mute = strip.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "MuteBtn");
                foreach (var button in new[] { header.MuteButton, mute }) {
                    var label = button.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Label");
                    Center(label, button);
                    Assert.Equal(10, label.FontSize);
                    Assert.Equal(FontWeight.Bold, label.FontWeight);
                    Assert.Equal(button.Foreground, label.Foreground);
                }
                header.MuteButton.IsChecked = true;
                mute.Classes.Add("muteOn");
                Settle(window);
                Assert.Equal(mute.Background, header.MuteButton.Background);
                Assert.Equal(mute.Foreground, header.MuteButton.Foreground);
            } finally { strip.DisposeSubscriptions(); items.Clear(); window.Close(); }
        }

        [AvaloniaFact]
        public void PianoTools_UseCenteredInkAndOwnedText_InBothToggleStates() {
            using var geometry = RealGeometry();
            using var dispatcher = DocManagerTestSetup.EnterScopedDispatcher(nullChannel: true, installScheduler: false);
            DocManager.Inst.SearchAllLegacyPlugins();
            var vm = new PianoRollViewModel();
            using var roll = new PianoRoll(vm);
            var window = new Window { Width = 1000, Height = 800, Content = roll };
            try {
                window.Show();
                Settle(window);
                var tools = roll.GetVisualDescendants().OfType<ToggleButton>().Where(b => b.Classes.Contains("dawTool")).ToArray();
                Assert.Equal(12, tools.Length);
                foreach (var button in tools) {
                    Assert.Equal(new Size(20, 20), button.Bounds.Size);
                    foreach (var icon in button.GetVisualDescendants().OfType<DawIcon>()) {
                        Contained(icon);
                        Center(icon, button, icon.RenderedInkBounds.Center);
                    }
                    button.IsChecked = !button.IsChecked;
                    Settle(window);
                    foreach (var icon in button.GetVisualDescendants().OfType<DawIcon>()) {
                        Assert.Equal(button.Foreground, icon.Stroke ?? icon.Fill);
                        Center(icon, button, icon.RenderedInkBounds.Center);
                    }
                }
                foreach (var button in roll.GetVisualDescendants().OfType<Button>().Where(b => b.Content is string)) {
                    var label = button.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Name == "PART_Label");
                    if (label != null) Center(label, button);
                }
            } finally { window.Close(); vm.Dispose(); }
        }
    }
}

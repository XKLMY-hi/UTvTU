using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App;
using OpenUtau.App.Controls;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// Measures the actual shell XML hierarchy with its compiled local styles and real Avalonia
    /// controls. MainWindow itself starts update checks and timers, so this fixture excludes the
    /// document workspace, event handlers, flyouts and VM bindings. Readouts use representative
    /// values; this verifies shell geometry, not document/navigation behavior or pixel rendering.
    /// </summary>
    [Collection("Theme")]
    public class WorkspaceShellLayoutTests {
        static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        static XElement Source() => XDocument.Load(System.IO.Path.Combine(
            AppContext.BaseDirectory, "Views", "MainWindow.axaml")).Root!;
        static XElement Named(XElement root, string name) => root.DescendantsAndSelf()
            .Single(e => (string?)e.Attribute(Xaml + "Name") == name);

        internal sealed class Fixture : IDisposable {
            public readonly Window Window;
            public readonly Grid Shell;
            public readonly Dictionary<string, Control> NamedControls = new();

            public Fixture(double width, double height, ThemeVariant theme) {
                var source = Source();
                var main = Named(source, "MainGrid");
                Shell = new Grid { RowDefinitions = new RowDefinitions((string)main.Attribute("RowDefinitions")!) };
                foreach (var name in new[] { "ShellHeaderRow", "ShellStatusRow" }) {
                    Shell.Children.Add(Build(Named(source, name)));
                }
                // The workspace consumes the remaining row just as the product does.
                var body = new Border();
                Grid.SetRow(body, 1);
                Shell.Children.Add(body);
                Window = new Window { Width = width, Height = height, Content = Shell, RequestedThemeVariant = theme };
                Window.Styles.Add(new StyleInclude(new Uri("avares://OpenUtau/")) {
                    Source = new Uri("avares://OpenUtau/Styles/WorkspaceShell.axaml"),
                });
                Window.Show();
                Dispatcher.UIThread.RunJobs();
                Window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
            }

            public Control Build(XElement e) {
                Control control = e.Name.LocalName switch {
                    "Grid" => new Grid(),
                    "WorkspaceHeader" => new WorkspaceHeader(),
                    "StackPanel" => new StackPanel(),
                    "Border" => new Border(),
                    "Button" => new Button(),
                    "ToggleButton" => new Avalonia.Controls.Primitives.ToggleButton(),
                    "TextBlock" => new TextBlock(),
                    "Menu" => new Menu(),
                    "MenuItem" => new MenuItem(),
                    "Viewbox" => new Viewbox(),
                    "Canvas" => new Canvas(),
                    "Path" => new Avalonia.Controls.Shapes.Path(),
                    "DawIcon" => new DawIcon(),
                    "ViewScaler" => new ViewScaler(),
                    "ProgressBar" => new ProgressBar(),
                    _ => throw new InvalidOperationException($"Unhandled shell element {e.Name}"),
                };
                foreach (var attr in e.Attributes()) {
                    string key = attr.Name.LocalName;
                    string value = attr.Value;
                    if (key == "Name") {
                        control.Name = value;
                        NamedControls.Add(value, control);
                    } else if (key == "Classes") {
                        foreach (var cls in value.Split(' ', StringSplitOptions.RemoveEmptyEntries)) control.Classes.Add(cls);
                    } else if (value.StartsWith("{DynamicResource Md3", StringComparison.Ordinal)) {
                        string resourceKey = value.Substring("{DynamicResource ".Length).TrimEnd('}');
                        var property = AvaloniaPropertyRegistry.Instance.FindRegistered(control.GetType(), key);
                        if (property != null) control[!property] = new DynamicResourceExtension(resourceKey);
                    } else if (!value.StartsWith("{", StringComparison.Ordinal)) {
                        ApplyLiteral(control, key, value);
                    }
                }
                if (control is DawIcon icon && (string?)e.Attribute("Data") is { } geometryText) {
                    // Reparse the actual source under the active geometry provider. Application
                    // assets may have been created before a test scopes the real Skia provider.
                    if (geometryText.StartsWith("{StaticResource ", StringComparison.Ordinal)) {
                        var assets = XDocument.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Icons.axaml"));
                        var key = geometryText.Substring(16).TrimEnd('}');
                        geometryText = assets.Descendants().Single(node => (string?)node.Attribute(Xaml + "Key") == key).Value;
                    }
                    icon.Data = Geometry.Parse(geometryText);
                }
                if (control is TextBlock text) {
                    var textAttribute = (string?)e.Attribute("Text");
                    text.Text = ResolveText(textAttribute);
                    if (e.Elements().Any(c => c.Name.LocalName == "TextBlock.Text")) {
                        text.Text = e.ToString().Contains("BeatPerBar", StringComparison.Ordinal) ? "12 / 16" : "999.00";
                    }
                }
                if (control is Button button && e.Attribute("Content") is { } contentAttr) button.Content = ResolveText(contentAttr.Value);
                if (control is MenuItem item) item.Header = ResolveText((string?)e.Attribute("Header"));
                // MenuItem instances from VM ItemsSource must never be duplicated into another menu.
                // Only top-level headers participate in closed-menu shell measurement.
                if (control is MenuItem || control is ViewScaler) return control;
                var children = e.Elements().Where(c => !c.Name.LocalName.Contains('.')).Select(Build).ToList();
                if (control is Panel panel) foreach (var child in children) panel.Children.Add(child);
                else if (control is Border border) border.Child = children.SingleOrDefault();
                else if (control is Viewbox viewbox) viewbox.Child = children.SingleOrDefault();
                else if (control is ContentControl content) content.Content = children.SingleOrDefault() ?? content.Content;
                else if (control is ItemsControl items) foreach (var child in children) items.Items.Add(child);
                return control;
            }

            static string ResolveText(string? value) {
                if (value == null) return "";
                if (value.StartsWith("{DynamicResource ", StringComparison.Ordinal)) {
                    var key = value.Substring(17).TrimEnd('}');
                    return ThemeManager.GetString(key);
                }
                if (value.Contains("PlayPosTime", StringComparison.Ordinal)) return "99:59.999";
                if (value.Contains("ProgressText", StringComparison.Ordinal)) return "Rendering vocal track 12 of 24";
                if (value.Contains("AppVersion", StringComparison.Ordinal)) return "UTvTU 0.2.0";
                return value.StartsWith("{", StringComparison.Ordinal) ? "" : value;
            }

            static void ApplyLiteral(Control c, string key, string value) {
                double Number() => double.Parse(value, CultureInfo.InvariantCulture);
                switch (key) {
                    case "Width": c.Width = Number(); break;
                    case "Height": c.Height = Number(); break;
                    case "MinWidth": c.MinWidth = Number(); break;
                    case "MinHeight": c.MinHeight = Number(); break;
                    case "MaxWidth": c.MaxWidth = Number(); break;
                    case "Margin": c.Margin = Thickness.Parse(value); break;
                    case "Padding" when c is Border b: b.Padding = Thickness.Parse(value); break;
                    case "Padding" when c is Avalonia.Controls.Primitives.TemplatedControl t: t.Padding = Thickness.Parse(value); break;
                    case "BorderThickness" when c is Border b: b.BorderThickness = Thickness.Parse(value); break;
                    case "CornerRadius" when c is Border b: b.CornerRadius = CornerRadius.Parse(value); break;
                    case "FontSize" when c is TextBlock t: t.FontSize = Number(); break;
                    case "FontFamily" when c is TextBlock t: t.FontFamily = new FontFamily(value); break;
                    case "FontWeight" when c is TextBlock t: t.FontWeight = value == "SemiBold" ? FontWeight.SemiBold : FontWeight.Normal; break;
                    case "RowDefinitions" when c is Grid g: g.RowDefinitions = new RowDefinitions(value); break;
                    case "ColumnDefinitions" when c is Grid g: g.ColumnDefinitions = new ColumnDefinitions(value); break;
                    case "Grid.Row": Grid.SetRow(c, (int)Number()); break;
                    case "Grid.Column": Grid.SetColumn(c, (int)Number()); break;
                    case "Canvas.Left": Canvas.SetLeft(c, Number()); break;
                    case "Canvas.Top": Canvas.SetTop(c, Number()); break;
                    case "Spacing" when c is StackPanel s: s.Spacing = Number(); break;
                    case "Orientation" when c is StackPanel s: s.Orientation = Enum.Parse<Orientation>(value); break;
                    case "HorizontalAlignment": c.HorizontalAlignment = Enum.Parse<HorizontalAlignment>(value); break;
                    case "VerticalAlignment": c.VerticalAlignment = Enum.Parse<VerticalAlignment>(value); break;
                    case "ClipToBounds": c.ClipToBounds = bool.Parse(value); break;
                    case "TextTrimming" when c is TextBlock t: t.TextTrimming = TextTrimming.CharacterEllipsis; break;
                }
            }

            public Rect InShell(Control control) {
                var origin = control.TranslatePoint(default, Shell);
                Assert.True(origin.HasValue, $"{control.Name} must be attached to shell");
                return new Rect(origin!.Value, control.Bounds.Size);
            }
            public void Dispose() => Window.Close();
        }

        [AvaloniaTheory]
        [InlineData(800, 600)]
        [InlineData(960, 600)]
        [InlineData(1200, 800)]
        [InlineData(1440, 900)]
        [InlineData(1800, 900)]
        public void ActualShellRows_ArrangeWithoutControlOverlap(double width, double height) {
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark }) {
                using var f = new Fixture(width, height, theme);
                var header = f.InShell(f.NamedControls["ShellHeaderRow"]);
                var status = f.InShell(f.NamedControls["ShellStatusRow"]);
                Assert.True(header.Height is 57 or 101, $"Unexpected header height: {header}"); // Includes separator.
                if (width == 800) Assert.Equal(101, header.Height);
                if (width == 1800) Assert.Equal(57, header.Height);
                Assert.Equal(32, status.Height, 3);
                Assert.Equal(height, status.Bottom, 3);
                var groups = new[] { "AppMenuButton", "TransportGroup", "ViewSwitcherGroup", "TopRightCluster" }
                    .Select(n => f.InShell(f.NamedControls[n])).ToArray();
                for (int i = 0; i < groups.Length; i++) {
                    for (int j = i + 1; j < groups.Length; j++) {
                        Assert.False(groups[i].Intersects(groups[j]), $"Header groups overlap: {groups[i]} / {groups[j]}");
                    }
                }
                foreach (var button in f.Shell.GetVisualDescendants().OfType<Button>().Where(b =>
                    b.Classes.Any(cls => cls is "topBtn" or "tpBtn" or "tpPlay" or "viewTab" or "brandBtn"))) {
                    var bounds = f.InShell(button);
                    Assert.InRange(bounds.Left, 0, width);
                    Assert.InRange(bounds.Right, 0, width);
                    var row = button.GetVisualAncestors().OfType<Border>().First(b => b.Name?.StartsWith("Shell", StringComparison.Ordinal) == true);
                    var rowBounds = f.InShell(row);
                    Assert.True(bounds.Top >= rowBounds.Top - 0.5 && bounds.Bottom <= rowBounds.Bottom + 0.5,
                        $"{button.Name}/{string.Join(' ', button.Classes)} overflows its row: {bounds} / {rowBounds}");
                    Assert.True(button.Focusable);
                    Assert.Equal(button.Classes.Contains("viewTab") ? 30 : 36, bounds.Height, 3);
                }
            }
        }

        [AvaloniaFact]
        public void ResizingHeader_KeepsGroupIdentity_AndUsesOriginalShapes() {
            using var f = new Fixture(1800, 900, ThemeVariant.Dark);
            var header = f.NamedControls["WorkspaceHeader"];
            var transport = f.NamedControls["TransportGroup"];
            var views = f.NamedControls["ViewSwitcherGroup"];
            Assert.Equal(56, header.Bounds.Height);
            f.Window.Width = 800;
            Dispatcher.UIThread.RunJobs();
            f.Window.UpdateLayout();
            Assert.Equal(100, header.Bounds.Height);
            Assert.True(f.InShell(views).Top >= f.InShell(transport).Bottom);
            f.Window.Width = 1800;
            Dispatcher.UIThread.RunJobs();
            f.Window.UpdateLayout();
            Assert.Equal(56, header.Bounds.Height);
            Assert.True(f.InShell(views).Top < f.InShell(transport).Bottom);
            var capsule = (Border)f.NamedControls["ViewSwitcher"];
            Assert.Equal(new CornerRadius(999), capsule.CornerRadius);
            Assert.Equal(36, capsule.Bounds.Height);
            Assert.Equal(new Thickness(3), capsule.Padding);
            var tab = (Button)f.NamedControls["ViewTabWorkspace"];
            tab.Classes.Add("selected");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new CornerRadius(999), tab.CornerRadius);
            Assert.Equal(f.Window.FindResource("md3.secondary-container"), tab.Background);
            Assert.Equal(f.Window.FindResource("md3.on-secondary-container"), tab.Foreground);
            var text = tab.GetVisualDescendants().OfType<TextBlock>().Single();
            Assert.Equal(11, text.FontSize);
            Assert.Equal(tab.Foreground, text.Foreground);
            Assert.Equal(new CornerRadius(8), ((Border)f.NamedControls["TimeReadout"]).CornerRadius);
            Assert.Equal(new CornerRadius(999), ((Border)f.NamedControls["TransportPill"]).CornerRadius);
        }

        [AvaloniaFact]
        public void KeyboardFocus_DoesNotExpandButtons_AndDisabledPianoHintHasEnabledParent() {
            using var f = new Fixture(800, 600, ThemeVariant.Dark);
            var save = (Button)f.NamedControls["ShellSaveButton"];
            var before = save.Bounds;
            Assert.True(save.Focus(NavigationMethod.Tab));
            Dispatcher.UIThread.RunJobs();
            f.Window.UpdateLayout();
            Assert.True(save.IsFocused);
            Assert.Equal(before, save.Bounds);
            var pianoXml = Named(Source(), "ViewTabPianoRoll");
            Assert.Equal("{Binding PianoRollNavigation.CanOpen}", (string?)pianoXml.Attribute("IsEnabled"));
            Assert.Equal("{Binding PianoRollNavigation.Hint}", (string?)pianoXml.Parent!.Attribute("ToolTip.Tip"));
            Assert.Equal("Border", pianoXml.Parent.Name.LocalName);
        }

        [AvaloniaTheory]
        [InlineData(180)]
        [InlineData(220)]
        [InlineData(272)]
        public void ActualLibraryTabs_ShareAvailablePanelWidth(double width) {
            using var f = new Fixture(800, 600, ThemeVariant.Dark);
            var tabs = (Grid)f.Build(Named(Source(), "LibraryTabs"));
            var host = new Border { Width = width, Height = 36, Child = tabs };
            f.Shell.Children.Add(host);
            Grid.SetRow(host, 1);
            f.Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            f.Window.UpdateLayout();
            var buttons = tabs.Children.OfType<Button>().ToArray();
            Assert.Equal(4, buttons.Length);
            double next = 0;
            foreach (var button in buttons) {
                Assert.Equal((width - 16) / 4, button.Bounds.Width, 2);
                Assert.Equal(28, button.Bounds.Height, 3);
                Assert.True(button.Bounds.Left >= next - 0.5);
                Assert.True(button.Bounds.Right <= tabs.Bounds.Width + 0.5);
                next = button.Bounds.Right;
            }
        }
    }
}

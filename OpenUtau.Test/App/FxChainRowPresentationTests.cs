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
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using Xunit;

namespace OpenUtau.Test.App {
    // One small row: no chain attachment, document commands, VST registry or plugins.
    [Collection("Theme")]
    public class FxChainRowPresentationTests {
        static FxChainRowViewModel Model(FxChainViewModel owner) => new(owner, null, 0) {
            Order = 1, Name = "Demo FX", BadgeText = "VST3", Detail = "ACME",
            CanReorder = true, IsPowered = true,
        };

        static Window Mount(Control control) {
            var window = new Window { Width = 340, Height = 100, Content = control };
            window.Show();
            Settle(window);
            return window;
        }

        static void Settle(Window window) {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }

        static T Part<T>(Visual root, string name) where T : Visual =>
            root.GetVisualDescendants().OfType<T>().Single(v => ((INamed)v).Name == name);

        static void Center(Control child, Control parent, Point center) {
            var actual = child.TranslatePoint(center, parent)!.Value;
            Assert.Equal(parent.Bounds.Width / 2, actual.X, 3);
            Assert.Equal(parent.Bounds.Height / 2, actual.Y, 3);
        }

        static IDisposable RealGeometry() => (IDisposable)typeof(DawPresentationTests)
            .GetMethod("RealGeometry", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;

        static Geometry Asset(string key) {
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "Icons.axaml"));
            return Geometry.Parse(document.Descendants().Single(e => (string?)e.Attribute(x + "Key") == key).Value);
        }

        [AvaloniaFact]
        public void NativeIconInk_IsCenteredWithinTheOriginalCompactHitTargets() {
            // Reuse the scoped geometry-only provider; font/bitmap layout stays headless.
            using var geometry = RealGeometry();
            using var owner = new FxChainViewModel();
            owner.Unsubscribe();
            var row = new FxChainRow { DataContext = Model(owner) };
            // App resources may already hold empty headless geometry instances. Parse the
            // same authoritative paths freshly under the real geometry provider for this probe.
            row.Grip.Data = Asset("icon-list");
            row.RemoveIcon.Data = Asset("icon-x");
            var window = Mount(row);
            try {
                Assert.Equal(new Size(20, 20), row.GripBox.Bounds.Size);
                Assert.Equal(new Size(22, 22), row.RemoveButton.Bounds.Size);
                foreach (var (icon, target) in new[] { (row.Grip, (Control)row.GripBox), (row.RemoveIcon, (Control)row.RemoveButton) }) {
                    var ink = icon.RenderedInkBounds;
                    Assert.True(ink.Width > 0 && ink.Height > 0);
                    Assert.True(ink.Left >= -0.01 && ink.Top >= -0.01 &&
                        ink.Right <= icon.Bounds.Width + 0.01 && ink.Bottom <= icon.Bounds.Height + 0.01);
                    Center(icon, target, ink.Center);
                    Assert.False(icon.IsHitTestVisible);
                }
                Assert.Equal(row.RemoveButton.Foreground, row.RemoveIcon.Fill);
                Assert.Equal(new CornerRadius(12), row.RowRoot.CornerRadius);
            } finally { window.Close(); }
        }

        [AvaloniaFact]
        public void LabelsKeepTheirTypography_AndLongTitlesStayInsideTheirColumn() {
            using var owner = new FxChainViewModel();
            owner.Unsubscribe();
            var vm = Model(owner);
            vm.Name = "A long effect name that must fit beside its format badge";
            var row = new FxChainRow { DataContext = vm };
            var window = Mount(row);
            try {
                Assert.Equal(12, row.NameText.FontSize);
                Assert.Equal(FontWeight.SemiBold, row.NameText.FontWeight);
                Assert.Equal(TextTrimming.CharacterEllipsis, row.NameText.TextTrimming);
                Assert.Equal(9, row.BadgeLabel.FontSize);
                foreach (var text in new[] { row.NameText, row.BadgeLabel, row.DetailText, row.StateChipText }) {
                    Assert.Equal(row.FontFamily, text.FontFamily);
                    Assert.NotNull(text.Foreground);
                    Assert.False(text.UseLayoutRounding);
                }
                var titleGrid = (Grid)row.NameText.Parent!;
                Assert.True(row.NameText.Bounds.Right <= row.BadgeChip.Bounds.Left + 0.01);
                Assert.True(row.BadgeChip.Bounds.Right <= titleGrid.Bounds.Width + 0.01);
                Center(row.BadgeLabel, row.BadgeChip, new Point(row.BadgeLabel.Bounds.Width / 2, row.BadgeLabel.Bounds.Height / 2));
                vm.IsMuted = true;
                Settle(window);
                Assert.True(row.StateChip.IsVisible);
                Center(row.StateChipText, row.StateChip, new Point(row.StateChipText.Bounds.Width / 2, row.StateChipText.Bounds.Height / 2));
            } finally { window.Close(); }
        }

        [AvaloniaFact]
        public void SwitchKeepsItsPillAndThumbGeometry_WithSharedKeyboardFocusAndDisabledFeedback() {
            using var owner = new FxChainViewModel();
            owner.Unsubscribe();
            var row = new FxChainRow { DataContext = Model(owner) };
            var window = Mount(row);
            try {
                var power = row.PowerToggle;
                Assert.Equal(new Size(34, 20), power.Bounds.Size);
                var thumb = Part<Border>(power, "PART_Thumb");
                Assert.Equal(new Size(14, 14), thumb.Bounds.Size);
                Assert.Equal(HorizontalAlignment.Right, thumb.HorizontalAlignment);
                Assert.Equal(power.Foreground, thumb.Background);
                power.IsChecked = false;
                Settle(window);
                Assert.Equal(HorizontalAlignment.Left, thumb.HorizontalAlignment);
                Assert.Equal(power.Foreground, thumb.Background);
                power.Focus(NavigationMethod.Tab);
                Settle(window);
                Assert.Equal(1, Part<Border>(power, "PART_FocusRing").Opacity);
                Assert.Equal(new Size(34, 20), power.Bounds.Size);
                Assert.Equal(0, Part<Border>(power, "PART_StateLayer").Opacity);
                power.IsEnabled = false;
                Settle(window);
                Assert.Equal(0.38, power.Opacity, 3);
                row.RemoveButton.Focus(NavigationMethod.Tab);
                Settle(window);
                Assert.Equal(1, Part<Border>(row.RemoveButton, "PART_FocusRing").Opacity);
            } finally { window.Close(); }
        }

        [AvaloniaFact]
        public void RemountRestoresLiveState_WithoutActivatingAPluginOrWritingTheDocument() {
            using var owner = new FxChainViewModel();
            owner.Unsubscribe();
            var vm = Model(owner);
            var row = new FxChainRow { DataContext = vm };
            var host = new Panel { Children = { row } };
            var window = Mount(host);
            try {
                host.Children.Remove(row);
                vm.Name = "Updated FX";
                vm.IsPowered = false;
                Assert.Equal("Demo FX", row.NameText.Text);
                host.Children.Add(row);
                Assert.Equal("Updated FX", row.NameText.Text);
                Assert.False(row.PowerToggle.IsChecked);
                vm.Name = "Live FX";
                Assert.Equal("Live FX", row.NameText.Text);
                int activates = 0;
                vm.Activated += (_, _) => activates++;
                row.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                Assert.Equal(1, activates);
                row.PowerToggle.IsChecked = true;
                row.PowerToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(row.PowerToggle.IsChecked); // No owner track: rejected request rolls back.
                Assert.Equal(1, activates);
            } finally { window.Close(); }
        }
    }
}

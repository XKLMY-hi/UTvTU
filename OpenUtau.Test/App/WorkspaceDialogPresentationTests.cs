using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Linq;
using OpenUtau.App.Controls;
using OpenUtau.App.Views;
using OpenUtau.Core.Util;
using OpenUtau.Core.Theming;
using OpenUtau.Theming;
using Xunit;

namespace OpenUtau.Test.App;

[Collection("WorkspaceSession")]
public sealed class WorkspaceDialogPresentationTests : IDisposable {
    readonly bool previousMotion = Preferences.Default.ReduceMotion;
    readonly bool previousDark = ColorPool.Current.IsDark;
    sealed class Host : WindowEx, IWorkspaceToolHost {
        public readonly TextBox Input = new() { Text = "editor" };
        public readonly Grid Surface = new();
        public readonly WorkspaceDialogHost Dialogs = new();
        public Host() {
            Width = 800; Height = 600;
            Surface.Children.Add(Input);
            Dialogs.BackgroundSurface = Surface;
            var root = new Grid(); root.Children.Add(Surface); root.Children.Add(Dialogs); Content = root;
        }
        public void PresentTool(WorkspaceToolView tool) => Dialogs.Push(tool);
    }
    [AvaloniaFact]
    public async Task DismissalKeepsBlurAndInputBlockedUntilTheCardHasFinishedLeaving() {
        Preferences.Default.ReduceMotion = false;
        var host = new Host(); host.Show(); Dispatcher.UIThread.RunJobs(); host.Input.Focus();
        try {
            var tool = new TypeInDialog(); var result = tool.ShowDialog(host);
            Dispatcher.UIThread.RunJobs();
            Assert.IsType<BlurEffect>(host.Surface.Effect);
            Assert.False(host.Surface.IsEnabled);
            tool.Close(); await result;
            Assert.False(host.Dialogs.HasDialogs);
            Assert.True(host.Dialogs.IsBlocking);
            Assert.False(host.Surface.IsEnabled);
            await Task.Delay(250); Dispatcher.UIThread.RunJobs();
            Assert.False(host.Dialogs.IsVisible);
            Assert.True(host.Surface.IsEnabled);
            Assert.Null(host.Surface.Effect);
            Assert.Same(host.Input, host.FocusManager!.GetFocusedElement());
        } finally { host.Dialogs.Shutdown(); host.Close(); }
    }
    [AvaloniaFact]
    public async Task OpeningAnotherDialogDuringFadeOutCannotBeDismissedByTheOldTimer() {
        Preferences.Default.ReduceMotion = false;
        var host = new Host(); host.Show();
        try {
            var first = new TypeInDialog(); first.Show(host); Dispatcher.UIThread.RunJobs(); first.Close();
            var next = new TypeInDialog(); var result = next.ShowDialog(host); Dispatcher.UIThread.RunJobs();
            await Task.Delay(250); Dispatcher.UIThread.RunJobs();
            Assert.True(host.Dialogs.HasDialogs);
            Assert.True(host.Dialogs.IsVisible);
            Assert.False(result.IsCompleted);
            Assert.False(host.Surface.IsEnabled);
            Assert.IsType<BlurEffect>(host.Surface.Effect);
        } finally { host.Dialogs.Shutdown(); host.Close(); }
    }
    [AvaloniaFact]
    public void ReducedMotionClosesImmediatelyWhileAnUnderlyingOverlayKeepsItsBlur() {
        Preferences.Default.ReduceMotion = true;
        var host = new Host(); host.Show();
        try {
            host.Dialogs.SetExternalModalVisible(true);
            var tool = new TypeInDialog(); tool.Show(host); Dispatcher.UIThread.RunJobs(); tool.Close();
            Assert.False(host.Dialogs.IsBlocking);
            Assert.True(host.Dialogs.IsBackgroundBlocked);
            Assert.False(host.Surface.IsEnabled);
            Assert.IsType<BlurEffect>(host.Surface.Effect);
            host.Dialogs.SetExternalModalVisible(false);
            Assert.True(host.Surface.IsEnabled);
            Assert.Null(host.Surface.Effect);
        } finally { host.Dialogs.Shutdown(); host.Close(); }
    }
    [AvaloniaFact]
    public void EmbeddedInputToolSharesTheCardSurfaceInsteadOfPaintingASquare() {
        var host = new Host(); host.Show();
        try {
            var tool = new TypeInDialog { Title = "BPM" };
            tool.Show(host); Dispatcher.UIThread.RunJobs(); host.UpdateLayout();
            Assert.True(tool.Background == null || tool.Background is ISolidColorBrush { Color.A: 0 });
            var card = host.Dialogs.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("md3-pop"));
            var position = tool.TranslatePoint(default, card)!.Value;
            Assert.True(position.X >= card.Padding.Left);
            Assert.True(position.Y >= card.Padding.Top);
            Assert.True(position.X + tool.Bounds.Width <= card.Bounds.Width - card.Padding.Right + .01);
            Assert.True(position.Y + tool.Bounds.Height <= card.Bounds.Height - card.Padding.Bottom + .01);
            var button = tool.FindControl<Button>("OkButton")!;
            AssertButtonLabelIsCentered(button);
            var root = button.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Root");
            Assert.Equal(button.Bounds.Size, root.Bounds.Size);
            Assert.Equal(button.CornerRadius, root.CornerRadius);
        } finally { host.Dialogs.Shutdown(); host.Close(); }
    }
    static Color ResourceColor(Control control, string key) {
        Assert.True(control.TryFindResource(key, out var resource));
        return Assert.IsAssignableFrom<ISolidColorBrush>(resource).Color;
    }
    static void AssertButtonLabelIsCentered(Button button) {
        Assert.Equal(40, button.Bounds.Height);
        Assert.True(button.Bounds.Width >= 80);
        Assert.Equal(new CornerRadius(999), button.CornerRadius);
        var label = button.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Label");
        Assert.Equal(button.FontSize, label.FontSize);
        Assert.Equal(button.Foreground, label.Foreground);
        var center = label.TranslatePoint(new Point(label.Bounds.Width / 2, label.Bounds.Height / 2), button)!.Value;
        Assert.InRange(Math.Abs(center.X - button.Bounds.Width / 2), 0, .6);
        Assert.InRange(Math.Abs(center.Y - button.Bounds.Height / 2), 0, .6);
    }
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmationButtonsKeepTheirRolesAndResultsInBothThemes(bool dark) {
        Preferences.Default.ReduceMotion = true;
        ColorPool.Initialize(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, dark);
        var host = new Host(); host.Show();
        try {
            foreach (var (role, expected) in new[] {
                ("primary", MessageBox.MessageBoxResult.Yes),
                ("tonal", MessageBox.MessageBoxResult.No),
                ("text", MessageBox.MessageBoxResult.Cancel),
            }) {
                var completion = MessageBox.Show(host, "Save changes?", "Confirm", MessageBox.MessageBoxButtons.YesNoCancel);
                Dispatcher.UIThread.RunJobs(); host.UpdateLayout();
                var buttons = host.Dialogs.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("dialogAction")).ToArray();
                Assert.Equal(3, buttons.Length);
                Assert.All(buttons, AssertButtonLabelIsCentered);
                var primary = buttons.Single(b => b.Classes.Contains("primary"));
                var secondary = buttons.Single(b => b.Classes.Contains("tonal"));
                var cancel = buttons.Single(b => b.Classes.Contains("text"));
                Assert.Equal(ResourceColor(primary, "md3.primary"), Assert.IsAssignableFrom<ISolidColorBrush>(primary.Background).Color);
                Assert.Equal(ResourceColor(primary, "md3.on-primary"), Assert.IsAssignableFrom<ISolidColorBrush>(primary.Foreground).Color);
                Assert.Equal(ResourceColor(secondary, "md3.secondary-container"), Assert.IsAssignableFrom<ISolidColorBrush>(secondary.Background).Color);
                Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(cancel.Background).Color.A);
                Assert.All(buttons, b => Assert.Equal(new Thickness(0), b.BorderThickness));
                buttons.Single(b => b.Classes.Contains(role)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(expected, await completion);
                Assert.False(host.Dialogs.HasDialogs);
            }
        } finally { host.Dialogs.Shutdown(); host.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void InputAndMappingDialogsShareFooterMetricsAndCloseIcons(bool dark) {
        Preferences.Default.ReduceMotion = true;
        ColorPool.Initialize(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, dark);
        var host = new Host(); host.Show();
        try {
            foreach (var tool in new WorkspaceToolView[] { new TypeInDialog(), new TimeSignatureDialog(), new TrackColorDialog(), new VoiceColorMappingDialog() }) {
                tool.Show(host); Dispatcher.UIThread.RunJobs(); host.UpdateLayout();
                var buttons = tool.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("dialogAction")).ToArray();
                Assert.NotEmpty(buttons);
                Assert.All(buttons, AssertButtonLabelIsCentered);
                var primary = buttons.Single(b => b.Classes.Contains("primary"));
                Assert.Equal(ResourceColor(primary, "md3.primary"), Assert.IsAssignableFrom<ISolidColorBrush>(primary.Background).Color);
                var close = host.Dialogs.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("dialogClose"));
                Assert.Equal(new Size(32, 32), close.Bounds.Size);
                Assert.IsType<DawIcon>(close.Content);
                tool.Close();
            }
        } finally { host.Dialogs.Shutdown(); host.Close(); }
    }
    public void Dispose() {
        Preferences.Default.ReduceMotion = previousMotion;
        ColorPool.SetDark(previousDark);
    }
}

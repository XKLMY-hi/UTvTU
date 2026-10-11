using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Linq;
using OpenUtau.App.Controls;
using OpenUtau.App.Views;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Test.App;

[Collection("WorkspaceSession")]
public sealed class WorkspaceDialogPresentationTests : IDisposable {
    readonly bool previousMotion = Preferences.Default.ReduceMotion;
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
            var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_ContentPresenter");
            Assert.True(presenter.Background == null || presenter.Background is ISolidColorBrush { Color.A: 0 });
            var root = button.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Root");
            Assert.Equal(button.Bounds.Size, root.Bounds.Size);
            Assert.Equal(button.CornerRadius, root.CornerRadius);
        } finally { host.Dialogs.Shutdown(); host.Close(); }
    }
    public void Dispose() => Preferences.Default.ReduceMotion = previousMotion;
}

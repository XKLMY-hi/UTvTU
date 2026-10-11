using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App.Controls;
using OpenUtau.App.Editing;
using OpenUtau.App.ViewModels;
using OpenUtau.App.Views;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.App;

[Collection("WorkspaceSession")]
public sealed class WorkspaceToolHostingTests : IDisposable {
    readonly DocManagerTestSetup.ScopedDispatcher dispatcher = DocManagerTestSetup.EnterScopedDispatcher();
    readonly UProject previous = DocManager.Inst.Project;
    readonly string previousPath = PathManager.Inst.DataPath;
    readonly bool previousPreview = Preferences.Default.LyricLivePreview;
    readonly bool previousSelection = Preferences.Default.LyricApplySelectionOnly;
    readonly string directory = Path.Combine(Path.GetTempPath(), "utvtu-tools-" + Guid.NewGuid().ToString("N"));
    public WorkspaceToolHostingTests() {
        Directory.CreateDirectory(directory);
        typeof(PathManager).GetProperty(nameof(PathManager.DataPath))!.SetValue(PathManager.Inst, directory);
    }
    sealed class HostWindow : WindowEx, IWorkspaceToolHost {
        public readonly ToolWorkspaceView Pages = new();
        public readonly WorkspaceDialogHost Dialogs = new();
        public HostWindow() {
            Width = 1100; Height = 700;
            var root = new Grid(); root.Children.Add(Pages); root.Children.Add(Dialogs); Content = root;
        }
        public void PresentTool(WorkspaceToolView tool) {
            if (tool is RenderWindow) { Pages.SetContent("render", tool); Pages.ShowPage("render"); }
            else if (tool is SingersDialog) { Pages.SetContent("singers", tool); Pages.ShowPage("singers"); }
            else Dialogs.Push(tool);
        }
    }
    [AvaloniaFact]
    public async Task ToolFromADetachedWindowRoutesToItsWorkspaceWithoutAnotherNativeWindow() {
        var host = new HostWindow(); host.Show();
        var detached = new WindowEx { WorkspaceOwner = host, Width = 300, Height = 200 }; detached.Show();
        var prompt = new TypeInDialog();
        try {
            var completion = prompt.ShowDialog(detached);
            Assert.Same(host, prompt.WindowHost);
            Assert.True(host.Dialogs.HasDialogs);
            Assert.False(completion.IsCompleted);
            host.Dialogs.CloseTop(); await completion;
            Assert.True(detached.IsVisible);
        } finally { prompt.Close(); detached.Close(); host.Close(); }
    }
    [AvaloniaFact]
    public async Task NestedDialogsRestoreTheUnderlyingControlAndCompleteOnlyTheirOwnTask() {
        var host = new HostWindow(); host.Show();
        var parent = new TypeInDialog(); parent.SetText("original");
        var parentTask = parent.ShowDialog(host);
        Dispatcher.UIThread.RunJobs();
        var child = new TypeInDialog(); child.SetText("nested");
        var childTask = child.ShowDialog(parent);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(host, child.WindowHost);
        child.Close();
        await childTask;
        Assert.False(parentTask.IsCompleted);
        Assert.True(host.Dialogs.HasDialogs);
        Assert.Equal("original", parent.FindControl<TextBox>("TextBox")!.Text);
        host.Dialogs.CloseTop();
        await parentTask;
        Assert.False(host.Dialogs.HasDialogs);
        Assert.True(parent.IsClosed);
        host.Close();
    }
    [AvaloniaFact]
    public async Task ClosingAnUnderlyingDialogDoesNotDismissANewerConfirmation() {
        var host = new HostWindow(); host.Show();
        var first = new TypeInDialog(); var firstTask = first.ShowDialog(host);
        var second = new TypeInDialog(); var secondTask = second.ShowDialog(host);
        first.Close(); await firstTask;
        Assert.False(secondTask.IsCompleted);
        Assert.True(host.Dialogs.HasDialogs);
        second.Close(); await secondTask;
        Assert.False(host.Dialogs.HasDialogs);
        host.Close();
    }
    [AvaloniaFact]
    public async Task MessageConfirmationUsesTheEmbeddedHostAndEscapeDefaultIsPreserved() {
        var host = new HostWindow(); host.Show();
        var result = MessageBox.Show(host, "Delete?", "Confirm", MessageBox.MessageBoxButtons.YesNo);
        Assert.True(host.Dialogs.HasDialogs);
        host.Dialogs.CloseTop();
        Assert.Equal(MessageBox.MessageBoxResult.No, await result);
        Assert.False(host.Dialogs.HasDialogs);
        host.Close();
    }
    [AvaloniaFact]
    public async Task RenderContinuesWhileAnotherPageIsShownAndKeepsItsConfiguration() {
        var project = new UProject();
        var part = new UWavePart { trackNo = 0, channels = 2, sampleRate = 44100 };
        typeof(UWavePart).GetProperty(nameof(UWavePart.Samples))!.SetValue(part, Enumerable.Repeat(.2f, 8820).ToArray());
        typeof(UWavePart).GetField("duration", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(part, 96);
        project.parts.Add(part);
        typeof(DocManager).GetProperty(nameof(DocManager.Project))!.SetValue(DocManager.Inst, project);
        var host = new HostWindow(); host.Show();
        var tool = new RenderWindow();
        try {
            tool.Show(host);
            string path = Path.Combine(directory, "embedded.wav");
            tool.FindControl<TextBox>("OutputPathBox")!.Text = path;
            tool.FindControl<ComboBox>("FormatCombo")!.SelectedIndex = 1;
            tool.OnStartRender(null, new RoutedEventArgs());
            host.Pages.ShowPage("singers");
            Assert.False(tool.IsClosed);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (tool.IsRendering && watch.Elapsed.TotalSeconds < 5) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.False(tool.IsRendering);
            Assert.True(File.Exists(path));
            tool.Activate();
            Assert.Equal(path, tool.FindControl<TextBox>("OutputPathBox")!.Text);
            Assert.Equal(1, tool.FindControl<ComboBox>("FormatCombo")!.SelectedIndex);
            Assert.Equal(100, tool.FindControl<ProgressBar>("ProgressBarControl")!.Value);
        } finally { tool.Close(); host.Close(); }
    }
    UVoicePart LyricsPart() {
        var project = new UProject { Saved = true };
        var part = new UVoicePart { Duration = 1920 };
        var a = project.CreateNote(60, 0, 480); a.lyric = "a";
        var b = project.CreateNote(62, 480, 480); b.lyric = "b";
        part.notes.Add(a); part.notes.Add(b); project.parts.Add(part);
        DocManager.Inst.ExecuteCmd(new LoadProjectNotification(project));
        return part;
    }
    [AvaloniaFact]
    public async Task EmbeddedPromptKeyboardCancelAndConfirmKeepTheirOriginalSemantics() {
        var host = new HostWindow(); host.Show();
        try {
            string value = "unchanged";
            var cancel = new TypeInDialog { onFinish = text => value = text };
            cancel.SetText("discarded");
            var cancelled = cancel.ShowDialog(host);
            cancel.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            await cancelled;
            Assert.Equal("unchanged", value);
            var confirm = new TypeInDialog { onFinish = text => value = text };
            confirm.SetText("accepted");
            var confirmed = confirm.ShowDialog(host);
            confirm.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            await confirmed;
            Assert.Equal("accepted", value);
            Assert.False(host.Dialogs.HasDialogs);
        } finally { host.Dialogs.Shutdown(); host.Close(); }
    }
    [AvaloniaFact]
    public void ResourceTableKeepsVirtualizationAndSearchWhenPagesChangeAtNarrowWidth() {
        var host = new HostWindow { Width = 800, Height = 650 }; host.Show();
        var tool = new SingersDialog();
        try {
            tool.Show(host);
            var table = tool.FindControl<DataGrid>("OtoGrid")!;
            table.ItemsSource = Enumerable.Range(0, 1000).Select(i => UOto.OfDummy("alias-" + i)).ToArray();
            var search = tool.FindControl<TextBox>("AliasSearch")!;
            search.Text = "retained";
            Dispatcher.UIThread.RunJobs();
            Assert.InRange(table.Bounds.Height, 1, 650);
            Assert.InRange(table.GetVisualDescendants().OfType<DataGridRow>().Count(), 1, 100);
            Assert.False(tool.FindControl<DataGrid>("SubbanksGrid")!.IsVisible);
            host.Pages.ShowPage("render");
            Assert.False(tool.IsClosed);
            tool.Activate(); Dispatcher.UIThread.RunJobs();
            Assert.Equal("retained", search.Text);
            Assert.InRange(table.Bounds.Height, 1, 650);
        } finally { tool.Close(); host.Close(); }
    }
    [AvaloniaFact]
    public void ShortcutPreferencesSearchSurvivesCategoryNavigationAndReleasesWithTheSession() {
        var preferences = new PreferencesView();
        var host = new WindowEx { Width = 1100, Height = 700, Content = preferences }; host.Show();
        try {
            preferences.ShowShortcuts(); Dispatcher.UIThread.RunJobs();
            var page = preferences.FindControl<ContentControl>("PageShortcuts")!;
            var tool = Assert.IsType<ShortcutOverviewWindow>(page.Content);
            var search = tool.FindControl<TextBox>("SearchBox")!;
            search.Text = "no-such-command-012345";
            Dispatcher.UIThread.RunJobs();
            Assert.True(Assert.IsType<ShortcutOverviewViewModel>(tool.DataContext).IsEmpty);
            preferences.ShowDefaultPage();
            Assert.False(page.IsVisible);
            preferences.ShowShortcuts();
            Assert.Same(tool, page.Content);
            Assert.Equal("no-such-command-012345", search.Text);
            Assert.True(preferences.FindControl<Button>("NavShortcuts")!.Classes.Contains("selected"));
            preferences.ReleaseSession();
            Assert.True(tool.IsClosed);
            Assert.Null(page.Content);
        } finally { preferences.ReleaseSession(); host.Close(); }
    }
    [AvaloniaFact]
    public async Task LoadingFailureRemovesOnlyTheBusyLayerAndKeepsItsUnderlyingPrompt() {
        var host = new HostWindow(); host.Show();
        var prompt = new TypeInDialog(); var completion = prompt.ShowDialog(host);
        try {
            var work = new TaskCompletionSource();
            var loading = LoadingWindow.LoadForAsyncTask(work.Task, host, 0);
            Assert.True(LoadingWindow.IsLoading());
            host.Dialogs.CloseTop();
            Assert.True(LoadingWindow.IsLoading());
            Assert.False(completion.IsCompleted);
            work.SetException(new InvalidOperationException("expected failure"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => loading);
            Assert.False(LoadingWindow.IsLoading());
            Assert.True(host.Dialogs.HasDialogs);
            Assert.False(completion.IsCompleted);
        } finally { LoadingWindow.EndLoading(); host.Dialogs.Shutdown(); host.Close(); }
    }
    [AvaloniaFact]
    public void LyricsPreviewDoesNotKeepTheGlobalUndoGroupOpenAndCancelRestoresOwnedLyrics() {
        var part = LyricsPart();
        using var vm = new LyricsViewModel { LivePreview = true };
        vm.Start(part, part.notes.ToArray(), Array.Empty<UNote>());
        vm.Text = "x y";
        Assert.Equal(new[] { "x", "y" }, part.notes.Select(n => n.lyric));
        Assert.Null(DocManager.Inst.ActiveUndoGroup);
        vm.Cancel();
        Assert.Equal(new[] { "a", "b" }, part.notes.Select(n => n.lyric));
        Assert.Null(DocManager.Inst.ActiveUndoGroup);
    }
    [AvaloniaFact]
    public void LyricsCancelPreservesExternalEditsAndUndoIsStillAvailable() {
        var part = LyricsPart();
        using var vm = new LyricsViewModel { LivePreview = true };
        vm.Start(part, part.notes.ToArray(), Array.Empty<UNote>());
        vm.Text = "x y";
        using (var edits = new DocumentEditGesture(DocManager.Inst.Project, "external"))
            Assert.True(edits.Execute(new ChangeNoteLyricCommand(part, part.notes.First(), "external")));
        vm.Cancel();
        Assert.Equal(new[] { "external", "b" }, part.notes.Select(n => n.lyric));
        DocManager.Inst.Undo();
        Assert.Equal(new[] { "external", "y" }, part.notes.Select(n => n.lyric));
    }
    [AvaloniaFact]
    public void LyricsInputCannotAppendToAnotherControlsTransactionOrModifyAReplacementProject() {
        var part = LyricsPart();
        using var vm = new LyricsViewModel { LivePreview = true };
        vm.Start(part, part.notes.ToArray(), Array.Empty<UNote>());
        DocManager.Inst.StartUndoGroup("external");
        var group = DocManager.Inst.ActiveUndoGroup;
        vm.Text = "x y";
        Assert.Same(group, DocManager.Inst.ActiveUndoGroup);
        Assert.Empty(group!.Commands);
        Assert.Equal("a", part.notes.First().lyric);
        DocManager.Inst.EndUndoGroup();
        var replacement = new UProject();
        DocManager.Inst.ExecuteCmd(new LoadProjectNotification(replacement));
        vm.Text = "late"; vm.Cancel();
        Assert.Equal("a", part.notes.First().lyric);
        Assert.Null(DocManager.Inst.ActiveUndoGroup);
    }
    public void Dispose() {
        if (DocManager.Inst.ActiveUndoGroup != null) DocManager.Inst.EndUndoGroup();
        typeof(DocManager).GetProperty(nameof(DocManager.Project))!.SetValue(DocManager.Inst, previous);
        typeof(PathManager).GetProperty(nameof(PathManager.DataPath))!.SetValue(PathManager.Inst, previousPath);
        Preferences.Default.LyricLivePreview = previousPreview; Preferences.Default.LyricApplySelectionOnly = previousSelection;
        dispatcher.Dispose();
        Directory.Delete(directory, true);
    }
}

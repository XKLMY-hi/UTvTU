using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
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
            else Dialogs.Push(tool);
        }
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

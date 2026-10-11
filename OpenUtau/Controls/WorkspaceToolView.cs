using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.Styling;
using OpenUtau.App.Views;

namespace OpenUtau.App.Controls;

public interface IWorkspaceToolHost {
    void PresentTool(WorkspaceToolView tool);
}

/// <summary>A tool is a control. Its session ends on Close, never on navigation or reparenting.</summary>
public class WorkspaceToolView : UserControl {
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<WorkspaceToolView, string?>(nameof(Title));
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public Window WindowHost => owner ?? TopLevel.GetTopLevel(this) as Window
        ?? throw new InvalidOperationException("Tool has no window host.");
    public bool IsClosed { get; private set; }
    public event EventHandler? Opened;
    public event EventHandler? Closed;
    Window? owner, fallback;
    TaskCompletionSource? completion;
    internal Action<WorkspaceToolView>? RemoveFromHost;

    public WorkspaceToolView() {
        this[!BackgroundProperty] = new DynamicResourceExtension("md3.surface");
        Styles.Add(new StyleInclude(new Uri("avares://OpenUtau/")) {
            Source = new Uri("avares://OpenUtau/Styles/ToolWorkspace.axaml"),
        });
    }
    public void Show() => Show(null);
    public void Show(Window? owner) => _ = Present(owner, false);
    public Task ShowDialog(Window owner) => Present(owner, true);
    public Task ShowDialog(WorkspaceToolView owner) => ShowDialog(owner.WindowHost);
    Task Present(Window? window, bool modal) {
        if (IsClosed) throw new InvalidOperationException("A closed tool session cannot be reused.");
        if (completion != null) { Activate(); return completion.Task; }
        completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        owner = window;
        var shell = window;
        if (shell is WindowEx { WorkspaceOwner: Window workspace }) shell = workspace;
        while (shell?.Owner is Window parent) shell = parent;
        if (shell is IWorkspaceToolHost host) {
            owner = shell;
            host.PresentTool(this);
            Opened?.Invoke(this, EventArgs.Empty);
        } else {
            // Detached editors and test hosts may have no main shell.
            double width = double.IsNaN(Width) ? 900 : Width;
            double height = double.IsNaN(Height) ? 640 : Height;
            Width = Height = double.NaN;
            fallback = new WindowEx { Content = this, Width = width, Height = height, Title = Title,
                WindowStartupLocation = WindowStartupLocation.CenterOwner };
            owner = fallback;
            fallback.Opened += (_, _) => Opened?.Invoke(this, EventArgs.Empty);
            fallback.Closed += (_, _) => CompleteClose();
            if (modal && window != null) _ = fallback.ShowDialog(window);
            else if (window != null) fallback.Show(window);
            else fallback.Show();
        }
        return completion.Task;
    }
    public void Activate() {
        if (IsClosed) return;
        if (owner is IWorkspaceToolHost host) host.PresentTool(this);
        else fallback?.Activate();
        Focus();
    }
    public void Close() {
        if (IsClosed) return;
        RemoveFromHost?.Invoke(this);
        RemoveFromHost = null;
        fallback?.Close();
        CompleteClose();
    }
    void CompleteClose() {
        if (IsClosed) return;
        IsClosed = true;
        try {
            OnClosed(EventArgs.Empty);
            Closed?.Invoke(this, EventArgs.Empty);
        } finally { completion?.TrySetResult(); }
    }
    protected virtual void OnClosed(EventArgs e) { }
}

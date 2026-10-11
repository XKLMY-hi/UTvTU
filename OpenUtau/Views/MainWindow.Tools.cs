using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;

namespace OpenUtau.App.Views;

public partial class MainWindow : IWorkspaceToolHost {
    RenderWindow? renderTool;
    SingersDialog? singersTool;
    PackageManagerDialog? packagesTool;
    readonly HashSet<WorkspaceToolView> tools = new();
    ToolWorkspaceView? toolPages;
    WorkspaceToolView? contextTool;
    IDisposable? contextTitleBinding;

    void IWorkspaceToolHost.PresentTool(WorkspaceToolView tool) => PresentTool(tool);
    void InitializeToolHosting() {
        ToolDialogHost.DialogsChanged = () => {
            bool enabled = !ToolDialogHost.HasDialogs;
            MainGrid.IsEnabled = WelcomeHost.IsEnabled = PreferencesHost.IsEnabled = ToolWorkspaceHost.IsEnabled = OverlayLayer.IsEnabled = enabled;
        };
    }

    void EnsureToolPages() {
        if (toolPages != null) return;
        toolPages = new ToolWorkspaceView {
            Navigate = page => { if (page == "render") ShowRenderWorkspace(); else ShowResourceCenter(page == "packages"); },
            ReturnToEditor = HideToolWorkspace, Preferences = ShowPreferences,
        };
        ToolWorkspaceHost.Content = toolPages;
    }
    void EnterToolWorkspace(string page) {
        FinishPartEdit();
        pianoRoll?.CancelActiveInput();
        ++pianoRollOpenGeneration;
        ++preferencesGeneration;
        PreferencesHost.IsVisible = false;
        PreferencesHost.ReleaseSession();
        EnsureToolPages();
        toolPages!.ShowPage(page);
        ToolWorkspaceHost.IsVisible = true;
        ToolWorkspaceHost.Focus();
    }
    public void ShowRenderWorkspace() {
        if (renderTool == null || renderTool.IsClosed) {
            renderTool = new RenderWindow();
            renderTool.JobStateChanged += RefreshDeliveryStatus;
            renderTool.Show(this);
        } else EnterToolWorkspace("render");
    }
    public void ShowResourceCenter(bool packages) {
        EnsureToolPages();
        if (packages) {
            if (packagesTool == null || packagesTool.IsClosed) {
                packagesTool = new PackageManagerDialog { DataContext = new PackageManagerViewModel() };
                packagesTool.Show(this);
            } else EnterToolWorkspace("packages");
        } else {
            if (singersTool == null || singersTool.IsClosed) {
                singersTool = new SingersDialog { DataContext = new SingersViewModel() };
                singersTool.Show(this);
            } else EnterToolWorkspace("singers");
        }
    }
    internal void PresentTool(WorkspaceToolView tool) {
        if (shellDisposed || tool.IsClosed) return;
        tools.Add(tool);
        tool.Closed -= OnToolClosed;
        tool.Closed += OnToolClosed;
        switch (tool) {
            case RenderWindow: EnsureToolPages(); toolPages!.SetContent("render", tool); EnterToolWorkspace("render"); break;
            case SingersDialog: EnsureToolPages(); toolPages!.SetContent("singers", tool); EnterToolWorkspace("singers"); break;
            case PackageManagerDialog: EnsureToolPages(); toolPages!.SetContent("packages", tool); EnterToolWorkspace("packages"); break;
            case TrackSettingsDialog or MixFxDialog or LyricsDialog or ExpressionsDialog:
                HideToolWorkspace();
                if (!ReferenceEquals(contextTool, tool)) contextTool?.Close();
                contextTool = tool;
                contextTitleBinding?.Dispose();
                contextTitleBinding = ContextToolTitle.Bind(TextBlock.TextProperty, tool.GetObservable(WorkspaceToolView.TitleProperty));
                ContextToolHost.Content = tool;
                ContextToolPanel.IsVisible = true;
                EditorToolsLayout.RowDefinitions[2].Height = new GridLength(Math.Clamp(Bounds.Height * .43, 220, 480));
                ContextToolSplitter.IsVisible = true;
                tool.RemoveFromHost = RemoveContextTool;
                tool.Focus();
                break;
            default: ToolDialogHost.Push(tool); break;
        }
    }
    void OnToolClosed(object? sender, EventArgs e) { if (sender is WorkspaceToolView tool) { tools.Remove(tool); tool.Closed -= OnToolClosed; } }
    void RemoveContextTool(WorkspaceToolView tool) {
        if (!ReferenceEquals(contextTool, tool)) return;
        ContextToolHost.Content = null;
        contextTool = null;
        contextTitleBinding?.Dispose();
        contextTitleBinding = null;
        ContextToolPanel.IsVisible = ContextToolSplitter.IsVisible = false;
        EditorToolsLayout.RowDefinitions[2].Height = new GridLength(0);
        Focus();
    }
    void OnCloseContextTool(object? sender, RoutedEventArgs e) => contextTool?.Close();
    internal void HideToolWorkspace() {
        ToolWorkspaceHost.IsVisible = false;
        if (shellDisposed) return;
        SetChromeForView(WelcomeHost.IsVisible ? ViewSwitcherPolicy.ChromeFor(AppSurface.Welcome) : viewModel.ViewSwitcher.Chrome);
        Focus();
    }
    void RefreshDeliveryStatus() {
        toolPages?.SetJobStatus(renderTool?.Status);
        DeliveryStatusText.Text = renderTool?.Status;
        DeliveryStatusButton.IsVisible = renderTool?.HasJobStatus == true;
        bool editing = renderTool?.IsRendering != true;
        MainLayout.IsEnabled = ShellHeaderRow.IsEnabled = ContextToolPanel.IsEnabled = editing;
        toolPages?.SetEditingEnabled(editing);
    }
    void OnReturnToDelivery(object? sender, RoutedEventArgs e) => ShowRenderWorkspace();
    void ResetProjectTools() {
        contextTool?.Close();
        renderTool?.Close();
        renderTool = null;
        RefreshDeliveryStatus();
        if (toolPages != null) toolPages.SetContent("render", new Border());
        HideToolWorkspace();
    }
    void ShutdownTools() {
        foreach (var tool in tools.ToArray()) tool.Close();
        tools.Clear();
        ToolDialogHost.Shutdown();
        toolPages?.ReleaseContents();
        ToolWorkspaceHost.Content = null;
        renderTool = null; singersTool = null; packagesTool = null;
    }
}

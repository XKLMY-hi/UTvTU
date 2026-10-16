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
    bool contextCollapsed;
    bool requiredPackagesPromptOpen;

    public async System.Threading.Tasks.Task OfferRequiredPackages(IReadOnlyList<string> packageIds) {
        if (shellDisposed || requiredPackagesPromptOpen) return;
        requiredPackagesPromptOpen = true;
        try {
            var ids = packageIds.Where(MissingPackageException.IsRegistryId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (ids.Length == 0) return;
            var answer = await MessageBox.Show(this,
                string.Format(ThemeManager.GetString("packages.required.prompt"), string.Join(", ", ids)),
                ThemeManager.GetString("packages.install"), MessageBox.MessageBoxButtons.YesNo);
            if (answer != MessageBox.MessageBoxResult.Yes || shellDisposed) return;
            ShowResourceCenter(true);
            if (packagesTool?.DataContext is PackageManagerViewModel model) {
                foreach (var id in ids) {
                    if (shellDisposed || packagesTool.IsClosed || !await model.InstallRequiredAsync(id)) break;
                }
            }
        } catch (Exception e) {
            if (!shellDisposed) await MessageBox.ShowError(this, e);
        } finally { requiredPackagesPromptOpen = false; }
    }
    double contextExpandedHeight;

    void IWorkspaceToolHost.PresentTool(WorkspaceToolView tool) => PresentTool(tool);
    void InitializeToolHosting() {
        ToolDialogHost.BackgroundSurface = WorkspaceSurface;
        ToolDialogHost.DialogsChanged = () => {
            OverlayLayer.IsEnabled = !ToolDialogHost.IsBlocking;
            RefreshDetachedEditing();
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
        if (tool.IsClosed) return;
        if (shellDisposed) { tool.Close(); return; }
        tools.Add(tool);
        tool.Closed -= OnToolClosed;
        tool.Closed += OnToolClosed;
        switch (tool) {
            case RenderWindow: EnsureToolPages(); toolPages!.SetContent("render", tool); EnterToolWorkspace("render"); break;
            case SingersDialog: EnsureToolPages(); toolPages!.SetContent("singers", tool); EnterToolWorkspace("singers"); break;
            case PackageManagerDialog: EnsureToolPages(); toolPages!.SetContent("packages", tool); EnterToolWorkspace("packages"); break;
            case TrackSettingsDialog or MixFxDialog or LyricsDialog or ExpressionsDialog or TrackEffectRack:
                FinishPartEdit();
                pianoRoll?.CancelActiveInput();
                HideToolWorkspace();
                bool sameTool = ReferenceEquals(contextTool, tool);
                if (!sameTool) contextTool?.Close();
                contextTool = tool;
                tool.Width = tool.Height = double.NaN;
                contextTitleBinding?.Dispose();
                contextTitleBinding = ContextToolTitle.Bind(TextBlock.TextProperty, tool.GetObservable(WorkspaceToolView.TitleProperty));
                ContextToolHost.Content = tool;
                ContextToolPanel.IsVisible = true;
                if (!sameTool) contextExpandedHeight = Math.Clamp(Bounds.Height * .43, 220, 480);
                if (!sameTool || contextCollapsed) SetContextCollapsed(false);
                tool.RemoveFromHost = RemoveContextTool;
                tool.Focus();
                break;
            default:
                FinishPartEdit();
                pianoRoll?.CancelActiveInput();
                ToolDialogHost.Push(tool);
                break;
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
    void OnToggleContextTool(object? sender, RoutedEventArgs e) => SetContextCollapsed(!contextCollapsed);
    void SetContextCollapsed(bool collapsed) {
        if (contextTool == null) return;
        if (collapsed && !contextCollapsed && ContextToolPanel.Bounds.Height > 40)
            contextExpandedHeight = Math.Max(160, ContextToolPanel.Bounds.Height);
        contextCollapsed = collapsed;
        ContextToolHost.IsVisible = !collapsed;
        ContextToolSplitter.IsVisible = !collapsed;
        double height = Math.Min(contextExpandedHeight, Math.Max(160, EditorToolsLayout.Bounds.Height - 126));
        EditorToolsLayout.RowDefinitions[2].Height = new GridLength(collapsed ? 40 : height);
        ContextCollapseText[!TextBlock.TextProperty] = new DynamicResourceExtension(collapsed ? "workspace.expand" : "workspace.collapse");
        ContextCollapseIcon[!DawIcon.DataProperty] = new DynamicResourceExtension(collapsed ? "icon-chevron-up" : "icon-chevron-down");
        if (collapsed) CollapseContextButton.Focus();
    }
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
        RefreshDetachedEditing();
    }
    void RefreshDetachedEditing() {
        bool enabled = !ToolDialogHost.IsBackgroundBlocked && renderTool?.IsRendering != true;
        if (pianoRollWindow != null) pianoRollWindow.IsEnabled = enabled;
        if (mixerWindow != null) mixerWindow.IsEnabled = enabled;
    }
    void OnReturnToDelivery(object? sender, RoutedEventArgs e) => ShowRenderWorkspace();
    void OnOpenResources(object? sender, RoutedEventArgs e) => ShowResourceCenter(false);
    public void InstallSinger() => OnMenuInstallSinger(this, new RoutedEventArgs());
    void ResetProjectTools() {
        contextTool?.Close();
        renderTool?.Close();
        renderTool = null;
        RefreshDeliveryStatus();
        if (toolPages != null) toolPages.SetContent("render", new Border());
        HideToolWorkspace();
    }
    void ShutdownTools() {
        LoadingWindow.EndLoading();
        foreach (var tool in tools.ToArray()) tool.Close();
        tools.Clear();
        ToolDialogHost.Shutdown();
        toolPages?.ReleaseContents();
        ToolWorkspaceHost.Content = null;
        renderTool = null; singersTool = null; packagesTool = null;
    }
}

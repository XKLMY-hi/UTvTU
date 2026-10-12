using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using OpenUtau.App.Controls;

namespace OpenUtau.App.Views;

public partial class ToolWorkspaceView : UserControl {
    public Action<string>? Navigate;
    public Action? ReturnToEditor, Preferences;
    public ToolWorkspaceView() {
        InitializeComponent();
        SizeChanged += (_, _) => {
            var narrow = Bounds.Width <= 800;
            WorkspaceColumns.ColumnDefinitions[0].Width = new GridLength(narrow ? 152 : Bounds.Width <= 1040 ? 180 : 232);
            WorkspaceNavigation.Padding = new Thickness(12, 24);
            ResourceContent.Margin = narrow ? new Thickness(16) : new Thickness(24);
        };
    }
    void OnNavigate(object? sender, RoutedEventArgs e) { if (sender is Button { Tag: string page }) Navigate?.Invoke(page); }
    void OnBack(object? sender, RoutedEventArgs e) => ReturnToEditor?.Invoke();
    void OnPreferences(object? sender, RoutedEventArgs e) => Preferences?.Invoke();
    public void ShowPage(string page) {
        ResourceHeader.ShapeKind = page == "packages" ? MaterialShapeKind.Square : MaterialShapeKind.Arch;
        ResourceHeader[!WorkspacePageHeader.DescriptionProperty] = new DynamicResourceExtension(
            page == "packages" ? "workspace.resources.packages.hint" : "workspace.resources.singers.hint");
        ResourceHeader[!WorkspacePageHeader.IconProperty] = new DynamicResourceExtension(page == "packages" ? "icon-package" : "icon-mic");
        RenderContent.IsVisible = page == "render";
        ResourceContent.IsVisible = page != "render";
        SingersContent.IsVisible = page == "singers";
        PackagesContent.IsVisible = page == "packages";
        RenderTab.Classes.Set("selected", page == "render");
        SingersTab.Classes.Set("selected", page == "singers");
        PackagesTab.Classes.Set("selected", page == "packages");
    }
    public void SetContent(string page, Control content) {
        switch (page) {
            case "render": RenderContent.Content = content; break;
            case "singers": SingersContent.Content = content; break;
            default: PackagesContent.Content = content; break;
        }
    }
    public void SetJobStatus(string? status) => JobStatus.Text = status;
    public void SetEditingEnabled(bool enabled) {
        ResourceContent.IsEnabled = enabled;
        PreferencesButton.IsEnabled = enabled;
    }
    public void ReleaseContents() { RenderContent.Content = SingersContent.Content = PackagesContent.Content = null; }
}

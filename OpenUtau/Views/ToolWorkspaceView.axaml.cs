using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OpenUtau.App.Views;

public partial class ToolWorkspaceView : UserControl {
    public Action<string>? Navigate;
    public Action? ReturnToEditor, Preferences;
    public ToolWorkspaceView() => InitializeComponent();
    void OnNavigate(object? sender, RoutedEventArgs e) { if (sender is Button { Tag: string page }) Navigate?.Invoke(page); }
    void OnBack(object? sender, RoutedEventArgs e) => ReturnToEditor?.Invoke();
    void OnPreferences(object? sender, RoutedEventArgs e) => Preferences?.Invoke();
    public void ShowPage(string page) {
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

using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using UTvTU.DawUI.Controls;
namespace OpenUtau.App.Views;
/// <summary>Local demonstration values only; no document, renderer or audio references.</summary>
public partial class DawUiGalleryView : UserControl {
    int actions;
    public DawUiGalleryView() => InitializeComponent();
    void OnDemoAction(object? sender, RoutedEventArgs args) => DemoStatus.Text = string.Format(ThemeManager.GetString("dawui.gallery.clicked"), ++actions);
    void OnParameterCommitted(object? sender, EventArgs args) {
        if (sender is DawParameterField field) DemoStatus.Text = string.Format(ThemeManager.GetString("dawui.gallery.committed"), field.Readout.Text);
    }
}

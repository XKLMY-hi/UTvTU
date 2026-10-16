using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using OpenUtau.App.ViewModels;
using OpenUtau.Core.Ustx;

namespace OpenUtau.App.Controls;

public partial class SingerPicker : UserControl {
    public event Action<USinger>? Chosen;
    public event Action? CloseRequested;
    public event Action? ManageRequested;
    SingerPickerViewModel? Model => DataContext as SingerPickerViewModel;
    public SingerPicker() {
        InitializeComponent();
        AddHandler(KeyDownEvent, HandleKey, RoutingStrategies.Tunnel);
    }
    public void FocusSearch() => SearchBox.Focus();
    void HandleKey(object? sender, KeyEventArgs e) {
        if (Model == null) return;
        if (e.Key is Key.Down or Key.Up) { Model.Move(e.Key == Key.Down ? 1 : -1); e.Handled = true; }
        else if (e.Key == Key.Enter) {
            if (e.Source is Visual source && source.GetSelfAndVisualAncestors().OfType<Button>().Any()) return;
            if (Model.Selected != null) Chosen?.Invoke(Model.Selected.Singer);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape) {
            if (Model.Search.Length > 0) Model.Search = "";
            else CloseRequested?.Invoke();
            e.Handled = true;
        }
    }
    void RowTapped(object? sender, TappedEventArgs e) {
        if (e.Source is Visual source && source.GetSelfAndVisualAncestors().OfType<Button>().Any()) return;
        if ((sender as Control)?.DataContext is SingerPickerRow row) Chosen?.Invoke(row.Singer);
        e.Handled = true;
    }
    void FavoriteClicked(object? sender, RoutedEventArgs e) {
        if ((sender as Control)?.DataContext is SingerPickerRow row) Model?.ToggleFavorite(row);
        e.Handled = true;
    }
    void ManageClicked(object? sender, RoutedEventArgs e) { ManageRequested?.Invoke(); e.Handled = true; }
}

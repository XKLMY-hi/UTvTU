using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App.Controls;

namespace OpenUtau.App.Views;

/// <summary>Nested confirmations retain the underlying tool and complete exactly once.</summary>
public sealed class WorkspaceDialogHost : UserControl {
    readonly List<(WorkspaceToolView Tool, IInputElement? Focus, double Width)> stack = new();
    public Action? DialogsChanged;
    readonly Border card = new() { CornerRadius = new CornerRadius(12), Padding = new Thickness(16),
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(24), MaxWidth = 680 };
    readonly ContentControl body = new();
    IDisposable? titleBinding;
    readonly TextBlock title = new() { FontSize = 16, FontWeight = FontWeight.Medium, TextWrapping = TextWrapping.Wrap };
    public WorkspaceDialogHost() {
        IsVisible = false;
        var backdrop = new Border { Background = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)) };
        card[!Border.BackgroundProperty] = new DynamicResourceExtension("md3.surface-container-low");
        card[!Border.BorderBrushProperty] = new DynamicResourceExtension("md3.outline-variant");
        card.BorderThickness = new Thickness(1);
        var close = new Button { Content = "×", Width = 32, Height = 32, Margin = new Thickness(0) };
        close.Classes.Add("toolClose");
        close[!AutomationProperties.NameProperty] = new DynamicResourceExtension("button.cancel");
        close.Click += (_, _) => CloseTop();
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 12) };
        Grid.SetColumn(close, 1);
        header.Children.Add(title); header.Children.Add(close);
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        grid.Children.Add(header); Grid.SetRow(body, 1); grid.Children.Add(body);
        card.Child = grid;
        KeyboardNavigation.SetTabNavigation(card, KeyboardNavigationMode.Cycle);
        var layer = new Grid(); layer.Children.Add(backdrop); layer.Children.Add(card); Content = layer;
        AddHandler(KeyDownEvent, (_, e) => {
            if (e.Key == Key.Escape && !e.Handled) { CloseTop(); e.Handled = true; }
        }, Avalonia.Interactivity.RoutingStrategies.Bubble);
        SizeChanged += (_, _) => card.MaxHeight = Math.Max(100, Bounds.Height - 48);
    }
    public bool HasDialogs => stack.Count > 0;
    public void Push(WorkspaceToolView tool) {
        if (stack.Exists(s => ReferenceEquals(s.Tool, tool))) return;
        stack.Add((tool, (TopLevel.GetTopLevel(this) as Window)?.FocusManager?.GetFocusedElement(),
            Math.Min(680, double.IsNaN(tool.Width) ? 420 : tool.Width + 32)));
        tool.RemoveFromHost = Remove;
        Refresh();
    }
    void Remove(WorkspaceToolView tool) {
        int index = stack.FindIndex(s => ReferenceEquals(s.Tool, tool));
        if (index < 0) return;
        bool top = index == stack.Count - 1;
        var focus = stack[index].Focus;
        stack.RemoveAt(index);
        if (top) { Refresh(); focus?.Focus(); }
    }
    void Refresh() {
        if (body.Content is ScrollViewer old) old.Content = null;
        body.Content = null;
        titleBinding?.Dispose();
        IsVisible = HasDialogs;
        DialogsChanged?.Invoke();
        if (!HasDialogs) return;
        var tool = stack[^1].Tool;
        titleBinding = title.Bind(TextBlock.TextProperty, tool.GetObservable(WorkspaceToolView.TitleProperty));
        card.Width = stack[^1].Width;
        card.MaxHeight = Math.Max(100, Bounds.Height - 48);
        tool.Width = tool.Height = double.NaN;
        body.Content = new ScrollViewer { Content = tool, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Dispatcher.UIThread.Post(() => {
            if (!tool.IsClosed && HasDialogs && stack[^1].Tool == tool)
                (tool.GetVisualDescendants().OfType<InputElement>().FirstOrDefault(c => c.Focusable && c.IsEffectivelyEnabled && c.IsVisible) ?? tool).Focus();
        });
    }
    public void CloseTop() { if (HasDialogs) stack[^1].Tool.Close(); }
    public void Shutdown() { while (HasDialogs) CloseTop(); }
}

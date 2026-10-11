using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Util;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.Styling;
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
    readonly Border backdrop = new() { Background = new SolidColorBrush(Color.FromArgb(72, 0, 0, 0)) };
    readonly Button close = new() { Width = 32, Height = 32, Margin = new Thickness(0) };
    int generation;
    bool shuttingDown, externalModal;
    IInputElement? returnFocus;
    public Control? BackgroundSurface { get; set; }
    public bool IsBlocking => HasDialogs || IsVisible;
    public bool IsBackgroundBlocked => IsBlocking || externalModal;
    public void SetExternalModalVisible(bool visible) { externalModal = visible; UpdatePresentation(); }
    void UpdatePresentation() {
        if (BackgroundSurface != null) {
            BackgroundSurface.IsEnabled = !IsBackgroundBlocked;
            BackgroundSurface.Effect = IsBackgroundBlocked ? new BlurEffect { Radius = 3 } : null;
        }
        DialogsChanged?.Invoke();
    }
    IDisposable? titleBinding;
    readonly TextBlock title = new() { FontSize = 16, FontWeight = FontWeight.Medium, TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center };
    void ConstrainBody() {
        card.MaxHeight = Math.Max(100, Bounds.Height - 48);
        if (HasDialogs) stack[^1].Tool.MaxHeight = Math.Max(100, Bounds.Height - 112);
    }
    public WorkspaceDialogHost() {
        Styles.Add(new StyleInclude(new Uri("avares://OpenUtau/")) {
            Source = new Uri("avares://OpenUtau/Styles/ToolWorkspace.axaml"),
        });
        IsVisible = false;
        backdrop.Classes.Add("md3-fade");
        card.Classes.Add("md3-pop");
        card[!Border.BackgroundProperty] = new DynamicResourceExtension("md3.surface-container-low");
        card[!Border.BorderBrushProperty] = new DynamicResourceExtension("md3.outline-variant");
        card.BorderThickness = new Thickness(1);
        var icon = new DawIcon { Width = 14, Height = 14 };
        icon[!DawIcon.DataProperty] = new DynamicResourceExtension("icon-x");
        icon[!DawIcon.FillProperty] = new DynamicResourceExtension("md3.on-surface-variant");
        close.Content = icon;
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
        SizeChanged += (_, _) => ConstrainBody();
    }
    public bool HasDialogs => stack.Count > 0;
    public void Push(WorkspaceToolView tool) {
        if (shuttingDown) { tool.Close(); return; }
        if (stack.Exists(s => ReferenceEquals(s.Tool, tool))) return;
        stack.Add((tool, (TopLevel.GetTopLevel(this) as Window)?.FocusManager?.GetFocusedElement(),
            Math.Min(680, double.IsNaN(tool.Width) ? 420 : tool.Width + 32)));
        tool.RemoveFromHost = Remove;
        Refresh(null);
    }
    void Remove(WorkspaceToolView tool) {
        int index = stack.FindIndex(s => ReferenceEquals(s.Tool, tool));
        if (index < 0) return;
        bool top = index == stack.Count - 1;
        var focus = stack[index].Focus;
        stack.RemoveAt(index);
        if (top) Refresh(focus);
    }
    void ReleaseBody() {
        if (body.Content is ScrollViewer old) old.Content = null;
        body.Content = null;
        titleBinding?.Dispose();
        titleBinding = null;
    }
    void Refresh(IInputElement? focus) {
        int version = ++generation;
        Classes.Set("no-motion", Preferences.Default.ReduceMotion);
        if (!HasDialogs) {
            card.IsEnabled = false;
            returnFocus = focus;
            card.Classes.Set("shown", false);
            backdrop.Classes.Set("shown", false);
            UpdatePresentation();
            if (shuttingDown || Preferences.Default.ReduceMotion) CompleteDismiss(version);
            else DispatcherTimer.RunOnce(() => CompleteDismiss(version), OpenUtau.Theming.Md3Motion.EffectsDefault);
            return;
        }
        returnFocus = null;
        ReleaseBody();
        card.IsEnabled = true;
        IsVisible = true;
        UpdatePresentation();
        var tool = stack[^1].Tool;
        close.IsVisible = tool is not LoadingWindow;
        titleBinding = title.Bind(TextBlock.TextProperty, tool.GetObservable(WorkspaceToolView.TitleProperty));
        card.Width = stack[^1].Width;
        card.MaxHeight = Math.Max(100, Bounds.Height - 48);
        tool.Width = tool.Height = double.NaN;
        ConstrainBody();
        body.Content = new ScrollViewer { Content = tool, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Dispatcher.UIThread.Post(() => {
            if (version != generation || tool.IsClosed || !HasDialogs || stack[^1].Tool != tool) return;
            card.Classes.Set("shown", true);
            backdrop.Classes.Set("shown", true);
            if (focus is InputElement input && input.IsEffectivelyEnabled && TopLevel.GetTopLevel(input) != null) input.Focus();
            else (tool.GetVisualDescendants().OfType<InputElement>().FirstOrDefault(c => c.Focusable && c.IsEffectivelyEnabled && c.IsVisible) ?? tool).Focus();
        });
    }
    void CompleteDismiss(int version) {
        if (version != generation || HasDialogs) return;
        ReleaseBody();
        IsVisible = false;
        UpdatePresentation();
        var focus = returnFocus;
        returnFocus = null;
        if (shuttingDown || TopLevel.GetTopLevel(this) is not Window { IsVisible: true }) return;
        if (focus is InputElement input && input.IsEffectivelyEnabled && TopLevel.GetTopLevel(input) != null) input.Focus();
        else (TopLevel.GetTopLevel(this) as Window)?.Focus();
    }
    public void CloseTop() { if (HasDialogs && stack[^1].Tool is not LoadingWindow) stack[^1].Tool.Close(); }
    public void Shutdown() {
        shuttingDown = true;
        externalModal = false;
        while (HasDialogs) stack[^1].Tool.Close();
        CompleteDismiss(++generation);
    }
}

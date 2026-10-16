using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.VisualTree;
using OpenUtau.App.ViewModels;
using OpenUtau.Core.Ustx;

namespace OpenUtau.App.Controls;

public partial class EffectRackCard : UserControl {
    FxChainRowViewModel? row;
    BuiltInEffectView? editor;
    bool dragging;
    double startX, deltaX;
    public EffectRackCard() {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachRow();
        PointerPressed += (_, e) => {
            if (e.ClickCount == 2 && row?.Kind == FxChainItemKind.Vst && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { row.Activate(); e.Handled = true; }
        };
        KeyDown += (_, e) => {
            if (e.Key == Key.Enter && row?.Kind == FxChainItemKind.Vst) { row.Activate(); e.Handled = true; }
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key is Key.Left or Key.Right) { row?.RequestMove(e.Key == Key.Left ? -1 : 1); e.Handled = true; }
        };
    }
    void AttachRow() {
        if (row != null) row.PropertyChanged -= OnRowChanged;
        editor?.Dispose(); editor = null;
        row = DataContext as FxChainRowViewModel;
        ContextFlyout = null;
        if (row == null) { EditorHost.Content = null; return; }
        row.PropertyChanged += OnRowChanged;
        Width = row.IsBuiltIn ? 306 : 224;
        var original = row;
        var menu = new MenuFlyout();
        if (!row.IsBuiltIn) {
            var replace = new MenuItem { Header = ThemeManager.GetString("rack.replace") };
            replace.Click += (_, _) => original.RequestReplacement();
            menu.Items.Add(replace);
        }
        var remove = new MenuItem { Header = ThemeManager.GetString("fxchain.remove.tip") };
        remove.Click += (_, _) => original.RemoveCommand.Execute(null);
        menu.Items.Add(remove);
        ContextFlyout = menu;
        if (row.IsBuiltIn && row.OwnerTrack != null) {
            editor = new BuiltInEffectView(row.OwnerTrack, row.EffectId, (TrackEffectKind)row.Module!.Value);
            EditorHost.Content = editor;
        } else {
            var body = new StackPanel { Spacing = 10, Margin = new Thickness(16), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            var vendor = new TextBlock { Text = row.Detail, TextWrapping = TextWrapping.Wrap };
            Md3RackKit.Paint(vendor, TextBlock.ForegroundProperty, "md3.on-surface-variant");
            body.Children.Add(vendor);
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11 };
            status.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(row.Status)));
            Md3RackKit.Paint(status, TextBlock.ForegroundProperty, "md3.on-surface-variant");
            body.Children.Add(status);
            var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
            hint[!TextBlock.TextProperty] = new DynamicResourceExtension("rack.vst.hint");
            Md3RackKit.Paint(hint, TextBlock.ForegroundProperty, "md3.on-surface-variant");
            body.Children.Add(hint);
            var open = new Button { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
            open[!Button.ContentProperty] = new DynamicResourceExtension("rack.open");
            open[!Button.ThemeProperty] = new DynamicResourceExtension("DawTextButtonTheme");
            open.Click += (_, _) => row?.Activate(); body.Children.Add(open);
            EditorHost.Content = body;
        }
        Apply();
    }
    void OnRowChanged(object? sender, PropertyChangedEventArgs e) => Apply();
    void Apply() {
        if (row == null) return;
        if (row.IsBuiltIn) NameText[!TextBlock.TextProperty] = new DynamicResourceExtension(row.NameKey); else NameText.Text = row.Name;
        ToolTip.SetTip(NameText, NameText.Text);
        AutomationProperties.SetName(this, NameText.Text ?? row.Name);
        if (row.IsBuiltIn) KindText[!TextBlock.TextProperty] = new DynamicResourceExtension("fxchain.badge.builtin"); else KindText.Text = row.BadgeText;
        EditorHost.Opacity = row.IsMuted ? .55 : 1;
        PowerToggle.IsChecked = row.IsPowered;
    }
    void OnPower(object? sender, RoutedEventArgs e) { row?.RequestPower(PowerToggle.IsChecked == true); Apply(); }
    void OnGripPressed(object? sender, PointerPressedEventArgs e) {
        if ((e.Source is Control c && (c is ToggleButton || c.GetVisualAncestors().Any(a => a is ToggleButton))) || e.ClickCount > 1 || row?.CanReorder != true || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        dragging = true; startX = e.GetPosition(TopLevel.GetTopLevel(this)).X; deltaX = 0; e.Pointer.Capture(Grip);
    }
    void OnGripMoved(object? sender, PointerEventArgs e) { if (dragging) deltaX = e.GetPosition(TopLevel.GetTopLevel(this)).X - startX; }
    void OnGripReleased(object? sender, PointerReleasedEventArgs e) { if (!dragging) return; FinishDrag(); e.Pointer.Capture(null); e.Handled = true; }
    void OnGripLost(object? sender, PointerCaptureLostEventArgs e) => FinishDrag();
    void FinishDrag() {
        if (!dragging) return;
        dragging = false;
        if (row == null) return;
        int steps = (int)Math.Round(deltaX / Math.Max(1, Bounds.Width + 12));
        var root = TopLevel.GetTopLevel(this);
        var items = this.GetVisualAncestors().OfType<ItemsControl>().FirstOrDefault();
        if (root != null && items != null) {
            double dropX = startX + deltaX;
            foreach (var card in items.GetVisualDescendants().OfType<EffectRackCard>()) {
                var point = card.TranslatePoint(default, root);
                if (point.HasValue && dropX >= point.Value.X && dropX <= point.Value.X + card.Bounds.Width && card.row != null) {
                    steps = card.row.Order - row.Order;
                    break;
                }
            }
        }
        if (steps != 0) row.RequestMove(steps);
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); if (row == null) AttachRow(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
        dragging = false;
        if (row != null) row.PropertyChanged -= OnRowChanged;
        editor?.Dispose(); editor = null; row = null; EditorHost.Content = null;
        base.OnDetachedFromVisualTree(e);
    }
}

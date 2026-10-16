using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
namespace UTvTU.DawUI.Controls;
/// <summary>Ready-to-use MD3 skin over the same fader input and gesture contract used by the mixer.</summary>
public sealed class DawVerticalFader : DawFader {
    readonly Border track = new() { Width = 4, CornerRadius = new CornerRadius(2), IsHitTestVisible = false };
    readonly Border active = new() { Width = 4, CornerRadius = new CornerRadius(2), IsHitTestVisible = false };
    readonly Border zero = new() { Width = 16, Height = 1, IsHitTestVisible = false };
    public DawVerticalFader() {
        Width = 56; Height = 190; MinHeight = 56;
        this[!FocusBrushProperty] = new DynamicResourceExtension("md3.primary");
        track[!Border.BackgroundProperty] = new DynamicResourceExtension("md3.surface-container-highest");
        active[!Border.BackgroundProperty] = new DynamicResourceExtension("md3.primary");
        zero[!Border.BackgroundProperty] = new DynamicResourceExtension("md3.on-surface-variant");
        Children.Add(track); Children.Add(active); Children.Add(zero);
        var thumb = new Border { Width = 28, Height = 18, CornerRadius = new CornerRadius(6), IsHitTestVisible = false };
        thumb[!Border.BackgroundProperty] = new DynamicResourceExtension("md3.primary");
        var grip = new Border { Width = 12, Height = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        grip[!Border.BackgroundProperty] = new DynamicResourceExtension("md3.on-primary");thumb.Child = grip;
        Children.Add(thumb);Handle = thumb;ArrangeSkin();
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty || change.Property == ValueProperty || change.Property == MinimumProperty || change.Property == MaximumProperty) ArrangeSkin();
    }
    void ArrangeSkin() {
        if (track == null || Handle == null || Bounds.Height <= 0 || Maximum <= Minimum) return;
        double center = Bounds.Width / 2, travel = Math.Max(0, Bounds.Height - Handle.Height);
        SetLeft(track, center - 2);SetTop(track, Handle.Height / 2);track.Height = travel;
        double y = GetTop(Handle) + Handle.Height / 2;
        SetLeft(active, center - 2);SetTop(active, y);active.Height = Math.Max(0, Bounds.Height - Handle.Height / 2 - y);
        SetLeft(zero, center - 8);SetTop(zero, Handle.Height / 2 + travel * (Maximum - Math.Clamp(0, Minimum, Maximum)) / (Maximum - Minimum));
        SetLeft(Handle, center - Handle.Width / 2);UpdateHandle();
    }
}

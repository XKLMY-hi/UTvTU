using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.App.Controls;

/// <summary>Only realized rows decode a small avatar; recycling or detaching releases it.</summary>
public sealed class SingerAvatar : UserControl {
    public static readonly StyledProperty<USinger?> SingerProperty =
        AvaloniaProperty.Register<SingerAvatar, USinger?>(nameof(Singer));
    public static readonly StyledProperty<int> RevisionProperty =
        AvaloniaProperty.Register<SingerAvatar, int>(nameof(Revision));
    readonly Image image = new() { Stretch = Stretch.UniformToFill };
    readonly DawIcon fallback = new() { Width = 20, Height = 20 };
    Bitmap? bitmap;
    bool attached;

    public USinger? Singer { get => GetValue(SingerProperty); set => SetValue(SingerProperty, value); }
    public int Revision { get => GetValue(RevisionProperty); set => SetValue(RevisionProperty, value); }

    public SingerAvatar() {
        IsHitTestVisible = false;
        fallback[!DawIcon.DataProperty] = new DynamicResourceExtension("icon-mic");
        fallback[!DawIcon.FillProperty] = new DynamicResourceExtension("md3.primary");
        Content = new Grid { Children = { fallback, image } };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
        base.OnAttachedToVisualTree(e);
        attached = true;
        ReloadAvatar();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
        attached = false;
        ReleaseAvatar();
        base.OnDetachedFromVisualTree(e);
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
        base.OnPropertyChanged(change);
        if (attached && (change.Property == SingerProperty || change.Property == RevisionProperty)) ReloadAvatar();
    }
    void ReloadAvatar() {
        Bitmap? next = null;
        try {
            // Metadata is sufficient: never EnsureLoaded() a voicebank just to draw a row.
            if (Singer?.AvatarData is { Length: > 0 } data) {
                using var stream = new MemoryStream(data, writable: false);
                next = Bitmap.DecodeToWidth(stream, 72);
            } else if (Singer?.Avatar is { Length: > 0 } path && File.Exists(path)) {
                using var stream = File.OpenRead(path);
                next = Bitmap.DecodeToWidth(stream, 72);
            }
        } catch (Exception e) {
            Log.Warning(e, "Failed to load voicebank thumbnail {SingerId}", Singer?.Id);
        }
        var previous = bitmap;
        bitmap = next;
        image.Source = next;
        image.IsVisible = next != null;
        fallback.IsVisible = next == null;
        previous?.Dispose();
    }
    void ReleaseAvatar() {
        image.Source = null;
        image.IsVisible = false;
        fallback.IsVisible = true;
        bitmap?.Dispose();
        bitmap = null;
    }
}

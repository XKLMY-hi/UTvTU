using System;
using System.IO;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
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
    SingerThumbnailCache.Lease? lease;
    CancellationTokenSource? loading;
    int generation;
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
    async void ReloadAvatar() {
        ReleaseAvatar();
        int request = generation;
        var cancellation = new CancellationTokenSource();
        loading = cancellation;
        try {
            // Metadata is sufficient: never EnsureLoaded() a voicebank just to draw a row.
            var next = await SingerThumbnailCache.AcquireAsync(Singer?.AvatarData, Singer?.Avatar, Revision, cancellation.Token).ConfigureAwait(false);
            Dispatcher.UIThread.Post(() => {
                if (!attached || request != generation) { next?.Dispose(); return; }
                lease = next;
                image.Source = next?.Bitmap;
                image.IsVisible = next != null;
                fallback.IsVisible = next == null;
            });
        } catch (OperationCanceledException) {
        } catch (Exception e) {
            Log.Warning(e, "Failed to load voicebank thumbnail {SingerId}", Singer?.Id);
        }
    }
    void ReleaseAvatar() {
        generation++;
        loading?.Cancel();
        loading?.Dispose();
        loading = null;
        image.Source = null;
        image.IsVisible = false;
        fallback.IsVisible = true;
        lease?.Dispose();
        lease = null;
    }
}

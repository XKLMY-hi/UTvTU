using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using OpenUtau.Theming;

namespace OpenUtau.App.Controls;

/// <summary>A keyboard-accessible transport button; playback commands remain in the host.</summary>
public sealed class PlayPauseButton : Button {
    public static readonly StyledProperty<bool> IsPlayingProperty =
        AvaloniaProperty.Register<PlayPauseButton, bool>(nameof(IsPlaying));
    readonly PlayPauseGlyph glyph = new() { Width = 18, Height = 18 };
    protected override Type StyleKeyOverride => typeof(Button);
    public bool IsPlaying { get => GetValue(IsPlayingProperty); set => SetValue(IsPlayingProperty, value); }
    public PlayPauseButton() {
        Classes.Add("expressivePlay");
        Content = glyph;
        glyph.Bind(PlayPauseGlyph.FillProperty, this.GetObservable(ForegroundProperty));
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
        base.OnPropertyChanged(change);
        if (change.Property == IsPlayingProperty) {
            PseudoClasses.Set(":playing", IsPlaying);
            glyph.IsPlaying = IsPlaying;
        }
    }
}

internal sealed class PlayPauseGlyph : Control {
    internal static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<PlayPauseGlyph, IBrush?>("Fill");
    internal static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<PlayPauseGlyph, double>("Progress");
    bool playing;
    bool attached;
    CancellationTokenSource? animation;
    internal bool IsPlaying {
        get => playing;
        set {
            double current = GetValue(ProgressProperty);
            playing = value;
            Stop();
            double target = value ? 1 : 0;
            SetValue(ProgressProperty, target);
            if (attached && !Md3Motion.GetReduced(this) && current != target) {
                var source = new CancellationTokenSource();
                animation = source;
                var motion = new Animation {
                    Duration = Md3StandardSpringEasing.SettlingDuration(800, .6),
                    Easing = new Md3StandardSpringEasing { Stiffness = 800, Damping = .6 },
                    Children = {
                        new KeyFrame { Cue = new Cue(0), Setters = { new Setter(ProgressProperty, current) } },
                        new KeyFrame { Cue = new Cue(1), Setters = { new Setter(ProgressProperty, target) } },
                    },
                };
                _ = Run(motion, source);
            }
        }
    }
    public PlayPauseGlyph() {
        IsHitTestVisible = false;
        Focusable = false;
    }
    static PlayPauseGlyph() => AffectsRender<PlayPauseGlyph>(FillProperty, ProgressProperty);
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
        base.OnPropertyChanged(change);
        if (change.Property == Md3Motion.ReducedProperty) {
            Stop();
            SetValue(ProgressProperty, playing ? 1 : 0);
        }
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
        base.OnAttachedToVisualTree(e);
        attached = true;
        SetValue(ProgressProperty, playing ? 1 : 0);
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
        attached = false;
        Stop();
        base.OnDetachedFromVisualTree(e);
    }
    void Stop() { animation?.Cancel(); animation?.Dispose(); animation = null; }
    async Task Run(Animation motion, CancellationTokenSource source) {
        try { await motion.RunAsync(this, source.Token); }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(animation, source)) { animation = null; source.Dispose(); } }
    }
    public override void Render(DrawingContext context) {
        var fill = GetValue(FillProperty);
        if (fill == null) return;
        double t = GetValue(ProgressProperty);
        Point Mix(double ax, double ay, double bx, double by) =>
            new((ax + (bx - ax) * t) * Bounds.Width / 16, (ay + (by - ay) * t) * Bounds.Height / 16);
        void Draw(Point a, Point b, Point c, Point d) {
            var path = new StreamGeometry();
            using (var pen = path.Open()) {
                pen.BeginFigure(a, true); pen.LineTo(b); pen.LineTo(c); pen.LineTo(d); pen.EndFigure(true);
            }
            context.DrawGeometry(fill, null, path);
        }
        Draw(Mix(4, 2, 3, 2), Mix(8, 4.4, 6, 2), Mix(8, 11.6, 6, 14), Mix(4, 14, 3, 14));
        Draw(Mix(8, 4.4, 10, 2), Mix(14, 8, 13, 2), Mix(14, 8, 13, 14), Mix(8, 11.6, 10, 14));
    }
}

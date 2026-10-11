using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using OpenUtau.Theming;

namespace OpenUtau.App.Controls;

/// <summary>
/// Source-derived decorative geometry with finite, interruptible state changes.
/// Only an indeterminate busy state repeats. Avalonia pauses its clock while hidden;
/// detaching or enabling reduced motion cancels it. No timer or document subscription.
/// Matching positive polar radii keeps this selected set of shapes free of crossings.
/// This adapter is not AndroidX's general-purpose feature-matching Morph algorithm.
/// </summary>
public sealed class MaterialMorph : Control {
    public static readonly StyledProperty<MaterialShapeKind> KindProperty =
        AvaloniaProperty.Register<MaterialMorph, MaterialShapeKind>(nameof(Kind), MaterialShapeKind.Cookie7);
    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<MaterialMorph, IBrush?>(nameof(Fill));
    public static readonly StyledProperty<bool> IsIndeterminateProperty =
        AvaloniaProperty.Register<MaterialMorph, bool>(nameof(IsIndeterminate));
    internal static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<MaterialMorph, double>("Progress", 1);
    internal static readonly StyledProperty<double> PhaseProperty =
        AvaloniaProperty.Register<MaterialMorph, double>("Phase");
    static readonly MaterialShapeKind[] busyShapes = {
        MaterialShapeKind.Cookie7, MaterialShapeKind.Circle,
        MaterialShapeKind.Sunny, MaterialShapeKind.Square,
    };
    CancellationTokenSource? animation;
    Style? loopStyle;
    bool attached;
    double[] from, target;
    Geometry? rendered;

    public MaterialMorph() {
        IsHitTestVisible = false;
        Focusable = false;
        from = target = MaterialShapeProfiles.Radii[(int)Kind];
    }
    static MaterialMorph() => AffectsRender<MaterialMorph>(FillProperty);
    public MaterialShapeKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public bool IsIndeterminate { get => GetValue(IsIndeterminateProperty); set => SetValue(IsIndeterminateProperty, value); }
    internal bool HasActiveAnimation => animation != null || Classes.Contains("morph-busy");
    protected override Size MeasureOverride(Size availableSize) => new(96, 96);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
        base.OnAttachedToVisualTree(e);
        attached = true;
        Reset();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
        attached = false;
        Stop();
        base.OnDetachedFromVisualTree(e);
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
        base.OnPropertyChanged(change);
        if (change.Property == KindProperty) {
            var current = CurrentRadii();
            Stop();
            from = current;
            target = MaterialShapeProfiles.Radii[(int)Kind];
            if (attached && !Md3Motion.GetReduced(this) && !IsIndeterminate) {
                SetCurrentValue(ProgressProperty, 0);
                Start(ProgressProperty, 0, 1,
                    Md3StandardSpringEasing.SettlingDuration(380, .8),
                    new Md3StandardSpringEasing { Stiffness = 380, Damping = .8 }, false);
            } else Reset();
        } else if (change.Property == IsIndeterminateProperty || change.Property == Md3Motion.ReducedProperty) {
            Reset();
        }
        if (change.Property == ProgressProperty || change.Property == PhaseProperty ||
            change.Property == KindProperty || change.Property == BoundsProperty) {
            rendered = null;
            InvalidateVisual();
        }
    }
    void Reset() {
        Stop();
        from = target = MaterialShapeProfiles.Radii[(int)Kind];
        SetCurrentValue(ProgressProperty, 1);
        SetCurrentValue(PhaseProperty, 0);
        rendered = null;
        InvalidateVisual();
        if (attached && IsIndeterminate && !Md3Motion.GetReduced(this)) {
            Start(PhaseProperty, 0, 4, TimeSpan.FromSeconds(3.2), new LinearEasing(), true);
        }
    }
    void Stop() {
        Classes.Remove("morph-busy");
        animation?.Cancel();
        animation?.Dispose();
        animation = null;
    }
    void Start(StyledProperty<double> property, double start, double end, TimeSpan duration, Easing easing, bool repeat) {
        var motion = new Animation {
            Duration = duration, Easing = easing,
            IterationCount = repeat ? IterationCount.Infinite : new IterationCount(1),
            PlaybackBehavior = PlaybackBehavior.OnlyIfVisible,
            FillMode = FillMode.None,
            Children = {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(property, start) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(property, end) } },
            },
        };
        if (repeat) {
            // Avalonia 12 exposes looping animation through styles, not RunAsync.
            // Removing the selector class disposes its animation subscription.
            if (loopStyle == null) {
                loopStyle = new Style(selector => selector.OfType<MaterialMorph>().Class("morph-busy"));
                loopStyle.Animations.Add(motion);
                Styles.Add(loopStyle);
            }
            Classes.Add("morph-busy");
            return;
        }
        var source = new CancellationTokenSource();
        animation = source;
        _ = Run(motion, source, property, end);
    }
    async Task Run(Animation motion, CancellationTokenSource source, StyledProperty<double> property, double end) {
        try {
            await motion.RunAsync(this, source.Token);
            if (ReferenceEquals(animation, source)) SetCurrentValue(property, end);
        } catch (OperationCanceledException) {
            // Cancellation is expected on interruption, detach or preference changes.
        } finally {
            if (ReferenceEquals(animation, source)) { animation = null; source.Dispose(); }
        }
    }
    double[] CurrentRadii() {
        double[] a = from, b = target;
        double progress = Math.Clamp(GetValue(ProgressProperty), 0, 1);
        if (IsIndeterminate && !Md3Motion.GetReduced(this)) {
            double phase = GetValue(PhaseProperty) % 4;
            int index = (int)phase;
            a = MaterialShapeProfiles.Radii[(int)busyShapes[index]];
            b = MaterialShapeProfiles.Radii[(int)busyShapes[(index + 1) % 4]];
            double t = phase - index;
            progress = t * t * (3 - 2 * t);
        }
        var radii = new double[MaterialShapeProfiles.Count];
        for (int i = 0; i < radii.Length; i++) radii[i] = a[i] + (b[i] - a[i]) * progress;
        return radii;
    }
    public override void Render(DrawingContext context) {
        base.Render(context);
        if (Fill == null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        bool busy = IsIndeterminate && !Md3Motion.GetReduced(this);
        if (rendered == null) {
            if (!busy && GetValue(ProgressProperty) >= 1) rendered = MaterialShape.Source(Kind);
            else {
                var radii = CurrentRadii();
                var path = new StreamGeometry();
                using (var pen = path.Open()) {
                    for (int i = 0; i < radii.Length; i++) {
                        double angle = -Math.PI / 2 + i * Math.PI * 2 / radii.Length;
                        var p = new Point(50 + Math.Cos(angle) * radii[i], 50 + Math.Sin(angle) * radii[i]);
                        if (i == 0) pen.BeginFigure(p, true); else pen.LineTo(p);
                    }
                    pen.EndFigure(true);
                }
                rendered = path;
            }
        }
        // A fixed inset contains the full rotating silhouette without changing layout.
        double scale = Math.Min(Bounds.Width, Bounds.Height) / 100 * .84;
        double rotation = busy ? GetValue(PhaseProperty) * Math.PI / 2 : 0;
        var matrix = Matrix.CreateTranslation(-50, -50) * Matrix.CreateRotation(rotation) *
            Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(Bounds.Width / 2, Bounds.Height / 2);
        using (context.PushTransform(matrix)) context.DrawGeometry(Fill, null, rendered);
    }
}

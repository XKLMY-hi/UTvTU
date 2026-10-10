using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OpenUtau.App.Controls {
    /// <summary>
    /// Centers the actual vector ink in a fixed icon cell, including paths with
    /// nonzero origins or nonsquare bounds. Source geometry is never mutated.
    /// Stroke thickness is in DIP and stays consistent across source coordinate systems.
    /// Branding and explicit SVG viewBox artwork use their own original renderers.
    /// </summary>
    public sealed class DawIcon : Control {
        public static readonly StyledProperty<Geometry?> DataProperty =
            AvaloniaProperty.Register<DawIcon, Geometry?>(nameof(Data));
        public static readonly StyledProperty<IBrush?> FillProperty =
            AvaloniaProperty.Register<DawIcon, IBrush?>(nameof(Fill));
        public static readonly StyledProperty<IBrush?> StrokeProperty =
            AvaloniaProperty.Register<DawIcon, IBrush?>(nameof(Stroke));
        public static readonly StyledProperty<double> StrokeThicknessProperty =
            AvaloniaProperty.Register<DawIcon, double>(nameof(StrokeThickness), 1.5,
                validate: value => double.IsFinite(value) && value >= 0);
        public static readonly StyledProperty<PenLineCap> StrokeLineCapProperty =
            AvaloniaProperty.Register<DawIcon, PenLineCap>(nameof(StrokeLineCap), PenLineCap.Round);
        public static readonly StyledProperty<PenLineJoin> StrokeJoinProperty =
            AvaloniaProperty.Register<DawIcon, PenLineJoin>(nameof(StrokeJoin), PenLineJoin.Round);

        Geometry? rendered;

        static DawIcon() {
            AffectsRender<DawIcon>(DataProperty, FillProperty, StrokeProperty, StrokeThicknessProperty,
                StrokeLineCapProperty, StrokeJoinProperty);
        }

        public DawIcon() {
            IsHitTestVisible = false;
            Focusable = false;
        }

        public Geometry? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }
        public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
        public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
        public double StrokeThickness { get => GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }
        public PenLineCap StrokeLineCap { get => GetValue(StrokeLineCapProperty); set => SetValue(StrokeLineCapProperty, value); }
        public PenLineJoin StrokeJoin { get => GetValue(StrokeJoinProperty); set => SetValue(StrokeJoinProperty, value); }

        Pen? Outline => Stroke == null ? null : new Pen(Stroke, StrokeThickness,
            lineCap: StrokeLineCap, lineJoin: StrokeJoin);

        /// <summary>The bounds actually drawn inside the cell, including any outline.</summary>
        public Rect RenderedInkBounds {
            get {
                var geometry = GetGeometry();
                var pen = Outline;
                return geometry == null ? default : pen == null ? geometry.Bounds : geometry.GetRenderBounds(pen);
            }
        }

        protected override Size MeasureOverride(Size availableSize) => new Size(16, 16);

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == DataProperty || change.Property == BoundsProperty ||
                change.Property == StrokeProperty || change.Property == StrokeThicknessProperty ||
                change.Property == StrokeLineCapProperty || change.Property == StrokeJoinProperty) {
                rendered = null;
            }
        }

        Geometry? GetGeometry() {
            if (rendered != null) return rendered;
            if (Data == null || Bounds.Width <= 0 || Bounds.Height <= 0) return null;
            var source = Data.Bounds;
            double outline = Stroke == null ? 0 : StrokeThickness;
            double width = Math.Max(0, Bounds.Width - outline);
            double height = Math.Max(0, Bounds.Height - outline);
            double scale = Math.Min(source.Width > 0 ? width / source.Width : double.PositiveInfinity,
                source.Height > 0 ? height / source.Height : double.PositiveInfinity);
            if (!double.IsFinite(scale) || scale <= 0) return null;
            var matrix = Matrix.CreateTranslation(-source.X, -source.Y) * Matrix.CreateScale(scale, scale) *
                Matrix.CreateTranslation((Bounds.Width - source.Width * scale) / 2,
                    (Bounds.Height - source.Height * scale) / 2);
            rendered = Data.Clone();
            rendered.Transform = new MatrixTransform((Data.Transform?.Value ?? Matrix.Identity) * matrix);
            return rendered;
        }

        public override void Render(DrawingContext context) {
            base.Render(context);
            var geometry = GetGeometry();
            if (geometry != null) context.DrawGeometry(Fill, Outline, geometry);
        }
    }
}

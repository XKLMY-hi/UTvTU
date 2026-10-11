using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OpenUtau.App.Controls {
    public enum MaterialShapeKind {
        Arch,
        Square,
    }

    /// <summary>
    /// Decorative Material shape, centered without changing its aspect ratio.
    /// These cubic paths are derived from AndroidX MaterialShapes/RoundedPolygon:
    /// Arch = four vertices, rounding [1, 1, .2, .2], rotated -135 degrees;
    /// Square = unit rectangle, rounding .3. Both use zero smoothing.
    /// Source parameters, the circular-arc calculation and provenance are recorded
    /// in .opencode/design/md3-rebuild-2026-10-11/geometry.json.
    /// No raster tracing, brand geometry, theme subscriptions or animation timers.
    /// </summary>
    public sealed class MaterialShape : Control {
        // AndroidX source: Copyright 2024 The Android Open Source Project,
        // Apache License 2.0: https://www.apache.org/licenses/LICENSE-2.0
        public const string ArchPath = "M 0 50 C 0 22.385762508 22.385762508 0 50 0 L 50 0 C 77.614237492 0 100 22.385762508 100 50 L 100 85.857864376 C 100 93.668350211 93.668350211 100 85.857864376 100 L 14.142135624 100 C 6.331649789 100 0 93.668350211 0 85.857864376 Z";
        public const string SquarePath = "M 0 30 C 0 13.431457505 13.431457505 0 30 0 L 70 0 C 86.568542495 0 100 13.431457505 100 30 L 100 70 C 100 86.568542495 86.568542495 100 70 100 L 30 100 C 13.431457505 100 0 86.568542495 0 70 Z";

        public static readonly StyledProperty<MaterialShapeKind> KindProperty =
            AvaloniaProperty.Register<MaterialShape, MaterialShapeKind>(nameof(Kind));
        public static readonly StyledProperty<IBrush?> FillProperty =
            AvaloniaProperty.Register<MaterialShape, IBrush?>(nameof(Fill));

        static readonly Geometry arch = Geometry.Parse(ArchPath);
        static readonly Geometry square = Geometry.Parse(SquarePath);
        Geometry? rendered;

        static MaterialShape() {
            AffectsRender<MaterialShape>(KindProperty, FillProperty);
        }

        public MaterialShape() {
            IsHitTestVisible = false;
            Focusable = false;
        }

        public MaterialShapeKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
        public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }

        protected override Size MeasureOverride(Size availableSize) => new Size(64, 64);

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == KindProperty || change.Property == BoundsProperty) {
                rendered = null;
            }
        }

        public override void Render(DrawingContext context) {
            base.Render(context);
            if (Bounds.Width <= 0 || Bounds.Height <= 0 || Fill == null) return;
            if (rendered == null) {
                double scale = Math.Min(Bounds.Width, Bounds.Height) / 100;
                if (!double.IsFinite(scale) || scale <= 0) return;
                rendered = (Kind == MaterialShapeKind.Square ? square : arch).Clone();
                rendered.Transform = new MatrixTransform(Matrix.CreateScale(scale, scale) *
                    Matrix.CreateTranslation((Bounds.Width - 100 * scale) / 2,
                        (Bounds.Height - 100 * scale) / 2));
            }
            context.DrawGeometry(Fill, null, rendered);
        }
    }
}

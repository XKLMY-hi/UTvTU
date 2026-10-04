using System;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using OpenUtau.Core.Theming;
using OpenUtau.Theming;

namespace OpenUtau.App.Controls {
    /// <summary>
    /// 旋钮（MD3 自研控件语言，2026-10 实时效果机架）。
    ///
    /// 交互：纵向拖动（Shift 微调）、滚轮、方向键（Home/End 到端点）、双击回到
    /// <see cref="DefaultValue"/>。扫过角 270°，从 7:30（Minimum）到 4:30（Maximum）。
    ///
    /// 视觉：MD3 语言——底轨圆弧 + 强调色数值圆弧 + 圆形旋帽 + 指针线，**无拟物渐变、
    /// 无阴影**，全部是实色描边。双极性量程（如 ±12 dB）时数值圆弧从 0 点画起，
    /// 一眼看出是"衰减还是提升"。
    ///
    /// 取色：画刷属性为 null 时回退到 MD3 颜色池（<see cref="ColorPool"/>），
    /// 因此本控件**不含任何字面量色值**；XAML 侧照常可用
    /// <c>{DynamicResource md3.*}</c> 覆盖。
    /// </summary>
    public class Knob : RangeBase {
        /// <summary>双击复位与"原点"参考值。</summary>
        public static readonly StyledProperty<double> DefaultValueProperty =
            AvaloniaProperty.Register<Knob, double>(nameof(DefaultValue));

        /// <summary>底轨圆弧（未填充部分）。</summary>
        public static readonly StyledProperty<IBrush?> TrackBrushProperty =
            AvaloniaProperty.Register<Knob, IBrush?>(nameof(TrackBrush));

        /// <summary>数值圆弧（已填充部分）与焦点环。</summary>
        public static readonly StyledProperty<IBrush?> ArcBrushProperty =
            AvaloniaProperty.Register<Knob, IBrush?>(nameof(ArcBrush));

        /// <summary>旋帽填充。</summary>
        public static readonly StyledProperty<IBrush?> CapBrushProperty =
            AvaloniaProperty.Register<Knob, IBrush?>(nameof(CapBrush));

        /// <summary>旋帽描边。</summary>
        public static readonly StyledProperty<IBrush?> CapBorderBrushProperty =
            AvaloniaProperty.Register<Knob, IBrush?>(nameof(CapBorderBrush));

        /// <summary>指针线。</summary>
        public static readonly StyledProperty<IBrush?> PointerBrushProperty =
            AvaloniaProperty.Register<Knob, IBrush?>(nameof(PointerBrush));

        public double DefaultValue {
            get => GetValue(DefaultValueProperty);
            set => SetValue(DefaultValueProperty, value);
        }
        public IBrush? TrackBrush {
            get => GetValue(TrackBrushProperty);
            set => SetValue(TrackBrushProperty, value);
        }
        public IBrush? ArcBrush {
            get => GetValue(ArcBrushProperty);
            set => SetValue(ArcBrushProperty, value);
        }
        public IBrush? CapBrush {
            get => GetValue(CapBrushProperty);
            set => SetValue(CapBrushProperty, value);
        }
        public IBrush? CapBorderBrush {
            get => GetValue(CapBorderBrushProperty);
            set => SetValue(CapBorderBrushProperty, value);
        }
        public IBrush? PointerBrush {
            get => GetValue(PointerBrushProperty);
            set => SetValue(PointerBrushProperty, value);
        }

        const double SweepDegrees = 270;
        // 全量程所需纵向拖动像素。
        const double DragPixels = 200;
        const double FineFactor = 0.1;
        // 键盘/滚轮单步 = 量程的 1/100。
        const double StepFraction = 1.0 / 100.0;
        // 圆弧采样段数（每 90° 12 段，肉眼无折角）。
        const int SegmentsPerQuarter = 12;

        double lastY;
        bool dragging;

        public Knob() {
            // 焦点环随焦点变化重绘（Avalonia 12 的 OnGotFocus/OnLostFocus 签名与旧版不同，走事件更稳）。
            GotFocus += (_, _) => InvalidateVisual();
            LostFocus += (_, _) => InvalidateVisual();
        }

        static Knob() {
            AffectsRender<Knob>(ValueProperty, MinimumProperty, MaximumProperty,
                TrackBrushProperty, ArcBrushProperty, CapBrushProperty,
                CapBorderBrushProperty, PointerBrushProperty, IsEnabledProperty);
            FocusableProperty.OverrideDefaultValue<Knob>(true);
            CursorProperty.OverrideDefaultValue<Knob>(new Cursor(StandardCursorType.SizeNorthSouth));
        }

        double Range => Math.Max(0, Maximum - Minimum);

        void Nudge(double delta) {
            Value = Math.Clamp(Value + delta, Minimum, Maximum);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e) {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) {
                return;
            }
            if (e.ClickCount == 2) {
                Value = Math.Clamp(DefaultValue, Minimum, Maximum);
            } else {
                dragging = true;
                lastY = e.GetPosition(this).Y;
                e.Pointer.Capture(this);
            }
            Focus();
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e) {
            base.OnPointerMoved(e);
            if (!dragging) {
                return;
            }
            double y = e.GetPosition(this).Y;
            double scale = Range / DragPixels;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) {
                scale *= FineFactor;
            }
            Nudge((lastY - y) * scale);
            lastY = y;
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e) {
            base.OnPointerReleased(e);
            if (dragging) {
                dragging = false;
                e.Pointer.Capture(null);
                e.Handled = true;
            }
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) {
            base.OnPointerCaptureLost(e);
            dragging = false;
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e) {
            base.OnPointerWheelChanged(e);
            Nudge(e.Delta.Y * StepAmount(e.KeyModifiers));
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            double step = StepAmount(e.KeyModifiers);
            switch (e.Key) {
                case Key.Up:
                case Key.Right:
                    Nudge(step);
                    e.Handled = true;
                    break;
                case Key.Down:
                case Key.Left:
                    Nudge(-step);
                    e.Handled = true;
                    break;
                case Key.PageUp:
                    Nudge(step * 10);
                    e.Handled = true;
                    break;
                case Key.PageDown:
                    Nudge(-step * 10);
                    e.Handled = true;
                    break;
                case Key.Home:
                    Value = Minimum;
                    e.Handled = true;
                    break;
                case Key.End:
                    Value = Maximum;
                    e.Handled = true;
                    break;
            }
        }

        double StepAmount(KeyModifiers modifiers) {
            double step = Range * StepFraction;
            return modifiers.HasFlag(KeyModifiers.Shift) ? step * FineFactor : step;
        }

        // ── 取色：属性为 null 时回退颜色池（无字面量色值） ──
        IBrush Brush(IBrush? brush, Md3Role role) => brush ?? ColorPool.Current.Brush(role);

        public override void Render(DrawingContext context) {
            double size = Math.Min(Bounds.Width, Bounds.Height);
            if (size <= 14) {
                return;
            }
            if (!IsEnabled) {
                using (context.PushOpacity(0.38)) {
                    RenderKnob(context, size);
                }
                return;
            }
            RenderKnob(context, size);
        }

        void RenderKnob(DrawingContext context, double size) {
            var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
            double thickness = Math.Max(3, size / 16);
            double arcRadius = size / 2 - thickness / 2 - 1;
            double capRadius = Math.Max(4, arcRadius - thickness - 3);

            double t = Range > 0 ? Math.Clamp((Value - Minimum) / Range, 0, 1) : 0;
            // 双极性量程以 0 为原点，单极性以 Minimum 为原点。
            double origin = Minimum < 0 && Maximum > 0 ? -Minimum / Range : 0;

            var trackPen = new Pen(Brush(TrackBrush, Md3Role.SurfaceContainerHighest), thickness) {
                LineCap = PenLineCap.Round,
            };
            context.DrawGeometry(null, trackPen, Arc(center, arcRadius, 0, 1));

            if (Math.Abs(t - origin) > 1e-4) {
                var arcPen = new Pen(Brush(ArcBrush, Md3Role.Primary), thickness) {
                    LineCap = PenLineCap.Round,
                };
                context.DrawGeometry(null, arcPen, Arc(center, arcRadius, origin, t));
            }

            context.DrawEllipse(
                Brush(CapBrush, Md3Role.SurfaceContainerHigh),
                new Pen(Brush(CapBorderBrush, Md3Role.OutlineVariant), 1),
                center, capRadius, capRadius);

            var pointerPen = new Pen(Brush(PointerBrush, Md3Role.OnSurface), Math.Max(2, thickness * 0.5)) {
                LineCap = PenLineCap.Round,
            };
            context.DrawLine(pointerPen,
                PointAt(center, capRadius * 0.25, t),
                PointAt(center, capRadius * 0.94, t));

            if (IsFocused) {
                var focusPen = new Pen(Brush(ArcBrush, Md3Role.Primary), 2) {
                    LineCap = PenLineCap.Round,
                    DashStyle = new DashStyle(new[] { 2.0, 2.0 }, 0),
                };
                context.DrawEllipse(null, focusPen, center, arcRadius + thickness * 0.5, arcRadius + thickness * 0.5);
            }
        }

        /// <summary>从 <paramref name="t0"/> 到 <paramref name="t1"/> 的扫过圆弧（t ∈ [0,1]）。</summary>
        static StreamGeometry Arc(Point center, double radius, double t0, double t1) {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open()) {
                int n = Math.Max(2, (int)Math.Ceiling(Math.Abs(t1 - t0) * SegmentsPerQuarter * 4));
                ctx.BeginFigure(PointAt(center, radius, t0), false);
                for (int i = 1; i <= n; i++) {
                    ctx.LineTo(PointAt(center, radius, t0 + (t1 - t0) * i / n));
                }
                ctx.EndFigure(false);
            }
            return geometry;
        }

        // t ∈ [0,1] 处的点，0 = 7:30，1 = 4:30。
        static Point PointAt(Point center, double radius, double t) {
            double rad = (-SweepDegrees / 2 + SweepDegrees * t) * Math.PI / 180;
            return new Point(center.X + Math.Sin(rad) * radius, center.Y - Math.Cos(rad) * radius);
        }
    }
}

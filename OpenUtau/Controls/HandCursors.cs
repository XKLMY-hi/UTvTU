using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace OpenUtau.App.Controls {
    /// <summary>
    /// 可拖拽物的"张开手（grab）/ 握拳（grabbing）"光标（上游 `9caec1a6`）：
    /// Avalonia 的标准光标里没有这两种手形，所以在这里首次使用时画出来。
    ///
    /// 注意配色：光标位图用**白底黑描边**是刻意的 —— 系统光标要在任意背景（明/暗/
    /// 素材封面）上都看得清，因此不走 md3 色池（色池色在浅色内容上会看不见）。
    /// </summary>
    static class HandCursors {
        private const int Size = 24;

        private static Cursor? grab;
        private static Cursor? grabbing;

        /// <summary>悬停在可拖拽物上时。</summary>
        public static Cursor Grab => grab ??= Create(open: true);

        /// <summary>正在拖拽时。</summary>
        public static Cursor Grabbing => grabbing ??= Create(open: false);

        private static Cursor Create(bool open) {
            // 手掌 + 四指 + 拇指合成一个外轮廓，指间再画短线：
            // 张开的手手指长而分开，握起的手蜷成指节。
            double palmTop = open ? 10.5 : 9.5;
            Geometry hand = Rounded(5.5, palmTop, 13.8, open ? 10.5 : 11.5, 4);
            double[] tops = open ? new[] { 4.5, 3.0, 3.8, 5.8 } : new[] { 7.5, 7.0, 7.3, 8.0 };
            for (int i = 0; i < 4; ++i) {
                hand = Union(hand, Rounded(5.5 + i * 3.45, tops[i], 3.2, 13 - tops[i], 1.6));
            }
            var thumb = open
                ? Rounded(4.9, 11, 3.2, 7, 1.6, -35, new Point(6.5, 18))
                : Rounded(4.6, 12.5, 3.2, 5, 1.6, -25, new Point(6.2, 17.5));
            hand = Union(hand, thumb);

            var bitmap = new RenderTargetBitmap(new PixelSize(Size, Size), new Vector(96, 96));
            using (var context = bitmap.CreateDrawingContext()) {
                context.DrawGeometry(Brushes.White, new Pen(Brushes.Black, 1.2), hand);
                var separator = new Pen(Brushes.Black, 0.8);
                for (int i = 0; i < 3; ++i) {
                    double x = 5.5 + i * 3.45 + 3.2 + 0.125;
                    double y = Math.Max(tops[i], tops[i + 1]) + 1.6;
                    context.DrawLine(separator, new Point(x, y), new Point(x, palmTop + 1));
                }
            }
            return new Cursor(bitmap, new PixelPoint(Size / 2, Size / 2));
        }

        private static Geometry Rounded(double x, double y, double width, double height, double radius,
            double angle = 0, Point pivot = default) {
            var geometry = new RectangleGeometry(new Rect(x, y, width, height), radius, radius);
            if (angle != 0) {
                geometry.Transform = new RotateTransform(angle, pivot.X, pivot.Y);
            }
            return geometry;
        }

        private static Geometry Union(Geometry a, Geometry b) {
            return new CombinedGeometry(GeometryCombineMode.Union, a, b);
        }
    }
}

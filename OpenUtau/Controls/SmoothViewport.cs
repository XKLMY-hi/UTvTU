using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using OpenUtau.Core.Util;

namespace OpenUtau.App.Controls {
    /// <summary>
    /// 卷帘 / 编排视图的**平滑滚轮滚动与缩放**（上游 `1c43dc2b`）。
    ///
    /// 一次滚轮步进不直接跳到位，而是把"目标值"交给一条 <see cref="Motion"/>
    /// （0.18 s 的四次曲线，首尾速度与加速度都为 0：起步不顿、停下不撞），
    /// 滚动条/缩放值沿它滑过去。滑动途中的新步进**从当前位置与当前速度续接**
    /// （<see cref="Motion.Retarget"/>）⇒ 连续滚滚轮是"一段连续运动"而不是一段段跳。
    /// 计时用输入时钟（<c>Stopwatch</c>）而非数帧，帧间隔不匀也不会抖。
    /// 精密触控板送来的本来就是**小数** delta（<see cref="IsWheelStep"/> 判定），
    /// 它们立即生效、不做滑动——否则会把顺滑的手势再"加工"一遍。
    ///
    /// 本文件按我们的结构移植上游实现，做了一处适配：
    /// 上游读 `Preferences.Default.ReduceAnimations`，我们的等价偏好叫
    /// **`ReduceMotion`**（"我的电脑是土豆"开关）。不引入上游的偏好项/文案/设置界面。
    /// </summary>
    public class SmoothViewport {
        private readonly Control owner;
        private readonly List<ValueGlide> scrolls = new List<ValueGlide>();
        private readonly List<ValueGlide> values = new List<ValueGlide>();
        private readonly List<ZoomGlide> zooms = new List<ZoomGlide>();
        private bool running;

        public SmoothViewport(Control owner) {
            this.owner = owner;
        }

        /// <summary>
        /// 该滚轮 delta 是否来自"带刻度"的鼠标滚轮（整步进），而不是精密触控板。
        /// 只有整步进才值得做滑动；小数 delta 已经是连续的。
        /// </summary>
        public static bool IsWheelStep(double delta) {
            return delta != 0 && Math.Abs(delta - Math.Round(delta)) < 1e-6;
        }

        public ValueGlide Scroll(ScrollBar bar) {
            var glide = new ValueGlide(this, () => bar.Value, value => bar.Value = value, () => bar.Minimum, () => bar.Maximum);
            scrolls.Add(glide);
            return glide;
        }

        /// <summary>
        /// 让视图自己的另一个数值也带滑动（例如轨道高）。与滚动不同，缩放不会打断它。
        /// </summary>
        public ValueGlide Value(Func<double> get, Action<double> set, Func<double> min, Func<double> max) {
            var glide = new ValueGlide(this, get, set, min, max);
            values.Add(glide);
            return glide;
        }

        /// <param name="zoom">
        /// 视图模型的缩放方法；其 delta 必须把视图放大 (1 + 2 * delta) 倍
        /// （即 `OnXZoomed` / `OnYZoomed` 的约定）。
        /// </param>
        public ZoomGlide Zoom(Action<Point, double> zoom) {
            var glide = new ZoomGlide(this, zoom);
            zooms.Add(glide);
            return glide;
        }

        // 缩放每帧要绕锚点挪动滚动偏移，因此两者不同时滑动：
        // 新缩放停掉滚动，新滚动结束缩放。
        internal void CancelScrolls() {
            foreach (var glide in scrolls) {
                glide.Cancel();
            }
        }

        internal void FinishZooms() {
            foreach (var glide in zooms) {
                glide.Finish();
            }
        }

        /// <returns>
        /// false = 偏好里开了"减少动效"，或视图不在窗口里（没有帧可滑）⇒ 调用方立即设值。
        /// </returns>
        internal bool Start() {
            if (Preferences.Default.ReduceMotion) {
                return false;
            }
            var topLevel = TopLevel.GetTopLevel(owner);
            if (topLevel == null) {
                return false;
            }
            if (!running) {
                running = true;
                topLevel.RequestAnimationFrame(OnFrame);
            }
            return true;
        }

        private void OnFrame(TimeSpan time) {
            long now = Stopwatch.GetTimestamp();
            bool moving = false;
            foreach (var glide in scrolls) {
                moving |= glide.Step(now);
            }
            foreach (var glide in values) {
                moving |= glide.Step(now);
            }
            foreach (var glide in zooms) {
                moving |= glide.Step(now);
            }
            var topLevel = TopLevel.GetTopLevel(owner);
            if (moving && topLevel != null) {
                topLevel.RequestAnimationFrame(OnFrame);
            } else {
                running = false;
                FinishZooms();
            }
        }
    }

    /// <summary>
    /// 从 P0 以初速 V0 出发、在 <see cref="Duration"/> 后停在 P0 + Distance 的运动，
    /// 末端速度与加速度都为 0（不撞停）。即四次曲线
    /// p(s) = (3d - v)s^4 + (3v - 8d)s^3 + (6d - 3v)s^2 + vs（s = t / Duration，
    /// d = Distance，v = V0 * Duration）。取 V0 = 3 * Distance / Duration 时退化为
    /// ease-out cubic；更小的 V0（滑动途中续接）会先加速，从而保持速度连续。
    /// </summary>
    internal readonly struct Motion {
        public const double Duration = 0.18; // seconds

        private readonly double p0;
        private readonly double distance;
        private readonly double v0;
        private readonly long start;

        private Motion(double p0, double distance, double v0, long start) {
            this.p0 = p0;
            this.distance = distance;
            this.v0 = v0;
            this.start = start;
        }

        public double Target => p0 + distance;

        public static Motion EaseOut(double from, double target, long now) {
            return new Motion(from, target - from, 3 * (target - from) / Duration, now);
        }

        /// <summary>从当前位置、当前速度，朝新目标续接。</summary>
        public Motion Retarget(double target, long now) {
            double p = Position(now);
            double v = Velocity(now);
            double d = target - p;
            // 比这个还快就会冲过目标
            if (v * d > 0 && Math.Abs(v) > 4 * Math.Abs(d) / Duration) {
                v = 4 * d / Duration;
            }
            return new Motion(p, d, v, now);
        }

        public bool Done(long now) => Progress(now) >= 1;

        public double Position(long now) {
            double s = Progress(now);
            double d = distance, v = v0 * Duration;
            return p0 + (((3 * d - v) * s + (3 * v - 8 * d)) * s + (6 * d - 3 * v)) * s * s + v * s;
        }

        public double Velocity(long now) {
            double s = Progress(now);
            double d = distance, v = v0 * Duration;
            return (((4 * (3 * d - v) * s + 3 * (3 * v - 8 * d)) * s + 2 * (6 * d - 3 * v)) * s + v) / Duration;
        }

        private double Progress(long now) {
            return Math.Clamp(Stopwatch.GetElapsedTime(start, now).TotalSeconds / Duration, 0, 1);
        }
    }

    /// <summary>滚动条的值（或视图的其它数值）滑向目标。</summary>
    public class ValueGlide {
        private readonly SmoothViewport viewport;
        private readonly Func<double> get;
        private readonly Action<double> set;
        private readonly Func<double> min;
        private readonly Func<double> max;
        private Motion motion;
        private double lastSet;
        private bool active;

        internal ValueGlide(SmoothViewport viewport, Func<double> get, Action<double> set, Func<double> min, Func<double> max) {
            this.viewport = viewport;
            this.get = get;
            this.set = set;
            this.min = min;
            this.max = max;
        }

        public void By(double amount, bool animate) {
            viewport.FinishZooms();
            long now = Stopwatch.GetTimestamp();
            bool continuing = active && !MovedElsewhere();
            double target = Math.Clamp((continuing ? motion.Target : get()) + amount, min(), max());
            if (!animate || !viewport.Start()) {
                Set(target);
                active = false;
                return;
            }
            motion = continuing ? motion.Retarget(target, now) : Motion.EaseOut(get(), target, now);
            lastSet = get();
            active = true;
        }

        internal bool Step(long now) {
            if (!active) {
                return false;
            }
            if (MovedElsewhere()) {
                // 拖动滑块、自动滚动或跳转接管了
                active = false;
                return false;
            }
            Set(Math.Clamp(motion.Position(now), min(), max()));
            active = !motion.Done(now);
            return active;
        }

        internal void Cancel() {
            active = false;
        }

        private bool MovedElsewhere() {
            return Math.Abs(get() - lastSet) > 1e-6;
        }

        private void Set(double value) {
            set(value);
            lastSet = get();
        }
    }

    public class ZoomGlide {
        // 单个滚轮事件允许的最小缩放因子；缩放档位本身由视图模型钳制
        private const double MinFactor = 0.01;

        private readonly SmoothViewport viewport;
        private readonly Action<Point, double> zoom;
        // 以对数尺度记录的滑动位移，以及已经施加到视图上的部分
        private Motion motion;
        private double applied;
        private Point anchor;
        private bool active;

        internal ZoomGlide(SmoothViewport viewport, Action<Point, double> zoom) {
            this.viewport = viewport;
            this.zoom = zoom;
        }

        /// <param name="anchor">保持不动的点，按视图尺寸的比例给。</param>
        public void By(Point anchor, double delta, bool animate) {
            viewport.CancelScrolls();
            if (!animate || !viewport.Start()) {
                Finish();
                zoom(anchor, delta);
                return;
            }
            this.anchor = anchor;
            long now = Stopwatch.GetTimestamp();
            double step = Math.Log(Math.Max(1 + 2 * delta, MinFactor));
            if (active) {
                motion = motion.Retarget(motion.Target + step, now);
            } else {
                applied = 0;
                motion = Motion.EaseOut(0, step, now);
            }
            active = true;
        }

        internal bool Step(long now) {
            if (!active) {
                return false;
            }
            ApplyUpTo(motion.Position(now));
            active = !motion.Done(now);
            return active;
        }

        internal void Finish() {
            if (active) {
                ApplyUpTo(motion.Target);
            }
            active = false;
        }

        private void ApplyUpTo(double next) {
            double step = next - applied;
            if (step != 0) {
                zoom(anchor, (Math.Exp(step) - 1) / 2);
                applied = next;
            }
        }
    }
}

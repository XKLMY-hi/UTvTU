using System;
using System.Diagnostics;
using Avalonia.Controls;
using OpenUtau.App.Controls;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// 平滑滚动/缩放的**运动学契约**（上游 `1c43dc2b` 的移植版，见 `SmoothViewport`）。
    ///
    /// 这里只测纯数学与"降级路径"，不测窗口里的动画帧：
    ///   · `Motion` 是四次曲线，两端速度/加速度为 0 ⇒ 起步不顿、停下不撞；
    ///   · `Retarget` 必须从当前位置与当前速度续接（连续滚滚轮才是一段连续运动）；
    ///   · 没有窗口（没有帧可滑）或偏好里开了"减少动效"时，`By` 必须**立即设值**，
    ///     不能把值卡在半路上 —— 这是"土豆开关"的正确性所在。
    /// </summary>
    public class SmoothViewportTests {
        [Fact]
        public void IsWheelStep_DistinguishesNotchedWheelFromPrecisionTouchpad() {
            // 带刻度的鼠标滚轮：整步进
            Assert.True(SmoothViewport.IsWheelStep(1));
            Assert.True(SmoothViewport.IsWheelStep(-1));
            Assert.True(SmoothViewport.IsWheelStep(3));
            // 精密触控板：小数 delta（本来就连续，不该再加工）
            Assert.False(SmoothViewport.IsWheelStep(0.35));
            Assert.False(SmoothViewport.IsWheelStep(-0.02));
            // 零位移不算步进
            Assert.False(SmoothViewport.IsWheelStep(0));
        }

        [Fact]
        public void Motion_EaseOut_StartsAtSpeedAndComesToRestAtTheTarget() {
            long now = Stopwatch.GetTimestamp();
            var motion = Motion.EaseOut(from: 100, target: 160, now);

            Assert.Equal(160, motion.Target);
            // t = 0：在起点；初速 = 3 * 距离 / 时长（ease-out **起步快**，且与上一段滑动
            // 速度连续 —— 这正是"连续滚滚轮不顿"的来源，而不是从 0 起步）
            Assert.Equal(100, motion.Position(now), 6);
            Assert.Equal(3 * 60 / Motion.Duration, motion.Velocity(now), 6);
            Assert.False(motion.Done(now));

            // 中段：朝目标前进（在起止之间）
            double mid = motion.Position(now + (long)(Stopwatch.Frequency * Motion.Duration / 2));
            Assert.True(mid > 100 && mid < 160, $"中点应在起止之间，实际 {mid}");

            // t = Duration：正好停在目标、**速度为 0**（停下不撞）、结束
            long end = now + (long)(Stopwatch.Frequency * Motion.Duration);
            Assert.Equal(160, motion.Position(end), 6);
            Assert.Equal(0, motion.Velocity(end), 6);
            Assert.True(motion.Done(end));
        }

        [Fact]
        public void Motion_Retarget_KeepsPositionAndVelocityContinuous() {
            long now = Stopwatch.GetTimestamp();
            var first = Motion.EaseOut(from: 0, target: 100, now);
            // 滑动进行到一半时来了新的一步
            long mid = now + (long)(Stopwatch.Frequency * Motion.Duration / 2);
            double posAtMid = first.Position(mid);
            double velAtMid = first.Velocity(mid);

            var retargeted = first.Retarget(target: 250, now: mid);
            // 位置连续：续接的运动从当前位置出发
            Assert.Equal(posAtMid, retargeted.Position(mid), 6);
            // 速度连续：初速与续接前一致（除非会被限速以不冲过目标）
            Assert.Equal(velAtMid, retargeted.Velocity(mid), 6);
            Assert.Equal(250, retargeted.Target);
        }

        [Fact]
        public void ValueGlide_WithoutWindow_AppliesImmediately() {
            // 视图不在窗口里（TopLevel 为 null）⇒ 没有帧可滑，必须立即设值
            var viewport = new SmoothViewport(new Control());
            double value = 10;
            var glide = viewport.Value(() => value, v => value = v, () => 0, () => 100);

            glide.By(amount: 25, animate: true);
            Assert.Equal(35, value);      // 立即到位，不是停在半路

            // 钳制仍生效（min/max 由调用方给）
            glide.By(amount: 1000, animate: true);
            Assert.Equal(100, value);

            // animate: false（精密触控板路径）同样立即设值
            glide.By(amount: -30, animate: false);
            Assert.Equal(70, value);
        }

        [Fact]
        public void ValueGlide_ReduceMotionPreference_AppliesImmediately() {
            bool old = Preferences.Default.ReduceMotion;
            try {
                Preferences.Default.ReduceMotion = true;
                var viewport = new SmoothViewport(new Control());
                double value = 0;
                var glide = viewport.Value(() => value, v => value = v, () => 0, () => 100);
                glide.By(amount: 40, animate: true);
                Assert.Equal(40, value);   // 减少动效 ⇒ 不滑，直接到
            } finally {
                Preferences.Default.ReduceMotion = old;
            }
        }
    }
}

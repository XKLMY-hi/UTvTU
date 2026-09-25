using System;
using OpenUtau.Core.Theming;
using Xunit;

namespace OpenUtau.Test.Core.Theming {
    /// <summary>
    /// 动效令牌契约：数值必须与 Material 3 motion tokens 一致，开关必须能一键归零，
    /// 资源键必须稳定（XAML 侧要用）。
    /// </summary>
    public class Md3MotionTests {
        [Theory]
        [InlineData(Md3Duration.Short1, 50)]
        [InlineData(Md3Duration.Short2, 100)]
        [InlineData(Md3Duration.Short3, 150)]
        [InlineData(Md3Duration.Short4, 200)]
        [InlineData(Md3Duration.Medium1, 250)]
        [InlineData(Md3Duration.Medium2, 300)]
        [InlineData(Md3Duration.Medium3, 350)]
        [InlineData(Md3Duration.Medium4, 400)]
        [InlineData(Md3Duration.Long1, 450)]
        [InlineData(Md3Duration.Long2, 500)]
        [InlineData(Md3Duration.Long3, 550)]
        [InlineData(Md3Duration.Long4, 600)]
        [InlineData(Md3Duration.ExtraLong1, 700)]
        [InlineData(Md3Duration.ExtraLong2, 800)]
        [InlineData(Md3Duration.ExtraLong3, 900)]
        [InlineData(Md3Duration.ExtraLong4, 1000)]
        public void Durations_MatchMaterial3(Md3Duration duration, double milliseconds) {
            Assert.Equal(milliseconds, Md3Motion.MillisecondsOf(duration));
            Assert.Equal(TimeSpan.FromMilliseconds(milliseconds), Md3Motion.DurationWhen(duration, true));
        }

        [Fact]
        public void Disabled_ZeroesEveryDuration() {
            foreach (Md3Duration duration in Enum.GetValues<Md3Duration>()) {
                Assert.Equal(TimeSpan.Zero, Md3Motion.DurationWhen(duration, false));
            }
        }

        [Theory]
        [InlineData(Md3Easing.Linear, 0.0, 0.0, 1.0, 1.0)]
        [InlineData(Md3Easing.Standard, 0.2, 0.0, 0.0, 1.0)]
        [InlineData(Md3Easing.StandardDecelerate, 0.0, 0.0, 0.0, 1.0)]
        [InlineData(Md3Easing.StandardAccelerate, 0.3, 0.0, 1.0, 1.0)]
        [InlineData(Md3Easing.EmphasizedDecelerate, 0.05, 0.7, 0.1, 1.0)]
        [InlineData(Md3Easing.EmphasizedAccelerate, 0.3, 0.0, 0.8, 0.15)]
        public void Easings_MatchMaterial3(Md3Easing easing, double x1, double y1, double x2, double y2) {
            Assert.Equal((x1, y1, x2, y2), Md3Motion.Bezier(easing));
        }

        [Fact]
        public void SemanticDurations_UseTheDeclaredSlots() {
            // 语义档是"控件唯一该看的东西"：悬停 short3、进入 short4、离开 short3、视图 medium2
            Assert.Equal(TimeSpan.FromMilliseconds(150), Md3Motion.HoverDuration);
            Assert.Equal(TimeSpan.FromMilliseconds(200), Md3Motion.EnterDuration);
            Assert.Equal(TimeSpan.FromMilliseconds(150), Md3Motion.ExitDuration);
            Assert.Equal(TimeSpan.FromMilliseconds(300), Md3Motion.ViewDuration);
            Assert.Equal(Md3Easing.Standard, Md3Motion.HoverEasing);
            Assert.Equal(Md3Easing.EmphasizedDecelerate, Md3Motion.EnterEasing);
            Assert.Equal(Md3Easing.EmphasizedAccelerate, Md3Motion.ExitEasing);
            Assert.Equal(Md3Easing.EmphasizedDecelerate, Md3Motion.ViewEasing);
        }

        [Fact]
        public void ResourceKeys_AreStable() {
            Assert.Equal("md3.motion.duration.short4", Md3Motion.DurationKey(Md3Duration.Short4));
            Assert.Equal("md3.motion.duration.extra-long1", Md3Motion.DurationKey(Md3Duration.ExtraLong1));
            Assert.Equal("md3.motion.easing.standard", Md3Motion.EasingKey(Md3Easing.Standard));
            Assert.Equal("md3.motion.easing.emphasized-decelerate", Md3Motion.EasingKey(Md3Easing.EmphasizedDecelerate));
            Assert.Equal("md3.motion.easing.standard-accelerate", Md3Motion.EasingKey(Md3Easing.StandardAccelerate));
        }
    }
}

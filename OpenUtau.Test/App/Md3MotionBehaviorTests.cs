using System;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using OpenUtau.Core.Theming;
using OpenUtau.Theming;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// 动效接口契约：控件只贴标签或由代码点名播放，时长/延迟一律来自令牌。
    /// 覆盖：进入（单元素 / 子树交错）、离开、关掉动效后的兜底。
    /// </summary>
    public class Md3MotionBehaviorTests {
        [AvaloniaFact]
        public void Play_StartsHidden_WithTokenDuration() {
            var border = new Border();
            Motion.Play(border, MotionEntrance.FromBottom);
            Assert.Equal(0d, border.Opacity);
            Assert.NotNull(border.RenderTransform);
            var transitions = border.Transitions!.OfType<TransitionBase>().ToList();
            Assert.NotEmpty(transitions);
            Assert.All(transitions, t => Assert.Equal(Md3Motion.EnterDuration, t.Duration));
            Assert.Contains(transitions, t => t.Property == Visual.OpacityProperty);
            Assert.Contains(transitions, t => t.Property == Visual.RenderTransformProperty);
        }

        [AvaloniaFact]
        public void Play_ScaleEntrance_UsesCenterOrigin() {
            var border = new Border();
            Motion.Play(border, MotionEntrance.Scale);
            Assert.Equal(RelativePoint.Center, border.RenderTransformOrigin);
            Assert.IsType<ScaleTransform>(border.RenderTransform);
        }

        [AvaloniaFact]
        public void Play_WithDelay_PutsDelayOnTransitions() {
            var border = new Border();
            Motion.Play(border, MotionEntrance.FromBottom, 120);
            var transitions = border.Transitions!.OfType<TransitionBase>().ToList();
            Assert.All(transitions, t => Assert.Equal(TimeSpan.FromMilliseconds(120), t.Delay));
        }

        [AvaloniaFact]
        public void PlayAll_PlaysTaggedDescendants_WithOwnDelay() {
            var root = new StackPanel();
            var first = new Border();
            Motion.SetEnter(first, MotionEntrance.FromLeft);
            var second = new Border();
            Motion.SetEnter(second, MotionEntrance.FromBottom);
            Motion.SetDelay(second, 180);
            var untouched = new Border();
            root.Children.Add(first);
            root.Children.Add(second);
            root.Children.Add(untouched);

            Assert.Equal(2, Motion.PlayAll(root));
            Assert.Equal(0d, first.Opacity);
            Assert.Equal(0d, second.Opacity);
            Assert.Equal(1d, untouched.Opacity);   // 没贴标签的不动
            Assert.All(second.Transitions!.OfType<TransitionBase>(),
                t => Assert.Equal(TimeSpan.FromMilliseconds(180), t.Delay));
        }

        [AvaloniaFact]
        public void PlayExit_FadesOutWithExitToken() {
            var border = new Border();
            Motion.PlayExit(border);
            Assert.Equal(0d, border.Opacity);
            var transitions = border.Transitions!.OfType<TransitionBase>().ToList();
            Assert.All(transitions, t => Assert.Equal(Md3Motion.ExitDuration, t.Duration));
        }

        [AvaloniaFact]
        public void Reset_RestoresPlayableState() {
            var border = new Border();
            Motion.Play(border, MotionEntrance.Scale);
            Motion.Reset(border);
            Assert.Equal(1d, border.Opacity);
            Assert.Null(border.RenderTransform);
        }

        [AvaloniaFact]
        public void DisabledToken_JumpsToFinalState() {
            // 关掉动效后不应留下透明元素，时长也必须归零
            bool was = Md3Motion.Enabled;
            try {
                Md3Motion.Enabled = false;
                var border = new Border();
                Motion.Play(border, MotionEntrance.FromBottom);
                Assert.Equal(1d, border.Opacity);
                Assert.All(border.Transitions!.OfType<TransitionBase>(),
                    t => Assert.Equal(TimeSpan.Zero, t.Duration));
            } finally {
                Md3Motion.Enabled = was;
            }
        }
    }
}

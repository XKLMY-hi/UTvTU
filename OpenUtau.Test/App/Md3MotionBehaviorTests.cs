using System;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using OpenUtau.Core.Theming;
using OpenUtau.Theming;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// 动效接口契约：控件只贴标签（Motion.Hover / Enter / Popup），
    /// 过渡的时长一律来自令牌；多个附加属性共存时不能互相顶掉整份 Transitions。
    /// </summary>
    public class Md3MotionBehaviorTests {
        [AvaloniaFact]
        public void Hover_AttachesTokenDurationTransitions() {
            var border = new Border();
            Motion.SetHover(border, true);
            Assert.NotNull(border.Transitions);
            var transitions = border.Transitions!.OfType<TransitionBase>().ToList();
            Assert.NotEmpty(transitions);
            Assert.All(transitions, t => Assert.Equal(Md3Motion.HoverDuration, t.Duration));
            // 容器悬停要动的三个颜色属性都在（Border 上 Background/BorderBrush/Foreground 齐全）
            Assert.Contains(transitions, t => t.Property == Border.BackgroundProperty);
            Assert.Contains(transitions, t => t.Property == Border.BorderBrushProperty);
            Assert.Contains(transitions, t => t.Property == Visual.OpacityProperty);
        }

        [AvaloniaFact]
        public void Popup_StartsTransparent_WithTokenDuration() {
            var border = new Border();
            Motion.SetPopup(border, true);
            Assert.NotNull(border.Transitions);
            Assert.Equal(0d, border.Opacity);
            Assert.NotNull(border.RenderTransform);
            var transitions = border.Transitions!.OfType<TransitionBase>().ToList();
            Assert.All(transitions, t => Assert.Equal(Md3Motion.EnterDuration, t.Duration));
            Assert.Contains(transitions, t => t.Property == Visual.OpacityProperty);
            Assert.Contains(transitions, t => t.Property == Visual.RenderTransformProperty);
        }

        [AvaloniaFact]
        public void HoverAndPopup_ShareOneTransitionList() {
            var border = new Border();
            Motion.SetHover(border, true);
            Motion.SetPopup(border, true);
            var transitions = border.Transitions!.OfType<TransitionBase>().ToList();
            // 进入动画不能把悬停的颜色过渡顶掉；同一属性只留一条
            Assert.Contains(transitions, t => t.Property == Border.BackgroundProperty);
            Assert.Contains(transitions, t => t.Property == Visual.OpacityProperty);
            Assert.Equal(1, transitions.Count(t => t.Property == Visual.OpacityProperty));
        }

        [AvaloniaFact]
        public void DisabledToken_StillLeavesElementUsable() {
            // 关掉动效时进入动画立刻落到终态（不会留下一层透明元素）
            bool was = Md3Motion.Enabled;
            try {
                Md3Motion.Enabled = false;
                var border = new Border();
                Motion.SetEnter(border, true);
                Assert.Equal(1d, border.Opacity);
                Assert.NotNull(border.Transitions);
                var transitions = border.Transitions!.OfType<TransitionBase>().ToList();
                Assert.All(transitions, t => Assert.Equal(TimeSpan.Zero, t.Duration));
            } finally {
                Md3Motion.Enabled = was;
            }
        }
    }
}

using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.Core.Theming;

namespace OpenUtau.Theming {
    /// <summary>进入动画的来向（元素从哪一侧滑入/放大进入）。</summary>
    public enum MotionEntrance {
        /// <summary>不做进入动画。</summary>
        None,
        /// <summary>只淡入（无位移，适合遮罩）。</summary>
        Fade,
        /// <summary>从下方滑入。</summary>
        FromBottom,
        /// <summary>从上方滑入。</summary>
        FromTop,
        /// <summary>从左侧滑入。</summary>
        FromLeft,
        /// <summary>从右侧滑入。</summary>
        FromRight,
        /// <summary>原地放大进入（卡片弹出，0.96 → 1）。</summary>
        Scale,
    }

    /// <summary>
    /// 页面/面板级过渡接口：控件只贴标签（或由代码点名播放），时长与缓动由
    /// <see cref="Md3Motion"/> 令牌说了算 —— 与颜色池同构。
    ///
    /// 声明式（配合 <see cref="PlayAll"/> 一次性播放，Delay 做交错）：
    ///   motion:Motion.Enter="FromBottom" motion:Motion.Delay="120"
    ///   motion:Motion.Enter="Scale"      motion:Motion.AutoPlay="True"   // 弹层：挂载即播
    ///
    /// 代码式（视图/面板切换）：
    ///   <see cref="PlayAll"/>（视图出现，按各自 Delay 交错）
    ///   <see cref="Play"/>(控件, 来向)（面板展开、对话框弹出）
    ///   <see cref="PlayExitAsync"/>（视图离开：淡出 + 放大退场；<see cref="PlayExit"/> 为不等版本）
    ///
    /// 关掉动效（偏好设置）时长归零，全部变瞬变，调用方零分支。
    /// </summary>
    public static class Motion {
        /// <summary>滑入位移距离（px）。</summary>
        public const double Distance = 16;

        /// <summary>进入动画来向（None = 不参与）。</summary>
        public static readonly AttachedProperty<MotionEntrance> EnterProperty =
            AvaloniaProperty.RegisterAttached<Control, MotionEntrance>("Enter", typeof(Motion));

        /// <summary>交错延迟（毫秒），配合 Enter 使用。</summary>
        public static readonly AttachedProperty<int> DelayProperty =
            AvaloniaProperty.RegisterAttached<Control, int>("Delay", typeof(Motion));

        /// <summary>挂载到可视树时自动播放 Enter 动画（弹层/飞行卡片用）。</summary>
        public static readonly AttachedProperty<bool> AutoPlayProperty =
            AvaloniaProperty.RegisterAttached<Control, bool>("AutoPlay", typeof(Motion));

        static Motion() {
            AutoPlayProperty.Changed.AddClassHandler<Control>((control, args) => {
                if (args.NewValue is true) {
                    MotionEntrance entrance = GetEnter(control);
                    if (entrance != MotionEntrance.None) {
                        control.AttachedToVisualTree += (_, _) => Play(control, entrance, GetDelay(control));
                    }
                }
            });
        }

        public static void SetEnter(Control control, MotionEntrance value) => control.SetValue(EnterProperty, value);
        public static MotionEntrance GetEnter(Control control) => control.GetValue(EnterProperty);

        public static void SetDelay(Control control, int value) => control.SetValue(DelayProperty, value);
        public static int GetDelay(Control control) => control.GetValue(DelayProperty);

        public static void SetAutoPlay(Control control, bool value) => control.SetValue(AutoPlayProperty, value);
        public static bool GetAutoPlay(Control control) => control.GetValue(AutoPlayProperty);

        /// <summary>播放一个元素的进入动画。</summary>
        public static void Play(Control control, MotionEntrance entrance, int delayMilliseconds = 0) {
            if (entrance == MotionEntrance.None) {
                return;
            }
            TimeSpan delay = delayMilliseconds > 0 ? TimeSpan.FromMilliseconds(delayMilliseconds) : TimeSpan.Zero;
            control.Transitions = new Transitions {
                new DoubleTransition {
                    Property = Visual.OpacityProperty,
                    Duration = Md3Motion.EnterDuration,
                    Delay = delay,
                    Easing = Md3MotionResources.Easing(Md3Motion.EnterEasing),
                },
                new TransformOperationsTransition {
                    Property = Visual.RenderTransformProperty,
                    Duration = Md3Motion.EnterDuration,
                    Delay = delay,
                    Easing = Md3MotionResources.Easing(Md3Motion.EnterEasing),
                },
            };
            Apply(control, entrance, exit: false);
        }

        /// <summary>播放子树里所有标了 <see cref="EnterProperty"/> 的元素（按各自 Delay 交错）。返回播放个数。</summary>
        public static int PlayAll(Control root) {
            int count = 0;
            foreach (Control control in root.GetSelfAndVisualDescendants().OfType<Control>()) {
                MotionEntrance entrance = GetEnter(control);
                if (entrance == MotionEntrance.None) {
                    continue;
                }
                Play(control, entrance, GetDelay(control));
                count++;
            }
            return count;
        }

        /// <summary>开始播放离开动画（淡出 + 略微放大退场），不等动画结束。</summary>
        public static void PlayExit(Control control) {
            control.Transitions = new Transitions {
                new DoubleTransition {
                    Property = Visual.OpacityProperty,
                    Duration = Md3Motion.ExitDuration,
                    Easing = Md3MotionResources.Easing(Md3Motion.ExitEasing),
                },
                new TransformOperationsTransition {
                    Property = Visual.RenderTransformProperty,
                    Duration = Md3Motion.ExitDuration,
                    Easing = Md3MotionResources.Easing(Md3Motion.ExitEasing),
                },
            };
            Apply(control, MotionEntrance.Scale, exit: true);
        }

        /// <summary>播放离开动画并等它结束（视图切换用）。</summary>
        public static async Task PlayExitAsync(Control control) {
            PlayExit(control);
            if (Md3Motion.Enabled) {
                await Task.Delay(Md3Motion.ExitDuration);
            }
        }

        /// <summary>复位到"未播放"状态（离开动画结束后调用，便于下次重播）。</summary>
        public static void Reset(Control control) {
            control.Opacity = 1;
            control.RenderTransform = null;
        }

        private static void Apply(Control control, MotionEntrance entrance, bool exit) {
            control.RenderTransformOrigin = entrance == MotionEntrance.Scale ? RelativePoint.Center : RelativePoint.TopLeft;
            ITransform start = exit
                ? new ScaleTransform(1, 1)
                : StartTransform(entrance);
            ITransform end = exit
                ? new ScaleTransform(1.04, 1.04)
                : EndTransform(entrance);
            control.Opacity = 0;
            control.RenderTransform = start;
            if (!Md3Motion.Enabled) {
                control.Opacity = 1;
                control.RenderTransform = end;
                return;
            }
            // 先以起始态呈现一帧，再切到终止态让过渡跑起来（Background 优先级晚于 Render）
            Dispatcher.UIThread.Post(() => {
                control.Opacity = 1;
                control.RenderTransform = end;
            }, DispatcherPriority.Background);
        }

        private static ITransform StartTransform(MotionEntrance entrance) => entrance switch {
            MotionEntrance.FromBottom => new TranslateTransform(0, Distance),
            MotionEntrance.FromTop => new TranslateTransform(0, -Distance),
            MotionEntrance.FromLeft => new TranslateTransform(-Distance, 0),
            MotionEntrance.FromRight => new TranslateTransform(Distance, 0),
            MotionEntrance.Scale => new ScaleTransform(0.96, 0.96),
            _ => new TranslateTransform(0, 0),
        };

        private static ITransform EndTransform(MotionEntrance entrance) =>
            entrance == MotionEntrance.Scale ? new ScaleTransform(1, 1) : new TranslateTransform(0, 0);
    }
}

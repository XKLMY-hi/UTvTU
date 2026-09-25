using System;
using System.Collections.Generic;
using System.Reflection;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using OpenUtau.Core.Theming;

namespace OpenUtau.Theming {
    /// <summary>
    /// 动效接口：控件只贴标签，时长/缓动由 <see cref="Md3Motion"/> 令牌说了算
    /// （与颜色池同构 —— 一个接口管所有动效，改数值只动令牌）。
    ///
    ///   motion:Motion.Hover="True"   悬停/状态层过渡（Background / BorderBrush / Foreground）
    ///   motion:Motion.Enter="True"   元素进入：淡入 + 上移 8px
    ///   motion:Motion.Popup="True"   弹层进入：淡入 + 由 0.96 放大（挂在 Popup 内容上，每次打开都会跑）
    ///
    /// 关闭动效（偏好设置）时令牌时长归零，过渡变成瞬变，这里无需分支。
    /// </summary>
    public static class Motion {
        /// <summary>悬停/状态层过渡。</summary>
        public static readonly AttachedProperty<bool> HoverProperty =
            AvaloniaProperty.RegisterAttached<Control, bool>("Hover", typeof(Motion));

        /// <summary>元素进入动画（淡入 + 上移）。</summary>
        public static readonly AttachedProperty<bool> EnterProperty =
            AvaloniaProperty.RegisterAttached<Control, bool>("Enter", typeof(Motion));

        /// <summary>弹层进入动画（淡入 + 放大）。</summary>
        public static readonly AttachedProperty<bool> PopupProperty =
            AvaloniaProperty.RegisterAttached<Control, bool>("Popup", typeof(Motion));

        static Motion() {
            HoverProperty.Changed.AddClassHandler<Control>((control, args) => {
                if (args.NewValue is true) {
                    ApplyHover(control);
                }
            });
            EnterProperty.Changed.AddClassHandler<Control>((control, args) => {
                if (args.NewValue is true) {
                    ApplyEnter(control, fromScale: false);
                }
            });
            PopupProperty.Changed.AddClassHandler<Control>((control, args) => {
                if (args.NewValue is true) {
                    ApplyEnter(control, fromScale: true);
                }
            });
        }

        public static void SetHover(Control control, bool value) => control.SetValue(HoverProperty, value);
        public static bool GetHover(Control control) => control.GetValue(HoverProperty);

        public static void SetEnter(Control control, bool value) => control.SetValue(EnterProperty, value);
        public static bool GetEnter(Control control) => control.GetValue(EnterProperty);

        public static void SetPopup(Control control, bool value) => control.SetValue(PopupProperty, value);
        public static bool GetPopup(Control control) => control.GetValue(PopupProperty);

        /// <summary>悬停过渡：颜色类属性 + 不透明度，按令牌时长/缓动过渡（找不到属性的控件自动跳过）。</summary>
        public static void ApplyHover(Control control) {
            var additions = new List<ITransition> {
                new DoubleTransition {
                    Property = Visual.OpacityProperty,
                    Duration = Md3Motion.HoverDuration,
                    Easing = Md3MotionResources.Easing(Md3Motion.HoverEasing),
                },
            };
            AddBrushTransition(additions, control, "Background");
            AddBrushTransition(additions, control, "BorderBrush");
            AddBrushTransition(additions, control, "Foreground");
            Merge(control, additions);
        }

        /// <summary>进入动画：不透明度 + 位移/缩放，全部走令牌。</summary>
        public static void ApplyEnter(Control control, bool fromScale) {
            Merge(control, new ITransition[] {
                new DoubleTransition {
                    Property = Visual.OpacityProperty,
                    Duration = Md3Motion.EnterDuration,
                    Easing = Md3MotionResources.Easing(Md3Motion.EnterEasing),
                },
                new TransformOperationsTransition {
                    Property = Visual.RenderTransformProperty,
                    Duration = Md3Motion.EnterDuration,
                    Easing = Md3MotionResources.Easing(Md3Motion.EnterEasing),
                },
            });
            control.Opacity = 0;
            if (fromScale) {
                control.RenderTransformOrigin = RelativePoint.Center;
                control.RenderTransform = new ScaleTransform(0.96, 0.96);
            } else {
                control.RenderTransformOrigin = RelativePoint.TopLeft;
                control.RenderTransform = new TranslateTransform(0, 8);
            }
            if (!Md3Motion.Enabled) {
                control.Opacity = 1;
                control.RenderTransform = fromScale ? new ScaleTransform(1, 1) : new TranslateTransform(0, 0);
                return;
            }
            // 首帧以"起始态"呈现，随后切到终止态让过渡跑起来（Background 优先级晚于 Render）
            Dispatcher.UIThread.Post(() => {
                control.Opacity = 1;
                control.RenderTransform = fromScale ? new ScaleTransform(1, 1) : new TranslateTransform(0, 0);
            }, DispatcherPriority.Background);
        }

        /// <summary>按"属性"合并过渡（同一属性后写覆盖先写，避免多个附加属性互相顶掉整份 Transitions）。</summary>
        private static void Merge(Control control, IEnumerable<ITransition> additions) {
            Transitions target = control.Transitions ?? new Transitions();
            foreach (ITransition addition in additions) {
                AvaloniaProperty? property = (addition as TransitionBase)?.Property;
                if (property != null) {
                    for (int i = target.Count - 1; i >= 0; i--) {
                        if (target[i] is TransitionBase existing && existing.Property == property) {
                            target.RemoveAt(i);
                        }
                    }
                }
                target.Add(addition);
            }
            control.Transitions = target;
        }

        private static void AddBrushTransition(List<ITransition> transitions, Control control, string propertyName) {
            AvaloniaProperty? property = FindProperty(control.GetType(), propertyName);
            if (property == null || property.PropertyType != typeof(IBrush)) {
                return;
            }
            transitions.Add(new BrushTransition {
                Property = property,
                Duration = Md3Motion.HoverDuration,
                Easing = Md3MotionResources.Easing(Md3Motion.HoverEasing),
            });
        }

        /// <summary>沿继承链找同名 *Property 静态字段（Border/Panel/TextBlock 各有各的 Background/Foreground）。</summary>
        private static AvaloniaProperty? FindProperty(Type type, string propertyName) {
            for (Type? t = type; t != null; t = t.BaseType) {
                FieldInfo? field = t.GetField(
                    propertyName + "Property",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
                if (field?.GetValue(null) is AvaloniaProperty property) {
                    return property;
                }
            }
            return null;
        }
    }
}

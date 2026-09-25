using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using OpenUtau.Core.Theming;

namespace OpenUtau.Theming {
    /// <summary>
    /// 把动效令牌写入 Avalonia 资源字典，使 XAML 能声明式取时长与缓动：
    ///
    ///   Duration="{StaticResource md3.motion.duration.short4}"
    ///   Easing="{StaticResource md3.motion.easing.emphasized-decelerate}"
    ///
    /// 与颜色池同构（<see cref="Md3ThemeResources"/>）：一个字典、一个安装点、开关一关全部归零。
    /// **注意**：<c>Duration</c>/<c>Easing</c> 在 Avalonia 里是普通 CLR 属性（不是 AvaloniaProperty），
    /// 只能 `StaticResource`，因此本字典要在任何窗口 XAML 加载**之前**安装（见 App.Initialize）；
    /// 代码路径请直接用 <see cref="Duration"/> / <see cref="Easing"/>，或优先用附加属性接口
    /// <see cref="Motion"/>（它自己会取令牌）。
    /// </summary>
    public static class Md3MotionResources {
        private static readonly Dictionary<Md3Easing, Easing> easingCache = new();
        private static ResourceDictionary? installed;

        /// <summary>已安装的动效资源字典（未安装时为 null）。</summary>
        public static ResourceDictionary? Installed => installed;

        /// <summary>按当前令牌安装/重建资源字典（幂等）。</summary>
        public static void Install() {
            Application? app = Application.Current;
            if (app == null) {
                return;
            }
            var dict = new ResourceDictionary();
            foreach (Md3Duration duration in Enum.GetValues<Md3Duration>()) {
                dict[Md3Motion.DurationKey(duration)] = Md3Motion.Duration(duration);
            }
            foreach (Md3Easing easing in Enum.GetValues<Md3Easing>()) {
                dict[Md3Motion.EasingKey(easing)] = Easing(easing);
            }
            if (installed != null) {
                app.Resources.MergedDictionaries.Remove(installed);
            }
            app.Resources.MergedDictionaries.Add(dict);
            installed = dict;
        }

        /// <summary>代码路径取时长（关闭动效时为 0）。</summary>
        public static TimeSpan Duration(Md3Duration duration) => Md3Motion.Duration(duration);

        /// <summary>代码路径取缓动实例（已缓存）。</summary>
        public static Easing Easing(Md3Easing easing) {
            if (!easingCache.TryGetValue(easing, out Easing? value)) {
                (double x1, double y1, double x2, double y2) = Md3Motion.Bezier(easing);
                value = new SplineEasing(x1, y1, x2, y2);
                easingCache[easing] = value;
            }
            return value;
        }
    }
}

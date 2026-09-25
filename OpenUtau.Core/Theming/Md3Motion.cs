using System;

namespace OpenUtau.Core.Theming {
    /// <summary>M3 时长档（Material 3 motion duration tokens，单位毫秒见 <see cref="Md3Motion"/>）。</summary>
    public enum Md3Duration {
        Short1,
        Short2,
        Short3,
        Short4,
        Medium1,
        Medium2,
        Medium3,
        Medium4,
        Long1,
        Long2,
        Long3,
        Long4,
        ExtraLong1,
        ExtraLong2,
        ExtraLong3,
        ExtraLong4,
    }

    /// <summary>M3 缓动档（cubic-bezier 控制点，取自 Material 3 motion easing tokens）。</summary>
    public enum Md3Easing {
        /// <summary>线性（进度类）。</summary>
        Linear,
        /// <summary>标准：通用状态变化。</summary>
        Standard,
        /// <summary>标准减速：小面向用户靠近。</summary>
        StandardDecelerate,
        /// <summary>标准加速：小面离开。</summary>
        StandardAccelerate,
        /// <summary>强调减速：大面/视图进入。</summary>
        EmphasizedDecelerate,
        /// <summary>强调加速：大面/视图离开。</summary>
        EmphasizedAccelerate,
    }

    /// <summary>
    /// 动效令牌（与颜色池同构）：Core 只放纯数据，UI 侧由 <c>Md3MotionResources</c> 装进资源字典，
    /// 控件侧用 <c>Md3.Theming.Motion</c> 的附加属性贴标签。
    ///
    /// **约定**：任何过渡的时长与缓动都必须来自这里，控件里不允许写死秒数或 curves；
    /// <see cref="Enabled"/> 关掉时所有时长归零（动效变瞬变，无障碍/低配可用，代码不用改）。
    /// </summary>
    public static class Md3Motion {
        private static readonly double[] Milliseconds = {
            50, 100, 150, 200,          // short1-4
            250, 300, 350, 400,         // medium1-4
            450, 500, 550, 600,         // long1-4
            700, 800, 900, 1000,        // extra-long1-4
        };

        private static readonly double[,] Curves = {
            { 0.0, 0.0, 1.0, 1.0 },      // linear
            { 0.2, 0.0, 0.0, 1.0 },      // standard
            { 0.0, 0.0, 0.0, 1.0 },      // standard-decelerate
            { 0.3, 0.0, 1.0, 1.0 },      // standard-accelerate
            { 0.05, 0.7, 0.1, 1.0 },     // emphasized-decelerate
            { 0.3, 0.0, 0.8, 0.15 },     // emphasized-accelerate
        };

        /// <summary>动效总开关（由偏好设置驱动；关闭后所有时长归零）。</summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>取时长（关闭动效时为 0）。</summary>
        public static TimeSpan Duration(Md3Duration duration) => DurationWhen(duration, Enabled);

        /// <summary>按指定开关取时长（纯函数版，便于对照与测试）。</summary>
        public static TimeSpan DurationWhen(Md3Duration duration, bool enabled) =>
            enabled ? TimeSpan.FromMilliseconds(Milliseconds[(int)duration]) : TimeSpan.Zero;

        /// <summary>取原始毫秒数（不受开关影响，用于判断"这一档本来有没有动效"）。</summary>
        public static double MillisecondsOf(Md3Duration duration) => Milliseconds[(int)duration];

        /// <summary>取 cubic-bezier 控制点。</summary>
        public static (double X1, double Y1, double X2, double Y2) Bezier(Md3Easing easing) =>
            (Curves[(int)easing, 0], Curves[(int)easing, 1], Curves[(int)easing, 2], Curves[(int)easing, 3]);

        /// <summary>时长 → XAML 资源键（如 md3.motion.duration.short4）。</summary>
        public static string DurationKey(Md3Duration duration) => "md3.motion.duration." + Kebab(duration.ToString());

        /// <summary>缓动 → XAML 资源键（如 md3.motion.easing.emphasized-decelerate）。</summary>
        public static string EasingKey(Md3Easing easing) => "md3.motion.easing." + Kebab(easing.ToString());

        // ── 语义档：控件按"这是什么动效"挑，不按数字挑（改数值只动这里） ──

        /// <summary>悬停/状态层。</summary>
        public static TimeSpan HoverDuration => Duration(Md3Duration.Short3);
        public static Md3Easing HoverEasing => Md3Easing.Standard;

        /// <summary>元素进入（弹层、列表行）。</summary>
        public static TimeSpan EnterDuration => Duration(Md3Duration.Short4);
        public static Md3Easing EnterEasing => Md3Easing.EmphasizedDecelerate;

        /// <summary>元素离开。</summary>
        public static TimeSpan ExitDuration => Duration(Md3Duration.Short3);
        public static Md3Easing ExitEasing => Md3Easing.EmphasizedAccelerate;

        /// <summary>视图级切换（欢迎页 ↔ 编辑器）。</summary>
        public static TimeSpan ViewDuration => Duration(Md3Duration.Medium2);
        public static Md3Easing ViewEasing => Md3Easing.EmphasizedDecelerate;

        private static string Kebab(string name) {
            var sb = new System.Text.StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++) {
                char c = name[i];
                if (char.IsUpper(c)) {
                    if (i > 0) {
                        sb.Append('-');
                    }
                    sb.Append(char.ToLowerInvariant(c));
                } else {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}

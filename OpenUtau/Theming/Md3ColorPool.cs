using System;
using System.Collections.Generic;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using OpenUtau.Core.Theming;

namespace OpenUtau.Theming {
    /// <summary>
    /// 统一颜色池接口：控件的颜色一律经此获取（新 UI 的颜色来源）。
    ///
    /// 与现有主题体系（Fluent/Suki + Plus 令牌）**并存**：本接口只负责 MD3 角色色，
    /// 控件按"逐步替换"原则迁移过来；迁移完成的控件不再直接引用旧的资源键。
    /// </summary>
    public interface IMd3ColorPool {
        /// <summary>种子色（ARGB）。</summary>
        uint Seed { get; }

        /// <summary>配色方案（莫奈/Material You 方案变体）。</summary>
        Md3SchemeVariant Scheme { get; }

        /// <summary>对比度档位（0 为默认）。</summary>
        double ContrastLevel { get; }

        /// <summary>当前深浅色变体。</summary>
        bool IsDark { get; }

        /// <summary>取当前变体的角色色。</summary>
        Color Color(Md3Role role);

        /// <summary>取指定变体的角色色（对照/预览用）。</summary>
        Color Color(Md3Role role, bool isDark);

        /// <summary>取当前变体的角色画刷（已缓存，可直接绑定）。</summary>
        IBrush Brush(Md3Role role);

        /// <summary>取指定变体的角色画刷。</summary>
        IBrush Brush(Md3Role role, bool isDark);

        /// <summary>取角色画笔（用于描边）。</summary>
        IPen Pen(Md3Role role, double thickness = 1.0);

        /// <summary>深浅色或配色变化时触发。</summary>
        event EventHandler? Changed;
    }

    /// <summary>颜色池实现：由种子 + 方案 + 对比度算出深浅两套完整角色色。</summary>
    public sealed class Md3ColorPool : IMd3ColorPool {
        private readonly Md3SchemeColors lightColors;
        private readonly Md3SchemeColors darkColors;
        private readonly IBrush?[] lightBrushes = new IBrush?[Md3SchemeColors.RoleCount];
        private readonly IBrush?[] darkBrushes = new IBrush?[Md3SchemeColors.RoleCount];
        private readonly Dictionary<(Md3Role Role, double Thickness, bool Dark), IPen> pens = new();

        public uint Seed { get; }
        public Md3SchemeVariant Scheme { get; }
        public double ContrastLevel { get; }
        public bool IsDark { get; private set; }

        public event EventHandler? Changed;

        public Md3ColorPool(uint seed, Md3SchemeVariant scheme, bool isDark, double contrastLevel = 0.0) {
            Seed = seed;
            Scheme = scheme;
            ContrastLevel = contrastLevel;
            IsDark = isDark;
            lightColors = Md3SchemeColors.Create(seed, scheme, false, contrastLevel);
            darkColors = Md3SchemeColors.Create(seed, scheme, true, contrastLevel);
        }

        /// <summary>底层角色值（诊断/预览用）。</summary>
        public Md3SchemeColors Colors(bool isDark) => isDark ? darkColors : lightColors;

        public static Color ToColor(uint argb) => Avalonia.Media.Color.FromArgb(
            (byte)((argb >> 24) & 255),
            (byte)((argb >> 16) & 255),
            (byte)((argb >> 8) & 255),
            (byte)(argb & 255));

        public Color Color(Md3Role role) => Color(role, IsDark);

        public Color Color(Md3Role role, bool isDark) => ToColor((isDark ? darkColors : lightColors).Get(role));

        public IBrush Brush(Md3Role role) => Brush(role, IsDark);

        public IBrush Brush(Md3Role role, bool isDark) {
            IBrush?[] cache = isDark ? darkBrushes : lightBrushes;
            int index = (int)role;
            IBrush? brush = cache[index];
            if (brush == null) {
                brush = new ImmutableSolidColorBrush(Color(role, isDark));
                cache[index] = brush;
            }
            return brush;
        }

        public IPen Pen(Md3Role role, double thickness = 1.0) {
            var key = (role, thickness, IsDark);
            if (!pens.TryGetValue(key, out IPen? pen)) {
                pen = new ImmutablePen(new ImmutableSolidColorBrush(Color(role)), thickness);
                pens[key] = pen;
            }
            return pen;
        }

        /// <summary>切换深浅色（由主题变更驱动）。</summary>
        public void SetDark(bool isDark) {
            if (IsDark == isDark) {
                return;
            }
            IsDark = isDark;
            pens.Clear();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>颜色池静态门面（与 ThemeManager 同风格），并提供 XAML 资源键。</summary>
    public static class ColorPool {
        /// <summary>默认种子：Material 3 基线紫（壁纸取色接入前的占位种子）。</summary>
        public const uint DefaultSeed = 0xFF6750A4;

        private static Md3ColorPool current =
            new Md3ColorPool(DefaultSeed, Md3SchemeVariant.TonalSpot, false);

        /// <summary>当前颜色池。任何控件都可以从这里取色。</summary>
        public static IMd3ColorPool Current => current;

        /// <summary>具体实现（含 Colors/Pen 等便利成员）。</summary>
        public static Md3ColorPool Instance => current;

        /// <summary>颜色变化（深浅色切换 / 重新着色）。</summary>
        public static event EventHandler? Changed {
            add => current.Changed += value;
            remove => current.Changed -= value;
        }

        /// <summary>重新着色：种子 / 方案 / 对比度变化时调用，并刷新 XAML 资源字典。</summary>
        public static void Initialize(uint seed, Md3SchemeVariant scheme, bool isDark, double contrastLevel = 0.0) {
            current = new Md3ColorPool(seed, scheme, isDark, contrastLevel);
            Md3ThemeResources.Install(current);
        }

        /// <summary>仅深浅色变化时调用（主题切换路径）；资源字典每次重建（幂等）。</summary>
        public static void SetDark(bool isDark) {
            current.SetDark(isDark);
            Md3ThemeResources.Install(current);
        }

        /// <summary>角色 → XAML 画刷资源键（如 md3.surface-container-highest）。</summary>
        public static string Key(Md3Role role) => "md3." + Kebab(role);

        /// <summary>角色 → XAML 颜色资源键（如 md3.color.surface-container-highest）。</summary>
        public static string ColorKey(Md3Role role) => "md3.color." + Kebab(role);

        private static string Kebab(Md3Role role) {
            string name = role.ToString();
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

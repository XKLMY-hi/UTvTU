using System;

namespace OpenUtau.Core.Theming {
    /// <summary>MD3 配色方案变体（对应参考实现的 Scheme* 类）。</summary>
    public enum Md3SchemeVariant {
        /// <summary>默认方案（Android 12/13 的 Material You）。</summary>
        TonalSpot,
        Vibrant,
        Expressive,
        Monochrome,
        Neutral,
        Rainbow,
        FruitSalad,
    }

    /// <summary>一次配色计算的全部上下文（移植自 dynamiccolor/dynamic_scheme.js）。</summary>
    internal sealed class Md3DynamicScheme {
        private readonly Md3TonalPalette[] palettes = new Md3TonalPalette[6];

        public Md3Hct SourceColorHct { get; }
        public Md3SchemeVariant Variant { get; }
        public bool IsDark { get; }
        public double ContrastLevel { get; }

        public Md3DynamicScheme(
            Md3Hct sourceColorHct,
            Md3SchemeVariant variant,
            bool isDark,
            double contrastLevel,
            Md3TonalPalette primary,
            Md3TonalPalette secondary,
            Md3TonalPalette tertiary,
            Md3TonalPalette neutral,
            Md3TonalPalette neutralVariant) {
            SourceColorHct = sourceColorHct;
            Variant = variant;
            IsDark = isDark;
            ContrastLevel = contrastLevel;
            palettes[(int)Md3PaletteKind.Primary] = primary;
            palettes[(int)Md3PaletteKind.Secondary] = secondary;
            palettes[(int)Md3PaletteKind.Tertiary] = tertiary;
            palettes[(int)Md3PaletteKind.Neutral] = neutral;
            palettes[(int)Md3PaletteKind.NeutralVariant] = neutralVariant;
            // 参考实现里 error 色板对所有方案固定为色相 25 / 彩度 84
            palettes[(int)Md3PaletteKind.Error] = Md3TonalPalette.FromHueAndChroma(25.0, 84.0);
        }

        public Md3TonalPalette Palette(Md3PaletteKind kind) => palettes[(int)kind];

        public static Md3DynamicScheme Create(uint seedArgb, Md3SchemeVariant variant, bool isDark, double contrastLevel = 0.0) {
            Md3Hct source = Md3Hct.FromInt(seedArgb);
            switch (variant) {
                case Md3SchemeVariant.Vibrant:
                    return new Md3DynamicScheme(source, variant, isDark, contrastLevel,
                        Md3TonalPalette.FromHueAndChroma(source.Hue, 200.0),
                        Md3TonalPalette.FromHueAndChroma(GetRotatedHue(source, VibrantHues, VibrantSecondaryRotations), 24.0),
                        Md3TonalPalette.FromHueAndChroma(GetRotatedHue(source, VibrantHues, VibrantTertiaryRotations), 32.0),
                        Md3TonalPalette.FromHueAndChroma(source.Hue, 10.0),
                        Md3TonalPalette.FromHueAndChroma(source.Hue, 12.0));
                case Md3SchemeVariant.Expressive:
                    return new Md3DynamicScheme(source, variant, isDark, contrastLevel,
                        Md3TonalPalette.FromHueAndChroma(Md3Math.SanitizeDegreesDouble(source.Hue + 240.0), 40.0),
                        Md3TonalPalette.FromHueAndChroma(GetRotatedHue(source, ExpressiveHues, ExpressiveSecondaryRotations), 24.0),
                        Md3TonalPalette.FromHueAndChroma(GetRotatedHue(source, ExpressiveHues, ExpressiveTertiaryRotations), 32.0),
                        Md3TonalPalette.FromHueAndChroma(source.Hue + 15, 8.0),
                        Md3TonalPalette.FromHueAndChroma(source.Hue + 15, 12.0));
                case Md3SchemeVariant.Monochrome:
                    return new Md3DynamicScheme(source, variant, isDark, contrastLevel,
                        Md3TonalPalette.FromHueAndChroma(source.Hue, 0.0),
                        Md3TonalPalette.FromHueAndChroma(source.Hue, 0.0),
                        Md3TonalPalette.FromHueAndChroma(source.Hue, 0.0),
                        Md3TonalPalette.FromHueAndChroma(source.Hue, 0.0),
                        Md3TonalPalette.FromHueAndChroma(source.Hue, 0.0));
                case Md3SchemeVariant.Neutral:
                    return new Md3DynamicScheme(source, variant, isDark, contrastLevel,
                        Md3TonalPalette.FromHueAndChroma(source.Hue, 12.0),
                        Md3TonalPalette.FromHueAndChroma(source.Hue, 8.0),
                        Md3TonalPalette.FromHueAndChroma(source.Hue, 16.0),
                        Md3TonalPalette.FromHueAndChroma(source.Hue, 2.0),
                        Md3TonalPalette.FromHueAndChroma(source.Hue, 2.0));
                case Md3SchemeVariant.Rainbow:
                    return new Md3DynamicScheme(source, variant, isDark, contrastLevel,
                        Md3TonalPalette.FromHueAndChroma(source.Hue, 48.0),
                        Md3TonalPalette.FromHueAndChroma(source.Hue, 16.0),
                        Md3TonalPalette.FromHueAndChroma(Md3Math.SanitizeDegreesDouble(source.Hue + 60.0), 24.0),
                        Md3TonalPalette.FromHueAndChroma(source.Hue, 0.0),
                        Md3TonalPalette.FromHueAndChroma(source.Hue, 0.0));
                case Md3SchemeVariant.FruitSalad:
                    return new Md3DynamicScheme(source, variant, isDark, contrastLevel,
                        Md3TonalPalette.FromHueAndChroma(Md3Math.SanitizeDegreesDouble(source.Hue - 50.0), 48.0),
                        Md3TonalPalette.FromHueAndChroma(Md3Math.SanitizeDegreesDouble(source.Hue - 50.0), 36.0),
                        Md3TonalPalette.FromHueAndChroma(source.Hue, 36.0),
                        Md3TonalPalette.FromHueAndChroma(source.Hue, 10.0),
                        Md3TonalPalette.FromHueAndChroma(source.Hue, 16.0));
                case Md3SchemeVariant.TonalSpot:
                default:
                    return new Md3DynamicScheme(source, variant, isDark, contrastLevel,
                        Md3TonalPalette.FromHueAndChroma(source.Hue, 36.0),
                        Md3TonalPalette.FromHueAndChroma(source.Hue, 16.0),
                        Md3TonalPalette.FromHueAndChroma(Md3Math.SanitizeDegreesDouble(source.Hue + 60.0), 24.0),
                        Md3TonalPalette.FromHueAndChroma(source.Hue, 6.0),
                        Md3TonalPalette.FromHueAndChroma(source.Hue, 8.0));
            }
        }

        private static readonly double[] VibrantHues = new double[] { 0.0, 41.0, 61.0, 101.0, 131.0, 181.0, 251.0, 301.0, 360.0 };
        private static readonly double[] VibrantSecondaryRotations = new double[] { 18.0, 15.0, 10.0, 12.0, 15.0, 18.0, 15.0, 12.0, 12.0 };
        private static readonly double[] VibrantTertiaryRotations = new double[] { 35.0, 30.0, 20.0, 25.0, 30.0, 35.0, 30.0, 25.0, 25.0 };

        private static readonly double[] ExpressiveHues = new double[] { 0.0, 21.0, 51.0, 121.0, 151.0, 191.0, 271.0, 321.0, 360.0 };
        private static readonly double[] ExpressiveSecondaryRotations = new double[] { 45.0, 95.0, 45.0, 20.0, 45.0, 90.0, 45.0, 45.0, 45.0 };
        private static readonly double[] ExpressiveTertiaryRotations = new double[] { 120.0, 120.0, 20.0, 45.0, 20.0, 15.0, 20.0, 120.0, 120.0 };

        private static double GetRotatedHue(Md3Hct sourceColor, double[] hues, double[] rotations) {
            double sourceHue = sourceColor.Hue;
            if (rotations.Length == 1) {
                return Md3Math.SanitizeDegreesDouble(sourceHue + rotations[0]);
            }
            int size = hues.Length;
            for (int i = 0; i <= size - 2; i++) {
                double thisHue = hues[i];
                double nextHue = hues[i + 1];
                if (thisHue < sourceHue && sourceHue < nextHue) {
                    return Md3Math.SanitizeDegreesDouble(sourceHue + rotations[i]);
                }
            }
            return sourceHue;
        }
    }

    /// <summary>一次配色结果的完整角色值（颜色池的数据层）。</summary>
    public sealed class Md3SchemeColors {
        private readonly uint[] values = new uint[Md3Roles.Count];

        public uint Seed { get; }
        public Md3SchemeVariant Variant { get; }
        public bool IsDark { get; }
        public double ContrastLevel { get; }

        private Md3SchemeColors(uint seed, Md3DynamicScheme scheme) {
            Seed = seed;
            Variant = scheme.Variant;
            IsDark = scheme.IsDark;
            ContrastLevel = scheme.ContrastLevel;
            for (int i = 0; i < values.Length; i++) {
                values[i] = Md3Roles.Get((Md3Role)i).GetArgb(scheme);
            }
        }

        public static Md3SchemeColors Create(uint seedArgb, Md3SchemeVariant variant, bool isDark, double contrastLevel = 0.0) {
            Md3DynamicScheme scheme = Md3DynamicScheme.Create(seedArgb, variant, isDark, contrastLevel);
            return new Md3SchemeColors(seedArgb, scheme);
        }

        public uint Get(Md3Role role) => values[(int)role];

        public string Hex(Md3Role role) => Md3Color.HexFromArgb(values[(int)role]);

        /// <summary>该角色在该配色下的实际 tone（诊断/测试用）。</summary>
        public double Tone(Md3Role role) => Md3Color.LstarFromArgb(values[(int)role]);

        public static int RoleCount => Md3Roles.Count;
    }
}

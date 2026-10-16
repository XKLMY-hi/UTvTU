using System;

namespace OpenUtau.Core.Theming {
    /// <summary>MD3 六个色调色板。</summary>
    public enum Md3PaletteKind {
        Primary,
        Secondary,
        Tertiary,
        Neutral,
        NeutralVariant,
        Error,
    }

    /// <summary>toneDeltaPair 的极性（移植自 dynamiccolor/tone_delta_pair.js 的语义）。</summary>
    internal enum Md3TonePolarity {
        Nearer,
        Lighter,
        Darker,
    }

    /// <summary>随对比度档位变化的取值曲线（移植自 dynamiccolor/contrast_curve.js）。</summary>
    internal sealed class Md3ContrastCurve {
        public double Low { get; }
        public double Normal { get; }
        public double Medium { get; }
        public double High { get; }

        public Md3ContrastCurve(double low, double normal, double medium, double high) {
            Low = low;
            Normal = normal;
            Medium = medium;
            High = high;
        }

        public double Get(double contrastLevel) {
            if (contrastLevel <= -1.0) {
                return Low;
            }
            if (contrastLevel < 0.0) {
                return Md3Math.Lerp(Low, Normal, (contrastLevel - (-1)) / 1);
            }
            if (contrastLevel < 0.5) {
                return Md3Math.Lerp(Normal, Medium, (contrastLevel - 0) / 0.5);
            }
            if (contrastLevel < 1.0) {
                return Md3Math.Lerp(Medium, High, (contrastLevel - 0.5) / 0.5);
            }
            return High;
        }
    }

    /// <summary>两个角色之间的 tone 间距约束（移植自 dynamiccolor/tone_delta_pair.js）。</summary>
    internal sealed class Md3ToneDeltaPair {
        public Md3Role RoleA { get; }
        public Md3Role RoleB { get; }
        public double Delta { get; }
        public Md3TonePolarity Polarity { get; }
        public bool StayTogether { get; }

        public Md3ToneDeltaPair(Md3Role roleA, Md3Role roleB, double delta, Md3TonePolarity polarity, bool stayTogether) {
            RoleA = roleA;
            RoleB = roleB;
            Delta = delta;
            Polarity = polarity;
            StayTogether = stayTogether;
        }
    }

    /// <summary>
    /// 单个 MD3 角色的规格（由 tools/md3-codegen 从参考实现内省生成）。
    /// tone 一律为对比度曲线（常量即四点相同）——参考实现中 surfaceDim 等角色的 tone 本身随对比度变化；
    /// Tone* 为常规配色方案，MonoTone* 为黑白方案覆盖值。
    /// </summary>
    internal sealed class Md3RoleSpec {
        public Md3Role Role { get; }
        public Md3PaletteKind Palette { get; }
        public bool IsBackground { get; }
        public Md3Role? BackgroundLight { get; }
        public Md3Role? BackgroundDark { get; }
        public Md3Role? SecondBackgroundLight { get; }
        public Md3Role? SecondBackgroundDark { get; }
        public Md3ContrastCurve? ContrastCurve { get; }
        public Md3ToneDeltaPair? ToneDeltaPair { get; }
        public Md3ContrastCurve ToneLight { get; }
        public Md3ContrastCurve ToneDark { get; }
        public Md3ContrastCurve MonoToneLight { get; }
        public Md3ContrastCurve MonoToneDark { get; }

        public Md3RoleSpec(
            Md3Role role,
            Md3PaletteKind palette,
            bool isBackground,
            Md3Role? backgroundLight,
            Md3Role? backgroundDark,
            Md3Role? secondBackgroundLight,
            Md3Role? secondBackgroundDark,
            Md3ContrastCurve? contrastCurve,
            Md3ToneDeltaPair? toneDeltaPair,
            Md3ContrastCurve toneLight,
            Md3ContrastCurve toneDark,
            Md3ContrastCurve monoToneLight,
            Md3ContrastCurve monoToneDark) {
            Role = role;
            Palette = palette;
            IsBackground = isBackground;
            BackgroundLight = backgroundLight;
            BackgroundDark = backgroundDark;
            SecondBackgroundLight = secondBackgroundLight;
            SecondBackgroundDark = secondBackgroundDark;
            ContrastCurve = contrastCurve;
            ToneDeltaPair = toneDeltaPair;
            ToneLight = toneLight;
            ToneDark = toneDark;
            MonoToneLight = monoToneLight;
            MonoToneDark = monoToneDark;
        }
    }
}

namespace OpenUtau.Core.Theming {
    /// <summary>动态颜色（移植自 dynamiccolor/dynamic_color.js）：按 scheme 求 tone，再落到色板取色。</summary>
    internal sealed class Md3DynamicColor {
        public Md3Role Role { get; }
        public Md3PaletteKind PaletteKind { get; }
        public bool IsBackground { get; }
        public Md3Role? BackgroundLight { get; }
        public Md3Role? BackgroundDark { get; }
        public Md3Role? SecondBackgroundLight { get; }
        public Md3Role? SecondBackgroundDark { get; }
        public Md3ContrastCurve? ContrastCurve { get; }
        public Md3ToneDeltaPair? DeltaPair { get; }
        public Md3ContrastCurve ToneLight { get; }
        public Md3ContrastCurve ToneDark { get; }
        public Md3ContrastCurve MonoToneLight { get; }
        public Md3ContrastCurve MonoToneDark { get; }

        public Md3DynamicColor(Md3RoleSpec spec) {
            Role = spec.Role;
            PaletteKind = spec.Palette;
            IsBackground = spec.IsBackground;
            BackgroundLight = spec.BackgroundLight;
            BackgroundDark = spec.BackgroundDark;
            SecondBackgroundLight = spec.SecondBackgroundLight;
            SecondBackgroundDark = spec.SecondBackgroundDark;
            ContrastCurve = spec.ContrastCurve;
            DeltaPair = spec.ToneDeltaPair;
            ToneLight = spec.ToneLight;
            ToneDark = spec.ToneDark;
            MonoToneLight = spec.MonoToneLight;
            MonoToneDark = spec.MonoToneDark;
        }

        /// <summary>原始 tone（未经背景对比/toneDeltaPair 调整）。</summary>
        public double Tone(Md3DynamicScheme scheme) {
            Md3ContrastCurve curve = scheme.Variant == Md3SchemeVariant.Monochrome
                ? (scheme.IsDark ? MonoToneDark : MonoToneLight)
                : (scheme.IsDark ? ToneDark : ToneLight);
            return curve.Get(scheme.ContrastLevel);
        }

        public Md3DynamicColor? Background(Md3DynamicScheme scheme) {
            Md3Role? role = scheme.IsDark ? BackgroundDark : BackgroundLight;
            return role.HasValue ? Md3Roles.Get(role.Value) : null;
        }

        public Md3DynamicColor? SecondBackground(Md3DynamicScheme scheme) {
            Md3Role? role = scheme.IsDark ? SecondBackgroundDark : SecondBackgroundLight;
            return role.HasValue ? Md3Roles.Get(role.Value) : null;
        }

        public Md3Hct GetHct(Md3DynamicScheme scheme) => scheme.Palette(PaletteKind).GetHct(GetTone(scheme));

        public uint GetArgb(Md3DynamicScheme scheme) => GetHct(scheme).ToInt();

        /// <summary>最终 tone（含 toneDeltaPair 与背景对比调整），逐行对应参考实现 getTone()。</summary>
        public double GetTone(Md3DynamicScheme scheme) {
            bool decreasingContrast = scheme.ContrastLevel < 0;
            // 情形 1：带 tone 间距约束的角色对
            if (DeltaPair != null) {
                Md3ToneDeltaPair pair = DeltaPair;
                Md3DynamicColor roleA = Md3Roles.Get(pair.RoleA);
                Md3DynamicColor roleB = Md3Roles.Get(pair.RoleB);
                double delta = pair.Delta;
                Md3DynamicColor bg = Background(scheme)!;
                double bgTone = bg.GetTone(scheme);
                bool aIsNearer = pair.Polarity == Md3TonePolarity.Nearer
                    || (pair.Polarity == Md3TonePolarity.Lighter && !scheme.IsDark)
                    || (pair.Polarity == Md3TonePolarity.Darker && scheme.IsDark);
                Md3DynamicColor nearer = aIsNearer ? roleA : roleB;
                Md3DynamicColor farther = aIsNearer ? roleB : roleA;
                bool amNearer = Role == nearer.Role;
                double expansionDir = scheme.IsDark ? 1 : -1;
                double nContrast = nearer.ContrastCurve!.Get(scheme.ContrastLevel);
                double fContrast = farther.ContrastCurve!.Get(scheme.ContrastLevel);
                double nInitialTone = nearer.Tone(scheme);
                double nTone = Md3Contrast.RatioOfTones(bgTone, nInitialTone) >= nContrast
                    ? nInitialTone
                    : ForegroundTone(bgTone, nContrast);
                double fInitialTone = farther.Tone(scheme);
                double fTone = Md3Contrast.RatioOfTones(bgTone, fInitialTone) >= fContrast
                    ? fInitialTone
                    : ForegroundTone(bgTone, fContrast);
                if (decreasingContrast) {
                    nTone = ForegroundTone(bgTone, nContrast);
                    fTone = ForegroundTone(bgTone, fContrast);
                }
                if ((fTone - nTone) * expansionDir >= delta) {
                    // 已满足约束
                } else {
                    fTone = Md3Math.ClampDouble(0, 100, nTone + delta * expansionDir);
                    if ((fTone - nTone) * expansionDir >= delta) {
                        // 已满足约束
                    } else {
                        nTone = Md3Math.ClampDouble(0, 100, fTone - delta * expansionDir);
                    }
                }
                if (50 <= nTone && nTone < 60) {
                    if (expansionDir > 0) {
                        nTone = 60;
                        fTone = System.Math.Max(fTone, nTone + delta * expansionDir);
                    } else {
                        nTone = 49;
                        fTone = System.Math.Min(fTone, nTone + delta * expansionDir);
                    }
                } else if (50 <= fTone && fTone < 60) {
                    if (pair.StayTogether) {
                        if (expansionDir > 0) {
                            nTone = 60;
                            fTone = System.Math.Max(fTone, nTone + delta * expansionDir);
                        } else {
                            nTone = 49;
                            fTone = System.Math.Min(fTone, nTone + delta * expansionDir);
                        }
                    } else {
                        fTone = expansionDir > 0 ? 60 : 49;
                    }
                }
                return amNearer ? nTone : fTone;
            }

            // 情形 2：无约束，仅按自身背景做对比调整
            double answer = Tone(scheme);
            Md3DynamicColor? background = Background(scheme);
            if (background == null) {
                return answer;
            }
            double bgTone2 = background.GetTone(scheme);
            double desiredRatio = ContrastCurve!.Get(scheme.ContrastLevel);
            if (Md3Contrast.RatioOfTones(bgTone2, answer) >= desiredRatio) {
                // 已达标，不"优化"
            } else {
                answer = ForegroundTone(bgTone2, desiredRatio);
            }
            if (decreasingContrast) {
                answer = ForegroundTone(bgTone2, desiredRatio);
            }
            if (IsBackground && 50 <= answer && answer < 60) {
                answer = Md3Contrast.RatioOfTones(49, bgTone2) >= desiredRatio ? 49 : 60;
            }

            // 情形 3：双背景
            Md3DynamicColor? second = SecondBackground(scheme);
            if (second != null) {
                double bgTone1 = background.GetTone(scheme);
                double bgToneOther = second.GetTone(scheme);
                double upper = System.Math.Max(bgTone1, bgToneOther);
                double lower = System.Math.Min(bgTone1, bgToneOther);
                if (Md3Contrast.RatioOfTones(upper, answer) >= desiredRatio &&
                    Md3Contrast.RatioOfTones(lower, answer) >= desiredRatio) {
                    return answer;
                }
                double lightOption = Md3Contrast.Lighter(upper, desiredRatio);
                double darkOption = Md3Contrast.Darker(lower, desiredRatio);
                var availables = new System.Collections.Generic.List<double>();
                if (lightOption != -1) {
                    availables.Add(lightOption);
                }
                if (darkOption != -1) {
                    availables.Add(darkOption);
                }
                bool prefersLight = TonePrefersLightForeground(bgTone1) || TonePrefersLightForeground(bgToneOther);
                if (prefersLight) {
                    return lightOption < 0 ? 100 : lightOption;
                }
                if (availables.Count == 1) {
                    return availables[0];
                }
                return darkOption < 0 ? 0 : darkOption;
            }
            return answer;
        }

        /// <summary>给定背景 tone，求满足对比度 ratio 的前景 tone（移植自 foregroundTone）。</summary>
        public static double ForegroundTone(double bgTone, double ratio) {
            double lighterTone = Md3Contrast.LighterUnsafe(bgTone, ratio);
            double darkerTone = Md3Contrast.DarkerUnsafe(bgTone, ratio);
            double lighterRatio = Md3Contrast.RatioOfTones(lighterTone, bgTone);
            double darkerRatio = Md3Contrast.RatioOfTones(darkerTone, bgTone);
            bool preferLighter = TonePrefersLightForeground(bgTone);
            if (preferLighter) {
                bool negligibleDifference = System.Math.Abs(lighterRatio - darkerRatio) < 0.1 &&
                    lighterRatio < ratio && darkerRatio < ratio;
                return lighterRatio >= ratio || lighterRatio >= darkerRatio || negligibleDifference
                    ? lighterTone
                    : darkerTone;
            }
            return darkerRatio >= ratio || darkerRatio >= lighterRatio ? darkerTone : lighterTone;
        }

        public static bool TonePrefersLightForeground(double tone) => Md3Math.JsRound(tone) < 60.0;

        public static bool ToneAllowsLightForeground(double tone) => Md3Math.JsRound(tone) <= 49.0;

        public static double EnableLightForeground(double tone) =>
            TonePrefersLightForeground(tone) && !ToneAllowsLightForeground(tone) ? 49.0 : tone;
    }

    /// <summary>49 个 MD3 角色的实例集合（规格表由代码生成，见 Md3RoleTable.g.cs）。</summary>
    internal static class Md3Roles {
        private static readonly Md3DynamicColor[] all;

        static Md3Roles() {
            Md3RoleSpec[] specs = Md3RoleTable.Roles;
            all = new Md3DynamicColor[specs.Length];
            for (int i = 0; i < specs.Length; i++) {
                all[i] = new Md3DynamicColor(specs[i]);
            }
        }

        public static Md3DynamicColor Get(Md3Role role) => all[(int)role];

        public static int Count => all.Length;
    }
}

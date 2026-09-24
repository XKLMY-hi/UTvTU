using System;
using System.Collections.Generic;

namespace OpenUtau.Core.Theming {
    /// <summary>HCT 色彩空间（移植自 hct/hct.js）：CAM16 的色相/彩度 + Lab 的 L*（tone）。</summary>
    internal sealed class Md3Hct {
        public uint Argb { get; private set; }
        public double Hue { get; private set; }
        public double Chroma { get; private set; }
        public double Tone { get; private set; }

        public static Md3Hct From(double hue, double chroma, double tone) =>
            new Md3Hct(Md3HctSolver.SolveToInt(hue, chroma, tone));

        public static Md3Hct FromInt(uint argb) => new Md3Hct(argb);

        private Md3Hct(uint argb) => SetInternalState(argb);

        private void SetInternalState(uint argb) {
            Md3Cam16 cam = Md3Cam16.FromInt(argb);
            Hue = cam.Hue;
            Chroma = cam.Chroma;
            Tone = Md3Color.LstarFromArgb(argb);
            Argb = argb;
        }

        public uint ToInt() => Argb;
    }

    /// <summary>HCT 求解器（逐行移植自 hct/hct_solver.js）：由 色相/彩度/tone 反解 sRGB 颜色。</summary>
    internal static class Md3HctSolver {
        private static double SanitizeRadians(double angle) => (angle + Math.PI * 8) % (Math.PI * 2);

        private static double TrueDelinearized(double rgbComponent) {
            double normalized = rgbComponent / 100.0;
            double delinearized;
            if (normalized <= 0.0031308) {
                delinearized = normalized * 12.92;
            } else {
                delinearized = 1.055 * Math.Pow(normalized, 1.0 / 2.4) - 0.055;
            }
            return delinearized * 255.0;
        }

        private static double ChromaticAdaptation(double component) {
            double af = Math.Pow(Math.Abs(component), 0.42);
            return Md3Math.Signum(component) * 400.0 * af / (af + 27.13);
        }

        private static double HueOf(double[] linrgb) {
            double[] scaledDiscount = Md3Math.MatrixMultiply(linrgb, Md3HctTables.ScaledDiscountFromLinrgb);
            double rA = ChromaticAdaptation(scaledDiscount[0]);
            double gA = ChromaticAdaptation(scaledDiscount[1]);
            double bA = ChromaticAdaptation(scaledDiscount[2]);
            double a = (11.0 * rA + -12.0 * gA + bA) / 11.0;
            double b = (rA + gA - 2.0 * bA) / 9.0;
            return Math.Atan2(b, a);
        }

        private static bool AreInCyclicOrder(double a, double b, double c) {
            double deltaAB = SanitizeRadians(b - a);
            double deltaAC = SanitizeRadians(c - a);
            return deltaAB < deltaAC;
        }

        private static double Intercept(double source, double mid, double target) =>
            (mid - source) / (target - source);

        private static double[] LerpPoint(double[] source, double t, double[] target) => new double[] {
            source[0] + (target[0] - source[0]) * t,
            source[1] + (target[1] - source[1]) * t,
            source[2] + (target[2] - source[2]) * t,
        };

        private static double[] SetCoordinate(double[] source, double coordinate, double[] target, int axis) {
            double t = Intercept(source[axis], coordinate, target[axis]);
            return LerpPoint(source, t, target);
        }

        private static bool IsBounded(double x) => 0.0 <= x && x <= 100.0;

        private static double[] NthVertex(double y, int n) {
            double kR = Md3HctTables.YFromLinrgb[0];
            double kG = Md3HctTables.YFromLinrgb[1];
            double kB = Md3HctTables.YFromLinrgb[2];
            double coordA = n % 4 <= 1 ? 0.0 : 100.0;
            double coordB = n % 2 == 0 ? 0.0 : 100.0;
            if (n < 4) {
                double g = coordA;
                double b = coordB;
                double r = (y - g * kG - b * kB) / kR;
                return IsBounded(r) ? new double[] { r, g, b } : new double[] { -1.0, -1.0, -1.0 };
            }
            if (n < 8) {
                double b = coordA;
                double r = coordB;
                double g = (y - r * kR - b * kB) / kG;
                return IsBounded(g) ? new double[] { r, g, b } : new double[] { -1.0, -1.0, -1.0 };
            }
            double rr = coordA;
            double gg = coordB;
            double bb = (y - rr * kR - gg * kG) / kB;
            return IsBounded(bb) ? new double[] { rr, gg, bb } : new double[] { -1.0, -1.0, -1.0 };
        }

        private static double[][] BisectToSegment(double y, double targetHue) {
            double[] left = new double[] { -1.0, -1.0, -1.0 };
            double[] right = left;
            double leftHue = 0.0;
            double rightHue = 0.0;
            bool initialized = false;
            bool uncut = true;
            for (int n = 0; n < 12; n++) {
                double[] mid = NthVertex(y, n);
                if (mid[0] < 0) {
                    continue;
                }
                double midHue = HueOf(mid);
                if (!initialized) {
                    left = mid;
                    right = mid;
                    leftHue = midHue;
                    rightHue = midHue;
                    initialized = true;
                    continue;
                }
                if (uncut || AreInCyclicOrder(leftHue, midHue, rightHue)) {
                    uncut = false;
                    if (AreInCyclicOrder(leftHue, targetHue, midHue)) {
                        right = mid;
                        rightHue = midHue;
                    } else {
                        left = mid;
                        leftHue = midHue;
                    }
                }
            }
            return new double[][] { left, right };
        }

        private static double[] Midpoint(double[] a, double[] b) => new double[] {
            (a[0] + b[0]) / 2,
            (a[1] + b[1]) / 2,
            (a[2] + b[2]) / 2,
        };

        private static int CriticalPlaneBelow(double x) => (int)Math.Floor(x - 0.5);

        private static int CriticalPlaneAbove(double x) => (int)Math.Ceiling(x - 0.5);

        private static double[] BisectToLimit(double y, double targetHue) {
            double[][] segment = BisectToSegment(y, targetHue);
            double[] left = segment[0];
            double leftHue = HueOf(left);
            double[] right = segment[1];
            for (int axis = 0; axis < 3; axis++) {
                if (left[axis] != right[axis]) {
                    int lPlane;
                    int rPlane;
                    if (left[axis] < right[axis]) {
                        lPlane = CriticalPlaneBelow(TrueDelinearized(left[axis]));
                        rPlane = CriticalPlaneAbove(TrueDelinearized(right[axis]));
                    } else {
                        lPlane = CriticalPlaneAbove(TrueDelinearized(left[axis]));
                        rPlane = CriticalPlaneBelow(TrueDelinearized(right[axis]));
                    }
                    for (int i = 0; i < 8; i++) {
                        if (Math.Abs(rPlane - lPlane) <= 1) {
                            break;
                        }
                        int mPlane = (int)Math.Floor((lPlane + rPlane) / 2.0);
                        double midPlaneCoordinate = Md3HctTables.CriticalPlanes[mPlane];
                        double[] mid = SetCoordinate(left, midPlaneCoordinate, right, axis);
                        double midHue = HueOf(mid);
                        if (AreInCyclicOrder(leftHue, targetHue, midHue)) {
                            right = mid;
                            rPlane = mPlane;
                        } else {
                            left = mid;
                            leftHue = midHue;
                            lPlane = mPlane;
                        }
                    }
                }
            }
            return Midpoint(left, right);
        }

        private static double InverseChromaticAdaptation(double adapted) {
            double adaptedAbs = Math.Abs(adapted);
            double baseValue = Math.Max(0, 27.13 * adaptedAbs / (400.0 - adaptedAbs));
            return Md3Math.Signum(adapted) * Math.Pow(baseValue, 1.0 / 0.42);
        }

        private static uint FindResultByJ(double hueRadians, double chroma, double y) {
            double j = Math.Sqrt(y) * 11.0;
            Md3ViewingConditions viewingConditions = Md3ViewingConditions.Default;
            double tInnerCoeff = 1 / Math.Pow(1.64 - Math.Pow(0.29, viewingConditions.N), 0.73);
            double eHue = 0.25 * (Math.Cos(hueRadians + 2.0) + 3.8);
            double p1 = eHue * (50000.0 / 13.0) * viewingConditions.Nc * viewingConditions.Ncb;
            double hSin = Math.Sin(hueRadians);
            double hCos = Math.Cos(hueRadians);
            for (int iterationRound = 0; iterationRound < 5; iterationRound++) {
                double jNormalized = j / 100.0;
                double alpha = chroma == 0.0 || j == 0.0 ? 0.0 : chroma / Math.Sqrt(jNormalized);
                double t = Math.Pow(alpha * tInnerCoeff, 1.0 / 0.9);
                double ac = viewingConditions.Aw *
                    Math.Pow(jNormalized, 1.0 / viewingConditions.C / viewingConditions.Z);
                double p2 = ac / viewingConditions.Nbb;
                double gamma = 23.0 * (p2 + 0.305) * t /
                    (23.0 * p1 + 11 * t * hCos + 108.0 * t * hSin);
                double a = gamma * hCos;
                double b = gamma * hSin;
                double rA = (460.0 * p2 + 451.0 * a + 288.0 * b) / 1403.0;
                double gA = (460.0 * p2 - 891.0 * a - 261.0 * b) / 1403.0;
                double bA = (460.0 * p2 - 220.0 * a - 6300.0 * b) / 1403.0;
                double rCScaled = InverseChromaticAdaptation(rA);
                double gCScaled = InverseChromaticAdaptation(gA);
                double bCScaled = InverseChromaticAdaptation(bA);
                double[] linrgb = Md3Math.MatrixMultiply(
                    new double[] { rCScaled, gCScaled, bCScaled }, Md3HctTables.LinrgbFromScaledDiscount);
                if (linrgb[0] < 0 || linrgb[1] < 0 || linrgb[2] < 0) {
                    return 0;
                }
                double kR = Md3HctTables.YFromLinrgb[0];
                double kG = Md3HctTables.YFromLinrgb[1];
                double kB = Md3HctTables.YFromLinrgb[2];
                double fnj = kR * linrgb[0] + kG * linrgb[1] + kB * linrgb[2];
                if (fnj <= 0) {
                    return 0;
                }
                if (iterationRound == 4 || Math.Abs(fnj - y) < 0.002) {
                    if (linrgb[0] > 100.01 || linrgb[1] > 100.01 || linrgb[2] > 100.01) {
                        return 0;
                    }
                    return Md3Color.ArgbFromLinrgb(linrgb);
                }
                j -= (fnj - y) * j / (2 * fnj);
            }
            return 0;
        }

        public static uint SolveToInt(double hueDegrees, double chroma, double lstar) {
            if (chroma < 0.0001 || lstar < 0.0001 || lstar > 99.9999) {
                return Md3Color.ArgbFromLstar(lstar);
            }
            hueDegrees = Md3Math.SanitizeDegreesDouble(hueDegrees);
            double hueRadians = hueDegrees / 180 * Math.PI;
            double y = Md3Color.YFromLstar(lstar);
            uint exactAnswer = FindResultByJ(hueRadians, chroma, y);
            if (exactAnswer != 0) {
                return exactAnswer;
            }
            double[] linrgb = BisectToLimit(y, hueRadians);
            return Md3Color.ArgbFromLinrgb(linrgb);
        }

        public static Md3Cam16 SolveToCam(double hueDegrees, double chroma, double lstar) =>
            Md3Cam16.FromInt(SolveToInt(hueDegrees, chroma, lstar));
    }

    /// <summary>色调色板（移植自 palettes/tonal_palette.js）：固定色相/彩度，按 tone 取色。</summary>
    internal sealed class Md3TonalPalette {
        public double Hue { get; }
        public double Chroma { get; }
        public Md3Hct KeyColor { get; }

        private readonly Dictionary<double, uint> cache = new Dictionary<double, uint>();

        public static Md3TonalPalette FromInt(uint argb) => FromHct(Md3Hct.FromInt(argb));

        public static Md3TonalPalette FromHct(Md3Hct hct) => new Md3TonalPalette(hct.Hue, hct.Chroma, hct);

        public static Md3TonalPalette FromHueAndChroma(double hue, double chroma) =>
            new Md3TonalPalette(hue, chroma, new KeyColorFinder(hue, chroma).Create());

        private Md3TonalPalette(double hue, double chroma, Md3Hct keyColor) {
            Hue = hue;
            Chroma = chroma;
            KeyColor = keyColor;
        }

        public uint Tone(double tone) {
            if (!cache.TryGetValue(tone, out uint argb)) {
                argb = Md3Hct.From(Hue, Chroma, tone).ToInt();
                cache[tone] = argb;
            }
            return argb;
        }

        public Md3Hct GetHct(double tone) => Md3Hct.FromInt(Tone(tone));

        /// <summary>可达到指定彩度的、最接近 tone 50 的色调（移植自 TonalPalette 内部 KeyColor）。</summary>
        private sealed class KeyColorFinder {
            private const double MaxChromaValue = 200.0;
            private readonly double hue;
            private readonly double requestedChroma;
            private readonly Dictionary<int, double> chromaCache = new Dictionary<int, double>();

            public KeyColorFinder(double hue, double requestedChroma) {
                this.hue = hue;
                this.requestedChroma = requestedChroma;
            }

            public Md3Hct Create() {
                const int pivotTone = 50;
                const int toneStepSize = 1;
                const double epsilon = 0.01;
                int lowerTone = 0;
                int upperTone = 100;
                while (lowerTone < upperTone) {
                    int midTone = (int)Math.Floor((lowerTone + upperTone) / 2.0);
                    bool isAscending = MaxChroma(midTone) < MaxChroma(midTone + toneStepSize);
                    bool sufficientChroma = MaxChroma(midTone) >= requestedChroma - epsilon;
                    if (sufficientChroma) {
                        if (Math.Abs(lowerTone - pivotTone) < Math.Abs(upperTone - pivotTone)) {
                            upperTone = midTone;
                        } else {
                            if (lowerTone == midTone) {
                                return Md3Hct.From(hue, requestedChroma, lowerTone);
                            }
                            lowerTone = midTone;
                        }
                    } else {
                        if (isAscending) {
                            lowerTone = midTone + toneStepSize;
                        } else {
                            upperTone = midTone;
                        }
                    }
                }
                return Md3Hct.From(hue, requestedChroma, lowerTone);
            }

            private double MaxChroma(int tone) {
                if (chromaCache.TryGetValue(tone, out double cached)) {
                    return cached;
                }
                double chroma = Md3Hct.From(hue, MaxChromaValue, tone).Chroma;
                chromaCache[tone] = chroma;
                return chroma;
            }
        }
    }
}

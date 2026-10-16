using System;

namespace OpenUtau.Core.Theming {
    /// <summary>
    /// MD3 颜色数学工具。逐行移植自 Google material-color-utilities@0.3.0
    /// （utils/math_utils.js、utils/color_utils.js、contrast/contrast.js）。
    /// 注意：JS 的 Math.round 为"四舍五入向上"，此处统一用 <see cref="JsRound"/> 保持逐位一致。
    /// </summary>
    internal static class Md3Math {
        /// <summary>JS Math.round 语义（.5 向上取整），C# Math.Round 默认是银行家舍入，不能直接用。</summary>
        public static int JsRound(double value) => (int)Math.Floor(value + 0.5);

        public static int Signum(double num) => num < 0 ? -1 : num == 0 ? 0 : 1;

        public static double Lerp(double start, double stop, double amount) => (1.0 - amount) * start + amount * stop;

        public static double ClampDouble(double min, double max, double input) =>
            input < min ? min : input > max ? max : input;

        public static int ClampInt(int min, int max, int input) =>
            input < min ? min : input > max ? max : input;

        public static double SanitizeDegreesDouble(double degrees) {
            degrees %= 360.0;
            if (degrees < 0) {
                degrees += 360.0;
            }
            return degrees;
        }

        public static double[] MatrixMultiply(double[] row, double[][] matrix) => new double[] {
            row[0] * matrix[0][0] + row[1] * matrix[0][1] + row[2] * matrix[0][2],
            row[0] * matrix[1][0] + row[1] * matrix[1][1] + row[2] * matrix[1][2],
            row[0] * matrix[2][0] + row[1] * matrix[2][1] + row[2] * matrix[2][2],
        };
    }

    /// <summary>ARGB / 线性 RGB / XYZ / Lab / L* 换算（移植自 utils/color_utils.js）。</summary>
    internal static class Md3Color {
        private static readonly double[][] SrgbToXyz = new double[][] {
            new double[] { 0.41233895, 0.35762064, 0.18051042 },
            new double[] { 0.2126, 0.7152, 0.0722 },
            new double[] { 0.01932141, 0.11916382, 0.95034478 },
        };

        private static readonly double[][] XyzToSrgb = new double[][] {
            new double[] { 3.2413774792388685, -1.5376652402851851, -0.49885366846268053 },
            new double[] { -0.9691452513005321, 1.8758853451067872, 0.04156585616912061 },
            new double[] { 0.05562093689691305, -0.20395524564742123, 1.0571799111220335 },
        };

        public static readonly double[] WhitePointD65 = new double[] { 95.047, 100.0, 108.883 };

        public static uint ArgbFromRgb(int red, int green, int blue) =>
            (uint)((255 << 24) | ((red & 255) << 16) | ((green & 255) << 8) | (blue & 255));

        public static uint ArgbFromLinrgb(double[] linrgb) =>
            ArgbFromRgb(Delinearized(linrgb[0]), Delinearized(linrgb[1]), Delinearized(linrgb[2]));

        public static int AlphaFromArgb(uint argb) => (int)((argb >> 24) & 255);
        public static int RedFromArgb(uint argb) => (int)((argb >> 16) & 255);
        public static int GreenFromArgb(uint argb) => (int)((argb >> 8) & 255);
        public static int BlueFromArgb(uint argb) => (int)(argb & 255);
        public static bool IsOpaque(uint argb) => AlphaFromArgb(argb) >= 255;

        public static double[] XyzFromArgb(uint argb) {
            double r = Linearized(RedFromArgb(argb));
            double g = Linearized(GreenFromArgb(argb));
            double b = Linearized(BlueFromArgb(argb));
            return Md3Math.MatrixMultiply(new double[] { r, g, b }, SrgbToXyz);
        }

        public static uint ArgbFromXyz(double x, double y, double z) {
            double linearR = XyzToSrgb[0][0] * x + XyzToSrgb[0][1] * y + XyzToSrgb[0][2] * z;
            double linearG = XyzToSrgb[1][0] * x + XyzToSrgb[1][1] * y + XyzToSrgb[1][2] * z;
            double linearB = XyzToSrgb[2][0] * x + XyzToSrgb[2][1] * y + XyzToSrgb[2][2] * z;
            return ArgbFromRgb(Delinearized(linearR), Delinearized(linearG), Delinearized(linearB));
        }

        public static uint ArgbFromLab(double l, double a, double b) {
            double[] whitePoint = WhitePointD65;
            double fy = (l + 16.0) / 116.0;
            double fx = a / 500.0 + fy;
            double fz = fy - b / 200.0;
            double xNormalized = LabInvf(fx);
            double yNormalized = LabInvf(fy);
            double zNormalized = LabInvf(fz);
            return ArgbFromXyz(xNormalized * whitePoint[0], yNormalized * whitePoint[1], zNormalized * whitePoint[2]);
        }

        public static double[] LabFromArgb(uint argb) {
            double linearR = Linearized(RedFromArgb(argb));
            double linearG = Linearized(GreenFromArgb(argb));
            double linearB = Linearized(BlueFromArgb(argb));
            double x = SrgbToXyz[0][0] * linearR + SrgbToXyz[0][1] * linearG + SrgbToXyz[0][2] * linearB;
            double y = SrgbToXyz[1][0] * linearR + SrgbToXyz[1][1] * linearG + SrgbToXyz[1][2] * linearB;
            double z = SrgbToXyz[2][0] * linearR + SrgbToXyz[2][1] * linearG + SrgbToXyz[2][2] * linearB;
            double[] whitePoint = WhitePointD65;
            double fx = LabF(x / whitePoint[0]);
            double fy = LabF(y / whitePoint[1]);
            double fz = LabF(z / whitePoint[2]);
            return new double[] { 116.0 * fy - 16, 500.0 * (fx - fy), 200.0 * (fy - fz) };
        }

        public static uint ArgbFromLstar(double lstar) {
            double y = YFromLstar(lstar);
            int component = Delinearized(y);
            return ArgbFromRgb(component, component, component);
        }

        public static double LstarFromArgb(uint argb) => 116.0 * LabF(XyzFromArgb(argb)[1] / 100.0) - 16.0;

        public static double YFromLstar(double lstar) => 100.0 * LabInvf((lstar + 16.0) / 116.0);

        public static double LstarFromY(double y) => LabF(y / 100.0) * 116.0 - 16.0;

        public static double Linearized(int rgbComponent) {
            double normalized = rgbComponent / 255.0;
            if (normalized <= 0.040449936) {
                return normalized / 12.92 * 100.0;
            }
            return Math.Pow((normalized + 0.055) / 1.055, 2.4) * 100.0;
        }

        public static int Delinearized(double rgbComponent) {
            double normalized = rgbComponent / 100.0;
            double delinearized;
            if (normalized <= 0.0031308) {
                delinearized = normalized * 12.92;
            } else {
                delinearized = 1.055 * Math.Pow(normalized, 1.0 / 2.4) - 0.055;
            }
            return Md3Math.ClampInt(0, 255, Md3Math.JsRound(delinearized * 255.0));
        }

        public static string HexFromArgb(uint argb) =>
            $"#{RedFromArgb(argb):x2}{GreenFromArgb(argb):x2}{BlueFromArgb(argb):x2}";

        private static double LabF(double t) {
            const double e = 216.0 / 24389.0;
            const double kappa = 24389.0 / 27.0;
            if (t > e) {
                return Math.Pow(t, 1.0 / 3.0);
            }
            return (kappa * t + 16) / 116;
        }

        private static double LabInvf(double ft) {
            const double e = 216.0 / 24389.0;
            const double kappa = 24389.0 / 27.0;
            double ft3 = ft * ft * ft;
            if (ft3 > e) {
                return ft3;
            }
            return (116 * ft - 16) / kappa;
        }
    }

    /// <summary>对比度工具（移植自 contrast/contrast.js）。</summary>
    internal static class Md3Contrast {
        public static double RatioOfTones(double toneA, double toneB) {
            toneA = Md3Math.ClampDouble(0.0, 100.0, toneA);
            toneB = Md3Math.ClampDouble(0.0, 100.0, toneB);
            return RatioOfYs(Md3Color.YFromLstar(toneA), Md3Color.YFromLstar(toneB));
        }

        public static double RatioOfYs(double y1, double y2) {
            double lighter = Math.Max(y1, y2);
            double darker = Math.Min(y1, y2);
            return (lighter + 5.0) / (darker + 5.0);
        }

        /// <summary>返回不小于 tone 且满足 ratio 的 tone；无法满足返回 -1。</summary>
        public static double Lighter(double tone, double ratio) {
            if (tone < 0.0 || tone > 100.0) {
                return -1.0;
            }
            double darkY = Md3Color.YFromLstar(tone);
            double lightY = ratio * (darkY + 5.0) - 5.0;
            double realContrast = RatioOfYs(lightY, darkY);
            double delta = Math.Abs(realContrast - ratio);
            if (realContrast < ratio && delta > 0.04) {
                return -1;
            }
            double returnValue = Md3Color.LstarFromY(lightY) + 0.4;
            if (returnValue < 0 || returnValue > 100) {
                return -1;
            }
            return returnValue;
        }

        /// <summary>返回不大于 tone 且满足 ratio 的 tone；无法满足返回 -1。</summary>
        public static double Darker(double tone, double ratio) {
            if (tone < 0.0 || tone > 100.0) {
                return -1.0;
            }
            double lightY = Md3Color.YFromLstar(tone);
            double darkY = ((lightY + 5.0) / ratio) - 5.0;
            double realContrast = RatioOfYs(lightY, darkY);
            double delta = Math.Abs(realContrast - ratio);
            if (realContrast < ratio && delta > 0.04) {
                return -1;
            }
            double returnValue = Md3Color.LstarFromY(darkY) - 0.4;
            if (returnValue < 0 || returnValue > 100) {
                return -1;
            }
            return returnValue;
        }

        public static double LighterUnsafe(double tone, double ratio) {
            double lighterSafe = Lighter(tone, ratio);
            return lighterSafe < 0.0 ? 100.0 : lighterSafe;
        }

        public static double DarkerUnsafe(double tone, double ratio) {
            double darkerSafe = Darker(tone, ratio);
            return darkerSafe < 0.0 ? 0.0 : darkerSafe;
        }
    }
}

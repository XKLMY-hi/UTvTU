using System;

namespace OpenUtau.Core.Theming {
    /// <summary>观看条件（移植自 hct/viewing_conditions.js）。</summary>
    internal sealed class Md3ViewingConditions {
        public double N { get; }
        public double Aw { get; }
        public double Nbb { get; }
        public double Ncb { get; }
        public double C { get; }
        public double Nc { get; }
        public double[] RgbD { get; }
        public double Fl { get; }
        public double FlRoot { get; }
        public double Z { get; }

        private Md3ViewingConditions(double n, double aw, double nbb, double ncb, double c, double nc,
            double[] rgbD, double fl, double flRoot, double z) {
            N = n;
            Aw = aw;
            Nbb = nbb;
            Ncb = ncb;
            C = c;
            Nc = nc;
            RgbD = rgbD;
            Fl = fl;
            FlRoot = flRoot;
            Z = z;
        }

        public static Md3ViewingConditions Make(
            double[]? whitePoint = null,
            double? adaptingLuminance = null,
            double backgroundLstar = 50.0,
            double surround = 2.0,
            bool discountingIlluminant = false) {
            double[] xyz = whitePoint ?? Md3Color.WhitePointD65;
            double adapting = adaptingLuminance ?? (200.0 / Math.PI) * Md3Color.YFromLstar(50.0) / 100.0;
            double rW = xyz[0] * 0.401288 + xyz[1] * 0.650173 + xyz[2] * -0.051461;
            double gW = xyz[0] * -0.250268 + xyz[1] * 1.204414 + xyz[2] * 0.045854;
            double bW = xyz[0] * -0.002079 + xyz[1] * 0.048952 + xyz[2] * 0.953127;
            double f = 0.8 + surround / 10.0;
            double c = f >= 0.9
                ? Md3Math.Lerp(0.59, 0.69, (f - 0.9) * 10.0)
                : Md3Math.Lerp(0.525, 0.59, (f - 0.8) * 10.0);
            double d = discountingIlluminant
                ? 1.0
                : f * (1.0 - (1.0 / 3.6) * Math.Exp((-adapting - 42.0) / 92.0));
            d = d > 1.0 ? 1.0 : d < 0.0 ? 0.0 : d;
            double nc = f;
            double[] rgbD = new double[] {
                d * (100.0 / rW) + 1.0 - d,
                d * (100.0 / gW) + 1.0 - d,
                d * (100.0 / bW) + 1.0 - d,
            };
            double k = 1.0 / (5.0 * adapting + 1.0);
            double k4 = k * k * k * k;
            double k4F = 1.0 - k4;
            double fl = k4 * adapting + 0.1 * k4F * k4F * Math.Cbrt(5.0 * adapting);
            double n = Md3Color.YFromLstar(backgroundLstar) / xyz[1];
            double z = 1.48 + Math.Sqrt(n);
            double nbb = 0.725 / Math.Pow(n, 0.2);
            double ncb = nbb;
            double[] rgbAFactors = new double[] {
                Math.Pow((fl * rgbD[0] * rW) / 100.0, 0.42),
                Math.Pow((fl * rgbD[1] * gW) / 100.0, 0.42),
                Math.Pow((fl * rgbD[2] * bW) / 100.0, 0.42),
            };
            double[] rgbA = new double[] {
                (400.0 * rgbAFactors[0]) / (rgbAFactors[0] + 27.13),
                (400.0 * rgbAFactors[1]) / (rgbAFactors[1] + 27.13),
                (400.0 * rgbAFactors[2]) / (rgbAFactors[2] + 27.13),
            };
            double aw = (2.0 * rgbA[0] + rgbA[1] + 0.05 * rgbA[2]) * nbb;
            return new Md3ViewingConditions(n, aw, nbb, ncb, c, nc, rgbD, fl, Math.Pow(fl, 0.25), z);
        }

        public static readonly Md3ViewingConditions Default = Make();
    }

    /// <summary>CAM16 色彩外观模型（移植自 hct/cam16.js，仅保留 HCT 所需部分）。</summary>
    internal sealed class Md3Cam16 {
        public double Hue { get; }
        public double Chroma { get; }
        public double J { get; }

        private Md3Cam16(double hue, double chroma, double j) {
            Hue = hue;
            Chroma = chroma;
            J = j;
        }

        public static Md3Cam16 FromInt(uint argb) => FromIntInViewingConditions(argb, Md3ViewingConditions.Default);

        public static Md3Cam16 FromIntInViewingConditions(uint argb, Md3ViewingConditions viewingConditions) {
            int red = Md3Color.RedFromArgb(argb);
            int green = Md3Color.GreenFromArgb(argb);
            int blue = Md3Color.BlueFromArgb(argb);
            double redL = Md3Color.Linearized(red);
            double greenL = Md3Color.Linearized(green);
            double blueL = Md3Color.Linearized(blue);
            double x = 0.41233895 * redL + 0.35762064 * greenL + 0.18051042 * blueL;
            double y = 0.2126 * redL + 0.7152 * greenL + 0.0722 * blueL;
            double z = 0.01932141 * redL + 0.11916382 * greenL + 0.95034478 * blueL;
            return FromXyzInViewingConditions(x, y, z, viewingConditions);
        }

        public static Md3Cam16 FromXyzInViewingConditions(double x, double y, double z, Md3ViewingConditions vc) {
            double rC = 0.401288 * x + 0.650173 * y - 0.051461 * z;
            double gC = -0.250268 * x + 1.204414 * y + 0.045854 * z;
            double bC = -0.002079 * x + 0.048952 * y + 0.953127 * z;
            double rD = vc.RgbD[0] * rC;
            double gD = vc.RgbD[1] * gC;
            double bD = vc.RgbD[2] * bC;
            double rAF = Math.Pow(vc.Fl * Math.Abs(rD) / 100.0, 0.42);
            double gAF = Math.Pow(vc.Fl * Math.Abs(gD) / 100.0, 0.42);
            double bAF = Math.Pow(vc.Fl * Math.Abs(bD) / 100.0, 0.42);
            double rA = Md3Math.Signum(rD) * 400.0 * rAF / (rAF + 27.13);
            double gA = Md3Math.Signum(gD) * 400.0 * gAF / (gAF + 27.13);
            double bA = Md3Math.Signum(bD) * 400.0 * bAF / (bAF + 27.13);
            double a = (11.0 * rA + -12.0 * gA + bA) / 11.0;
            double b = (rA + gA - 2.0 * bA) / 9.0;
            double u = (20.0 * rA + 20.0 * gA + 21.0 * bA) / 20.0;
            double p2 = (40.0 * rA + 20.0 * gA + bA) / 20.0;
            double atan2 = Math.Atan2(b, a);
            double atanDegrees = atan2 * 180.0 / Math.PI;
            double hue = atanDegrees < 0 ? atanDegrees + 360.0
                : atanDegrees >= 360 ? atanDegrees - 360.0
                : atanDegrees;
            double huePrime = hue < 20.14 ? hue + 360 : hue;
            double eHue = 0.25 * (Math.Cos(huePrime * Math.PI / 180.0 + 2.0) + 3.8);
            double p1 = 50000.0 / 13.0 * eHue * vc.Nc * vc.Ncb;
            double t = p1 * Math.Sqrt(a * a + b * b) / (u + 0.305);
            double alpha = Math.Pow(t, 0.9) * Math.Pow(1.64 - Math.Pow(0.29, vc.N), 0.73);
            double ac = p2 * vc.Nbb;
            double j = 100.0 * Math.Pow(ac / vc.Aw, vc.C * vc.Z);
            double c = alpha * Math.Sqrt(j / 100.0);
            return new Md3Cam16(hue, c, j);
        }
    }
}

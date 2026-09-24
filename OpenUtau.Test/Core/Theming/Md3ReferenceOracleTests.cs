using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenUtau.Core.Theming;
using Xunit;

namespace OpenUtau.Test.Core.Theming {
    /// <summary>
    /// 颜色池移植保真度测试。
    ///
    /// 对照数据由 Google 参考实现 material-color-utilities@0.3.0 直接生成
    /// （生成器：tools/md3-codegen/gen-oracle.js），覆盖 9 个种子 × 7 个配色方案 × 深浅 × 对比度档位，
    /// 共 132 个用例 × 49 个角色。任何一位偏差都会让 SHA-256 不符。
    /// </summary>
    public class Md3ReferenceOracleTests {
        private static readonly string DataDir =
            Path.Combine(AppContext.BaseDirectory, "Core", "Theming", "Data");

        private static Md3Role ParseRole(string camel) =>
            Enum.Parse<Md3Role>(char.ToUpperInvariant(camel[0]) + camel.Substring(1));

        private static uint ParseHexSeed(string hex) =>
            Convert.ToUInt32(hex.TrimStart('#'), 16) | 0xFF000000u;

        [Fact]
        public void AllRoleValues_MatchGoogleReference() {
            using JsonDocument meta = JsonDocument.Parse(File.ReadAllText(Path.Combine(DataDir, "md3-oracle-meta.json")));
            JsonElement root = meta.RootElement;
            Md3Role[] roleOrder = root.GetProperty("roleOrder").EnumerateArray()
                .Select(e => ParseRole(e.GetString()!)).ToArray();
            string[] cases = root.GetProperty("cases").EnumerateArray()
                .Select(e => e.GetString()!).ToArray();
            string expectedHash = root.GetProperty("sha256").GetString()!;

            var lines = new List<string>(cases.Length);
            foreach (string c in cases) {
                string[] parts = c.Split('|');
                uint seed = ParseHexSeed(parts[0]);
                Md3SchemeVariant variant = Enum.Parse<Md3SchemeVariant>(parts[1]);
                bool dark = parts[2] == "1";
                double contrast = double.Parse(parts[3], CultureInfo.InvariantCulture);
                Md3SchemeColors colors = Md3SchemeColors.Create(seed, variant, dark, contrast);

                var sb = new StringBuilder();
                sb.Append(Md3Color.HexFromArgb(seed)).Append('|')
                  .Append(parts[1]).Append('|')
                  .Append(parts[2]).Append('|')
                  .Append(parts[3]).Append('|');
                for (int i = 0; i < roleOrder.Length; i++) {
                    if (i > 0) {
                        sb.Append(',');
                    }
                    sb.Append(colors.Hex(roleOrder[i]));
                }
                lines.Add(sb.ToString());
            }

            string canonical = string.Join("\n", lines);
            string actualHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
            Assert.Equal(expectedHash, actualHash);
            Assert.Equal(132, cases.Length);
            Assert.Equal(49, roleOrder.Length);
        }

        [Theory]
        [InlineData("TonalSpot-light-6750a4", "TonalSpot", false, "#6750a4")]
        [InlineData("TonalSpot-dark-6750a4", "TonalSpot", true, "#6750a4")]
        [InlineData("Monochrome-light-6750a4", "Monochrome", false, "#6750a4")]
        public void GoldenValues_MatchGoogleReference(string goldenKey, string variantName, bool dark, string seedHex) {
            using JsonDocument meta = JsonDocument.Parse(File.ReadAllText(Path.Combine(DataDir, "md3-oracle-meta.json")));
            JsonElement golden = meta.RootElement.GetProperty("goldens").GetProperty(goldenKey);
            Md3SchemeColors colors = Md3SchemeColors.Create(
                ParseHexSeed(seedHex), Enum.Parse<Md3SchemeVariant>(variantName), dark);

            foreach (JsonProperty p in golden.EnumerateObject()) {
                Assert.Equal(p.Value.GetString(), colors.Hex(ParseRole(p.Name)));
            }
        }

        [Fact]
        public void HctSolver_MatchesGoogleReference_ForAllToneCases() {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(DataDir, "md3-hct-cases.json")));
            int count = 0;
            foreach (JsonElement c in doc.RootElement.EnumerateArray()) {
                double hue = c.GetProperty("hue").GetDouble();
                double chroma = c.GetProperty("chroma").GetDouble();
                double tone = c.GetProperty("tone").GetDouble();
                string expected = c.GetProperty("hex").GetString()!;
                Assert.Equal(expected, Md3Color.HexFromArgb(Md3Hct.From(hue, chroma, tone).ToInt()));
                count++;
            }
            Assert.True(count >= 600, $"HCT 用例数偏少：{count}");
        }

        [Fact]
        public void AllRoles_AreCoveredByTable() {
            Assert.Equal(49, Md3Roles.Count);
            Assert.Equal(49, Enum.GetValues<Md3Role>().Length);
            Assert.Equal(Md3SchemeColors.RoleCount, Enum.GetValues<Md3Role>().Length);
        }

        [Fact]
        public void CoreTextRoles_MeetMinimumContrast_ForDefaultSeed() {
            // 语义护栏：正文前景 onSurface/onSurfaceVariant 与对应背景至少 4.5:1（MD3 规范）
            foreach (bool dark in new[] { false, true }) {
                Md3SchemeColors c = Md3SchemeColors.Create(0xFF6750A4, Md3SchemeVariant.TonalSpot, dark);
                double surface = Md3Color.LstarFromArgb(c.Get(Md3Role.Surface));
                double onSurface = Md3Color.LstarFromArgb(c.Get(Md3Role.OnSurface));
                double surfaceVariant = Md3Color.LstarFromArgb(c.Get(Md3Role.SurfaceVariant));
                double onSurfaceVariant = Md3Color.LstarFromArgb(c.Get(Md3Role.OnSurfaceVariant));
                Assert.True(Md3Contrast.RatioOfTones(surface, onSurface) >= 4.5,
                    $"onSurface/surface 对比不足（dark={dark}）");
                Assert.True(Md3Contrast.RatioOfTones(surfaceVariant, onSurfaceVariant) >= 4.5,
                    $"onSurfaceVariant/surfaceVariant 对比不足（dark={dark}）");
            }
        }
    }
}

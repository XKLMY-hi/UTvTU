using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// W40 防漂移契约：`OpenUtau/Assets/Icons.axaml` 里的品牌几何必须与**用户设计稿的纯提取件**
    /// `.opencode/design/brand/extracted/**` 一致（归一空白后逐字比较）。
    ///
    /// 为什么基准是 extracted/ 而不是 out/：用户明确要求"设计稿只做提取、不要在其上二次创作"，
    /// `out/` 已被标为 **derived（派生：光学补偿/字距重排/描摹）不作产品用**；extracted/ 是逐字提取的原生 path。
    /// 提取件一变 ⇒ 本用例即红 ⇒ 强迫消费方重取。
    ///
    /// 前 3 条可直接比 `d`：`brand-mark` ← mark.svg、`brand-brace` ← brace-open.svg、
    /// `brand-v-chevron` ← v-chevron.svg。
    /// 右括号**没有独立几何**：设计稿 `brace-close.svg` = 同一条 brace 路径 + `scaleX(-1)`，
    /// 故产品侧同样只用 brand-brace + ScaleX(-1)（不留预烘焙镜像键）。
    ///
    /// 第 4 条 `brand-wordmark` ← wordmark-ut.svg 是**多 path + 多层 transform 的合成件**，
    /// 且其 `d` 用 PDF 内容流语法（操作数在前），无法逐字对拷 ⇒ 用两条更强的断言：
    ///   ① 源指纹：viewBox + 4 条 `d`（归一空白）的 SHA256 == 记录值（源一变即红）；
    ///   ② 产品键结构：坐标范围 == 源 viewBox 范围、子路径（M）数 == 8（U 单环 ×2 + T 三矩形 ×2）。
    /// 展平规则（U 用自身 matrix、T 用顶层 g）与指纹来由见 `Icons.axaml` 注释与 W40 提交说明。
    ///
    /// 注：断言读的是**产物副本**（这些文件都以 None/CopyToOutputDirectory 链到测试输出目录），
    /// 所以改了源文件必须先重建再跑测试（UI 标准里的纪律）。
    /// </summary>
    public class BrandGeometryContractTests {
        // 产品键 → 提取件文件（逐字来源）
        private static readonly (string Key, string Source)[] Contracts = {
            ("brand-mark", "mark.svg"),
            ("brand-brace", "brace-open.svg"),
            ("brand-v-chevron", "v-chevron.svg"),
        };

        /// <summary>wordmark-ut.svg 的源指纹（viewBox 属性原文 + 4 条 d 归一空白后 SHA256）。</summary>
        private const string WordmarkFingerprint =
            "fd5a6a796747baee05b655321ce25738ea895e9e198b08b00a75913a38fd1e6d";

        private const double WordmarkWidth = 1032.413736;
        private const double WordmarkHeight = 277.470703;
        private const int WordmarkRings = 8;

        private static string Normalize(string s) => Regex.Replace(s, @"\s+", "").Trim();

        private static string ExtractedDir => Path.Combine(AppContext.BaseDirectory, "brand", "extracted");

        private static string ProductFile => Path.Combine(AppContext.BaseDirectory, "Assets", "Icons.axaml");

        /// <summary>提取件里那条 path 的 d 属性（用户设计稿原生几何）。</summary>
        private static string SourcePathData(string fileName) {
            string file = Path.Combine(ExtractedDir, fileName);
            Assert.True(File.Exists(file), $"缺少提取件产物副本 {file}");
            var m = Regex.Match(File.ReadAllText(file), "d=\"([^\"]+)\"");
            Assert.True(m.Success, $"{fileName} 里找不到 path 的 d 属性");
            return Normalize(m.Groups[1].Value);
        }

        private static Dictionary<string, string> ProductKeys() {
            Assert.True(File.Exists(ProductFile), $"缺少产物副本 {ProductFile}");
            return Regex.Matches(File.ReadAllText(ProductFile), "x:Key=\"(brand-[^\"]+)\"[^>]*>([^<]*)<")
                .ToDictionary(m => m.Groups[1].Value, m => Normalize(m.Groups[2].Value));
        }

        [Fact]
        public void ProductGeometry_IsVerbatimCopyOfExtractedDesign() {
            var product = ProductKeys();
            string[] problems = Contracts
                .Where(c => !product.TryGetValue(c.Key, out string v) || v != SourcePathData(c.Source))
                .Select(c => $"{c.Key} ← {c.Source}")
                .ToArray();
            Assert.True(problems.Length == 0,
                "品牌几何与用户设计稿提取件不一致（提取件重出了吗？须重取）：" + string.Join(", ", problems));
        }

        [Fact]
        public void ProductIconDictionary_HasNoDerivedOrExtraBrandKeys() {
            var product = ProductKeys();
            var expected = Contracts.Select(c => c.Key).Append("brand-wordmark").ToHashSet();
            string[] extra = product.Keys.Where(k => !expected.Contains(k)).ToArray();
            Assert.True(extra.Length == 0,
                "Icons.axaml 里有非提取件品牌键（派生/占位/临时键应清理）：" + string.Join(", ", extra));
        }

        /// <summary>
        /// 第 4 条：字标。源码是多 path + 多层 transform 的合成件（且 d 用 PDF 语法），无法逐字对拷 ⇒
        /// ① 源指纹必须与记录值一致（源一变即红）；② 产品键的坐标范围/子路径数必须与源的 viewBox 一致。
        /// </summary>
        [Fact]
        public void ProductWordmark_MatchesExtractedSource() {
            string file = Path.Combine(ExtractedDir, "wordmark-ut.svg");
            Assert.True(File.Exists(file), $"缺少提取件产物副本 {file}");
            string svg = File.ReadAllText(file);

            string viewBox = Regex.Match(svg, "viewBox=\"([^\"]+)\"").Groups[1].Value;
            string[] paths = Regex.Matches(svg, "<path[^>]*?d=\"([^\"]+)\"")
                .Select(m => Regex.Replace(m.Groups[1].Value, @"\s+", " ").Trim())
                .ToArray();
            Assert.Equal(4, paths.Length);   // U / T / T / U 四条 path（环数见下：U 各 1 环、T 各 3 环 = 8）
            string fingerprint;
            using (var sha = System.Security.Cryptography.SHA256.Create()) {
                string payload = string.Join("\n", new[] { viewBox }.Concat(paths));
                fingerprint = string.Concat(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload))
                    .Select(b => b.ToString("x2")));
            }
            Assert.True(fingerprint == WordmarkFingerprint,
                "wordmark-ut.svg 变了（源指纹不符）⇒ 必须重新展平 brand-wordmark 并更新记录指纹");

            var product = ProductKeys();
            Assert.True(product.ContainsKey("brand-wordmark"), "Icons.axaml 缺少 brand-wordmark");
            // 注意：数值解析必须用**原文**（ProductKeys 会去掉所有空白，会把相邻数字粘成一个，
            // 例如 "C209.8563 201.9015" → "C209.8563201.9015"，这是真实踩到的坑）
            string wm = Regex.Match(File.ReadAllText(ProductFile),
                "x:Key=\"brand-wordmark\"[^>]*>([^<]*)<").Groups[1].Value;
            var nums = Regex.Matches(wm, "-?\\d+(?:\\.\\d+)?")
                .Select(m => double.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();
            // 坐标是成对的 x,y（M/L/C 全部坐标对）——取偶数/奇数位分别求范围
            double[] xs = nums.Where((_, i) => i % 2 == 0).ToArray();
            double[] ys = nums.Where((_, i) => i % 2 == 1).ToArray();
            Assert.Equal(0, xs.Min(), 1);
            Assert.Equal(0, ys.Min(), 1);
            Assert.Equal(WordmarkWidth, xs.Max(), 1);
            Assert.Equal(WordmarkHeight, ys.Max(), 1);
            Assert.Equal(WordmarkRings, Regex.Matches(wm, "M").Count);
        }

        /// <summary>唯一性：同字典重复键会互相覆盖（W40 真实踩到过），这里钉死。</summary>
        [Fact]
        public void ProductIconDictionary_HasNoDuplicateBrandKeys() {
            string text = File.ReadAllText(ProductFile);
            var keys = Regex.Matches(text, "x:Key=\"(brand-[^\"]+)\"")
                .Select(m => m.Groups[1].Value)
                .ToArray();
            string[] dupes = keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
            Assert.True(dupes.Length == 0, "Icons.axaml 里品牌几何键重复：" + string.Join(", ", dupes));
        }
    }
}

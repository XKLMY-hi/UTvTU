using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// W40 防漂移契约：`OpenUtau/Assets/Icons.axaml` 里的品牌几何必须与**用户设计稿的纯提取件**
    /// `.opencode/design/brand/extracted/**` 的 path 字符串**逐字相同**（归一空白后比较）。
    ///
    /// 为什么基准是 extracted/ 而不是 out/：用户明确要求"设计稿只做提取、不要在其上二次创作"，
    /// `out/` 已被标为 **derived（派生：光学补偿/字距重排/描摹）不作产品用**；extracted/ 是逐字提取的原生 path。
    /// 提取件一变 ⇒ 本用例即红 ⇒ 强迫消费方重取。
    ///
    /// 目前纳入比对的 3 条（字标待 PDF 轮廓提取件 `wordmark-outline.svg` 到位后一并纳入）：
    ///   brand-mark ← extracted/mark.svg · brand-brace ← extracted/brace-open.svg
    ///   brand-v-chevron ← extracted/v-chevron.svg
    /// 右括号**没有独立几何**：设计稿 `extracted/brace-close.svg` = 同一条 brace 路径 + `scaleX(-1)`，
    /// 故产品侧同样只用 brand-brace + ScaleX(-1)（不留预烘焙镜像键）。
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
            var expected = Contracts.Select(c => c.Key).ToHashSet();
            string[] extra = product.Keys.Where(k => !expected.Contains(k)).ToArray();
            // 只允许"待纳入"的字标键缺位，不允许出现派生/占位/临时键
            Assert.True(extra.Length == 0,
                "Icons.axaml 里有非提取件品牌键（派生/占位/临时键应清理）：" + string.Join(", ", extra));
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

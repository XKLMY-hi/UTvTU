using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// W40 防漂移契约：`OpenUtau/Assets/Icons.axaml` 里的品牌几何必须与
    /// **冻结文件** `.opencode/design/brand/out/utvtu-brand-lockup.axaml` 的 path 字符串**逐字相同**
    /// （归一空白后比较）。冻结文件一变、本用例即红 ⇒ 强迫消费方按 brand-frozen-manifest.md 重取，
    /// 这是"多处引用同一份共享几何"能成立的前提。
    ///
    /// 期望键集合**由冻结文件推导**（取其非空几何键），并额外断言产品侧**没有多余品牌键**
    /// ⇒ 临时键、占位键、已删键都会被抓到；冻结文件里那条故意为空的 `brand-wordmark-monoline`
    /// 我们不镜像（W39f 标注"不进接入清单"）。
    ///
    /// 注：断言读的是**产物副本**（两文件都以 None/CopyToOutputDirectory 链到测试输出目录），
    /// 所以改了源文件必须先重建再跑测试（UI 标准里的纪律）。
    /// </summary>
    public class BrandGeometryContractTests {
        private static string Normalize(string s) => Regex.Replace(s, @"\s+", "").Trim();

        private static Dictionary<string, string> ReadKeys(string file) {
            string text = File.ReadAllText(file);
            var map = new Dictionary<string, string>();
            foreach (Match m in Regex.Matches(text, "x:Key=\"(brand-[^\"]+)\"[^>]*>([^<]*)<")) {
                map[m.Groups[1].Value] = Normalize(m.Groups[2].Value);
            }
            return map;
        }

        private static string ProductFile => Path.Combine(AppContext.BaseDirectory, "Assets", "Icons.axaml");

        private static string FrozenFile => Path.Combine(
            AppContext.BaseDirectory, "brand", "out", "utvtu-brand-lockup.axaml");

        /// <summary>冻结几何键 = 冻结文件里几何非空的品牌键（空壳键不镜像）。</summary>
        private static Dictionary<string, string> FrozenGeometries() {
            Assert.True(File.Exists(FrozenFile), $"缺少产物副本 {FrozenFile}");
            return ReadKeys(FrozenFile)
                .Where(kv => kv.Value.Length > 0)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        [Fact]
        public void ProductIconDictionary_MatchesFrozenKeySet() {
            Assert.True(File.Exists(ProductFile), $"缺少产物副本 {ProductFile}");
            var frozen = FrozenGeometries();
            var product = ReadKeys(ProductFile);
            string[] missing = frozen.Keys.Where(k => !product.ContainsKey(k)).ToArray();
            string[] extra = product.Keys.Where(k => !frozen.ContainsKey(k)).ToArray();
            Assert.True(missing.Length == 0, "Icons.axaml 缺少冻结几何键：" + string.Join(", ", missing));
            Assert.True(extra.Length == 0, "Icons.axaml 有多余品牌键（临时/占位/已删键应清理）：" + string.Join(", ", extra));
        }

        [Fact]
        public void ProductGeometry_IsVerbatimCopyOfFrozenFile() {
            var frozen = FrozenGeometries();
            var product = ReadKeys(ProductFile);
            string[] drifted = frozen
                .Where(kv => !product.TryGetValue(kv.Key, out string v) || v != kv.Value)
                .Select(kv => kv.Key)
                .ToArray();
            Assert.True(drifted.Length == 0,
                "品牌几何与冻结文件不一致（冻结文件变了吗？须按 brand-frozen-manifest.md 重取）："
                + string.Join(", ", drifted));
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

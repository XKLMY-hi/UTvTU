using System;
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
    /// 注：断言读的是**产物副本**（两文件都以 None/CopyToOutputDirectory 链到测试输出目录），
    /// 所以改了源文件必须先重建再跑测试（UI 标准里的纪律）。
    /// </summary>
    public class BrandGeometryContractTests {
        // 冻结清单（W39f）的 7 键；其中 brand-wordmark-monoline 在冻结文件里**故意为空**
        //（W39f 标注"不进接入清单"），故这里只校验"键存在"，内容逐字比对交给第二条用例。
        private static readonly string[] FrozenKeys = {
            "brand-mark",
            "brand-brace",
            "brand-brace-flipx",
            "brand-v-chevron",
            "brand-v-chevron-stroke",
            "brand-wordmark",
            "brand-wordmark-monoline",
        };

        private static string Normalize(string s) => Regex.Replace(s, @"\s+", "").Trim();

        private static bool TryKey(string file, string key, out string value) {
            string text = File.ReadAllText(file);
            var m = Regex.Match(text, "x:Key=\"" + Regex.Escape(key) + "\"[^>]*>([^<]*)<");
            value = m.Success ? Normalize(m.Groups[1].Value) : string.Empty;
            return m.Success;
        }

        private static string ProductFile => Path.Combine(AppContext.BaseDirectory, "Assets", "Icons.axaml");

        private static string FrozenFile => Path.Combine(
            AppContext.BaseDirectory, "brand", "out", "utvtu-brand-lockup.axaml");

        [Fact]
        public void ProductIconDictionary_ExposesEveryFrozenGeometryKey() {
            Assert.True(File.Exists(ProductFile), $"缺少产物副本 {ProductFile}");
            string[] missing = FrozenKeys.Where(k => !TryKey(ProductFile, k, out _)).ToArray();
            Assert.True(missing.Length == 0, "Icons.axaml 缺少冻结几何键：" + string.Join(", ", missing));
        }

        [Fact]
        public void ProductGeometry_IsVerbatimCopyOfFrozenFile() {
            Assert.True(File.Exists(FrozenFile), $"缺少产物副本 {FrozenFile}");
            string[] drifted = FrozenKeys
                .Where(k => {
                    TryKey(FrozenFile, k, out string frozen);
                    TryKey(ProductFile, k, out string product);
                    return product != frozen;
                })
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

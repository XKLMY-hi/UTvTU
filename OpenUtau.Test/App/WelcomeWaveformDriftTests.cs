using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// 欢迎页波形几何的**单一来源契约**（W38）。
    ///
    /// 权威来源 = 生成器产物 `.opencode/design/welcome-waveform/waveform-b.xaml` 的 **`loudness:` 行**
    /// （由 `OpenUtau.Test/Tools/WelcomeWaveformGenerator.cs` 产出，`OPENUTAU_GEN_WELCOME_WAVE=1` 触发，
    /// 落盘目录可用 `OPENUTAU_GEN_WELCOME_OUT` 指定）；应用侧 `OpenUtau/Assets/WelcomeWaveform.axaml`
    /// 的 `welcome-waveform` 必须是它的**逐字副本**（去空白后相等）。
    ///
    /// 这条断言拦住三件事：
    ///   1. **手改资源字典**（绕过生成器微调几何）；
    ///   2. **忘记重生成**（生成器算法改了、产物没重跑，应用仍用旧几何）；
    ///   3. **算法漂移**（重生成后几何退回旧形态/形状变了，与线上不一致）。
    ///
    /// ⚠ 比较前先做**去空白归一**：两侧的数字格式可能不同（例如 `0.00` 与 `0`），
    ///    格式差异不算漂移；**数字本身**不同才算（见 `LoudnessNumbers_AreIdenticalInCount` 的姊妹思路：
    ///    本文件用"去空白逐字相等"这一更强口径，格式差异已在 W38 对齐时消除）。
    /// </summary>
    public class WelcomeWaveformDriftTests {
        private static string AssetFile => Path.Combine(AppContext.BaseDirectory, "Assets", "WelcomeWaveform.axaml");
        private static string ProductFile => Path.Combine(AppContext.BaseDirectory, "waveform", "waveform-b.xaml");

        private static string Norm(string s) => Regex.Replace(s, @"\s+", string.Empty);

        /// <summary>从资源字典取 `welcome-waveform` 的几何；取不到返回 null（格式变了要显式报错，别静默通过）。</summary>
        private static string? AssetGeometry(string assetText) {
            Match m = Regex.Match(assetText, "<StreamGeometry x:Key=\"welcome-waveform\">(?<d>[^<]+)</StreamGeometry>");
            return m.Success ? m.Groups["d"].Value : null;
        }

        /// <summary>从生成器产物取 `loudness:` 行；取不到返回 null。</summary>
        private static string? GeneratorLoudness(string productText) {
            Match m = Regex.Match(productText, @"^[ \t]*loudness:[ \t]*(?<d>.+)$", RegexOptions.Multiline);
            return m.Success ? m.Groups["d"].Value : null;
        }

        /// <summary>核心判据（抽成纯函数 ⇒ 可对"被篡改的输入"证明它真的会红，而不是空转）。</summary>
        internal static bool IsSameGeometry(string assetText, string productText, out string reason) {
            string? asset = AssetGeometry(assetText);
            string? gen = GeneratorLoudness(productText);
            if (asset == null) {
                reason = "资源字典里找不到 welcome-waveform 的 StreamGeometry（键名/格式变了？）";
                return false;
            }
            if (gen == null) {
                reason = "生成器产物里找不到 loudness 行（生成器没跑？输出格式改了？）";
                return false;
            }
            // 非空转护栏：几何必须足够长，否则正则误命中会让断言变成"永远通过"
            if (asset.Length < 1000 || gen.Length < 1000) {
                reason = $"几何过短（资源字典 {asset.Length} / 生成器 {gen.Length}）—— 断言会空转";
                return false;
            }
            if (!string.Equals(Norm(asset), Norm(gen), StringComparison.Ordinal)) {
                reason = $"几何不一致（资源字典 {Norm(asset).Length} 字符 / 生成器 {Norm(gen).Length} 字符）";
                return false;
            }
            reason = "一致";
            return true;
        }

        [Fact]
        public void WelcomeWaveform_IsVerbatimCopyOfGeneratorLoudness() {
            Assert.True(File.Exists(AssetFile), $"资源字典缺失：{AssetFile}");
            Assert.True(File.Exists(ProductFile), $"生成器产物缺失：{ProductFile}");
            bool same = IsSameGeometry(File.ReadAllText(AssetFile), File.ReadAllText(ProductFile), out string reason);
            Assert.True(same, $"{reason}\n⇒ 请重生成：设 OPENUTAU_GEN_WELCOME_WAVE=1 后跑 WelcomeWaveformGenerator 用例，"
                + "再把产物 loudness 行同步进 OpenUtau/Assets/WelcomeWaveform.axaml（勿手改几何）。");
        }

        /// <summary>证明这条护栏**不是空转**：把几何改一个数字就必然红。</summary>
        [Fact]
        public void DriftGuard_RejectsTamperedAsset() {
            string asset = File.ReadAllText(AssetFile);
            string product = File.ReadAllText(ProductFile);
            Assert.True(IsSameGeometry(asset, product, out _), "基线应当一致（不一致请看上一条用例）");

            // 篡改 1：改一个坐标数字
            string tampered = Regex.Replace(asset, @"\d+\.\d\d", "999.99", RegexOptions.None, TimeSpan.FromSeconds(2));
            Assert.False(IsSameGeometry(tampered, product, out _), "改了坐标数字却仍然通过 ⇒ 断言空转");

            // 篡改 2：删掉 loudness 行 ⇒ 生成器产物不可解析
            Assert.False(IsSameGeometry(asset, "waveform: M 0,0 L 1,1", out _), "生成器产物缺 loudness 行却通过");
        }
    }
}

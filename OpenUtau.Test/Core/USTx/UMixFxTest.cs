using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Core.Ustx {
    public class UMixFxTest {
        [Fact]
        public void Default_IsNotEnabled() {
            var fx = new UMixFx();
            Assert.False(fx.Enabled);
        }

        [Fact]
        public void YamlRoundTrip_PreservesEqParams() {
            var fx = new UMixFx {
                Enabled = true,
                EqLowDb = 3.5, EqMidFreq = 1000.0, EqMidDb = -2.0, EqHighDb = 1.0,
                EqBypassed = false,
            };
            var yaml = Yaml.DefaultSerializer.Serialize(fx);
            var restored = Yaml.DefaultDeserializer.Deserialize<UMixFx>(yaml);
            Assert.NotNull(restored);
            Assert.True(restored.Enabled);
            Assert.Equal(3.5, restored.EqLowDb);
            Assert.Equal(1000.0, restored.EqMidFreq);
            Assert.Equal(-2.0, restored.EqMidDb);
            Assert.Equal(1.0, restored.EqHighDb);
            Assert.False(restored.EqBypassed);
        }

        [Fact]
        public void YamlRoundTrip_PreservesCompAndRevParams() {
            var fx = new UMixFx {
                Enabled = true,
                CompThresholdDb = -18.0, CompRatio = 4.0,
                CompPreset = "Soft", ReverbPreset = "Hall",
                ReverbWet = 0.5, ReverbSize = 0.8,
            };
            var yaml = Yaml.DefaultSerializer.Serialize(fx);
            var restored = Yaml.DefaultDeserializer.Deserialize<UMixFx>(yaml);
            Assert.Equal(-18.0, restored.CompThresholdDb);
            Assert.Equal(4.0, restored.CompRatio);
            Assert.Equal("Soft", restored.CompPreset);
            Assert.Equal("Hall", restored.ReverbPreset);
            Assert.Equal(0.5, restored.ReverbWet);
            Assert.Equal(0.8, restored.ReverbSize);
        }

        [Fact]
        public void Disabled_NullEquivalent() {
            var disabled = new UMixFx { Enabled = false };
            var yaml = Yaml.DefaultSerializer.Serialize(disabled);
            var restored = Yaml.DefaultDeserializer.Deserialize<UMixFx>(yaml);
            Assert.False(restored.Enabled);
        }

        /// <summary>
        /// 旧 Plus 工程迁移：旧 ustx 只写 eq_bypassed/... 这一类反向键，
        /// 加载后必须落到新语义 EqEnabled/CompEnabled/ReverbEnabled。
        /// </summary>
        [Fact]
        public void LegacyBypassedKeys_MigrateToEnabledSemantics() {
            const string legacy = "enabled: true\neq_bypassed: true\ncomp_bypassed: false\nreverb_bypassed: true\n";
            var fx = Yaml.DefaultDeserializer.Deserialize<UMixFx>(legacy);
            Assert.NotNull(fx);
            Assert.False(fx.EqEnabled);      // eq_bypassed: true  → 模块关
            Assert.True(fx.CompEnabled);     // comp_bypassed: false → 模块开
            Assert.False(fx.ReverbEnabled);  // reverb_bypassed: true → 模块关
            // 反向别名恒互反
            Assert.True(fx.EqBypassed);
            Assert.False(fx.CompBypassed);
            Assert.True(fx.ReverbBypassed);
        }

        /// <summary>新键与旧键同时写出且互反 → 反复读写幂等（加载顺序不影响结果）。</summary>
        [Fact]
        public void CanonicalAndLegacyKeys_RoundTripIsIdempotent() {
            var fx = new UMixFx { Enabled = true, EqEnabled = false, CompEnabled = true, ReverbEnabled = false };
            var yaml = Yaml.DefaultSerializer.Serialize(fx);
            Assert.Contains("eq_enabled: false", yaml);
            Assert.Contains("eq_bypassed: true", yaml);
            Assert.Contains("reverb_enabled: false", yaml);
            Assert.Contains("reverb_bypassed: true", yaml);

            var once = Yaml.DefaultDeserializer.Deserialize<UMixFx>(yaml);
            Assert.False(once.EqEnabled);
            Assert.True(once.CompEnabled);
            Assert.False(once.ReverbEnabled);
            Assert.Equal(yaml, Yaml.DefaultSerializer.Serialize(once));

            // Clone 走规范键，迁移结果不丢
            var clone = once.Clone();
            Assert.False(clone.EqEnabled);
            Assert.True(clone.CompEnabled);
            Assert.False(clone.ReverbEnabled);
        }
    }
}

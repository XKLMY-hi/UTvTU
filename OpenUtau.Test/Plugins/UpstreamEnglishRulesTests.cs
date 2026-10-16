using System;
using System.Collections.Generic;
using System.Reflection;
using OpenUtau.Core.Ustx;
using OpenUtau.Plugin.Builtin;
using Xunit;

namespace OpenUtau.Test.Plugins;

public class UpstreamEnglishRulesTests {
    sealed class AliasSinger : USinger {
        readonly HashSet<string> aliases;
        public AliasSinger(params string[] aliases) { this.aliases = new(aliases); found = loaded = true; }
        public override bool TryGetOto(string phoneme, out UOto oto) {
            oto = aliases.Contains(phoneme) ? UOto.OfDummy(phoneme) : null;
            return oto != null;
        }
    }
    sealed class XSampa : EnXSampaPhonemizer {
        public IReadOnlyList<string> VV(string previous, string vowel, bool direct) {
            singer = new AliasSinger("3", "I", "U", "V", "bV", "- VI", "- VU", previous + " j", previous + " w", "ja", "wa",
                direct ? previous + " " + vowel : "unused");
            return ProcessSyllable(new Syllable { prevV = previous, v = vowel, cc = Array.Empty<string>(), tone = 60, vowelTone = 60 });
        }
    }
    sealed class EnJa : ENtoJAPhonemizer {
        public EnJa() {
            WanaKanaDictionary["N"] = new() { "ん" }; WanaKanaDictionary["n"] = new() { "ぬ" };
            WanaKanaDictionary["ax"] = new() { "ぁ" }; WanaKanaDictionary["a"] = new() { "あ" };
            yamlFallbacks.Add(new Replacement { from = "ax", to = "a" });
        }
    }
    [Theory]
    [InlineData("aI", "aI j", "ja")]
    [InlineData("aU", "aU w", "wa")]
    public void DiphthongVowelTransitionsUseTheirGlideBeforeTheNextVowel(string previous, string first, string second) {
        Assert.Equal(new[] { first, second }, new XSampa().VV(previous, "a", false));
        Assert.Equal(new[] { previous + " a" }, new XSampa().VV(previous, "a", true));
    }
    [Theory]
    [InlineData("N", "ん")]
    [InlineData("n", "ぬ")]
    [InlineData("ax", "ぁ")]
    [InlineData("Na", "んあ")]
    public void KanaLookupKeepsExactCapitalizationAndExactVowelMappingsBeforeFallbacks(string alias, string expected) {
        var method = typeof(ENtoJAPhonemizer).GetMethod("ToHiragana", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.Equal(expected, method.Invoke(new EnJa(), new object[] { alias, 60 }));
    }
    [Fact]
    public void WanaKanaYamlAcceptsScalarsAndSkipsNullEntries() {
        var mapping = new ENtoJAPhonemizer.WanaKanaData { roma = new object[] { "a", null, "" }, kana = 42 };
        Assert.Equal(new[] { "a" }, mapping.FromList);
        Assert.Equal(new[] { "42" }, mapping.ToList);
    }
}

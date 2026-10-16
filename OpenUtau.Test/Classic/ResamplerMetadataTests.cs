using System;
using System.IO;
using System.Linq;
using OpenUtau.Classic;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Test.TestSupport;
using Serilog;
using Xunit;

namespace OpenUtau.Test.Classic;

[Collection("AudioFixture")]
public sealed class ResamplerMetadataTests {
    sealed class Resampler : IResampler {
        public ResamplerManifest Manifest { get; init; }
        public string FilePath => "test";
        public bool SupportsFlag(string abbr) => false;
        public void CheckPermissions() { }
        public float[] DoResampler(ResamplerItem item, ILogger logger) => throw new NotSupportedException();
        public string DoResamplerReturnsFile(ResamplerItem item, ILogger logger) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CustomMetadataCopiesBothWaysAndBuiltinOrNullManifestsRemainSafe(bool manifest) {
        var directory = Path.Combine(Path.GetTempPath(), "UTvTU-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "source.flac");
        var temp = Path.Combine(directory, "cached.wav");
        File.WriteAllText(source, "source");
        File.WriteAllText(temp, "already decoded audio");
        File.WriteAllText(Path.Combine(directory, "source.analysis"), "analysis");
        var resampler = new Resampler { Manifest = manifest ? new ResamplerManifest { files = new[] { ".analysis", "../outside", null } } : null };
        VoicebankFiles.Inst.CopySourceTemp(source, temp, resampler);
        Assert.Equal(manifest, File.Exists(Path.Combine(directory, "cached.analysis")));
        File.WriteAllText(Path.Combine(directory, "cached.generated"), "generated");
        if (manifest) resampler.Manifest.files = new[] { ".generated" };
        VoicebankFiles.Inst.CopyBackMetaFiles(source, temp, resampler);
        Assert.Equal(manifest, File.Exists(Path.Combine(directory, "source.generated")));
        VoicebankFiles.Inst.CopySourceTemp(source, temp); // Keep the old binary/API entry.
    }

    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(200)]
    public void LengthenedPreutterPullsEarlierAudioWithoutMovingConsonantOrNegativeCutoffEnd(int velocity) {
        ToolsManager.Inst.Initialize();
        var root = Path.Combine(Path.GetTempPath(), "UTvTU-preutter-" + Guid.NewGuid().ToString("N"));
        var singer = DummyVoicebank.Load(DummyVoicebank.Create(root));
        var oto = singer.Otos.First(o => o.Alias == "a");
        oto.Offset = 200; oto.Preutter = 50; oto.Consonant = 80; oto.Cutoff = -400;
        var (project, part) = ClassicRenderSetup.BuildProject(singer, ("a", 60, 480));
        part.notes.First().SetExpression(project, project.tracks[0], "vel", new float?[] { velocity });
        part.notes.First().phonemeOverrides.Add(new UPhonemeOverride { index = 0, preutterDelta = 100 });
        using var phonemizer = ClassicRenderSetup.StartPhonemizer(project);
        ClassicRenderSetup.PhonemizeAndWait(project, part, timeoutMs: 3000);
        var phrase = part.renderPhrases.First();
        var item = new ResamplerItem(phrase, phrase.phones.First());
        Assert.InRange(item.offset, 0, 199.999);
        Assert.Equal(280, item.offset + item.consonant, 3);
        Assert.Equal(600, item.offset - item.cutoff, 3);
        Assert.True(double.IsFinite(item.durRequired) && item.durRequired > 0);
        if (velocity == 200) Assert.Equal(0, item.offset, 3);
    }
}

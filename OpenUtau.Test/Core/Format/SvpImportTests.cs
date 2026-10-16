using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAudio.Wave;
using Newtonsoft.Json;
using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.Core.Format;

[Collection("AudioFixture")]
public sealed class SvpImportTests {
    static string DirectoryForTest() {
        var path = Path.Combine(Path.GetTempPath(), "UTvTU-SVP-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InstrumentalImportLoadsActualSamplesOrKeepsTheMissingPart(bool missing) {
        using var dispatcher = DocManagerTestSetup.EnterScopedDispatcher();
        var directory = DirectoryForTest();
        var audio = Path.Combine(directory, "backing.wav");
        if (!missing) {
            using var writer = new WaveFileWriter(audio, new WaveFormat(48000, 16, 1));
            writer.WriteSamples(Enumerable.Repeat(0.2f, 4800).ToArray(), 0, 4800);
        }
        var path = Path.Combine(directory, "audio.svp");
        File.WriteAllText(path, JsonConvert.SerializeObject(new {
            version = 100, tracks = new[] { new {
                name = "Backing", mainRef = new { isInstrumental = true, blickOffset = 705600000L,
                    audio = new { filename = "backing.wav", duration = 2.0 } },
            } },
        }));
        var project = Formats.ReadProject(new[] { path });
        Assert.Single(project.tracks);
        var part = Assert.IsType<UWavePart>(Assert.Single(project.parts));
        Assert.Equal(missing, part.DisplayName.StartsWith("[Missing]", StringComparison.Ordinal));
        Assert.Equal(480, part.position);
        Assert.Equal(missing ? 2000 : 100, part.fileDurationMs, 2);
        Assert.True(part.Duration > 0);
        if (!missing) {
            Assert.True(part.Peaks.Wait(TimeSpan.FromSeconds(3)));
            var peaks = part.Peaks.Result;
            Assert.NotNull(peaks);
            Assert.NotEmpty(part.Samples);
        }
        dispatcher.PumpAll();
    }

    [Theory]
    [InlineData(".svp")]
    [InlineData(".SVP")]
    [InlineData(".sv2")]
    public void VocalNotesKeepGroupOffsetPitchOffsetAndLength(string extension) {
        using var dispatcher = DocManagerTestSetup.EnterScopedDispatcher();
        var path = Path.Combine(DirectoryForTest(), "notes" + extension);
        File.WriteAllText(path, JsonConvert.SerializeObject(new {
            version = 100, tracks = new[] { new {
                name = "Vocal", mainRef = new { groupID = "g", blickOffset = 705600000L, pitchOffset = 2 },
                mainGroup = new { uuid = "g", name = "Phrase", notes = new[] { new { onset = 0L, duration = 705600000L, pitch = 60, lyrics = "a" } } },
            } },
        }) + "\0");
        Assert.Equal(ProjectFormats.Svp, Formats.DetectProjectFormat(path));
        var project = Formats.ReadProject(new[] { path });
        Assert.Single(project.tracks);
        var part = Assert.IsType<UVoicePart>(Assert.Single(project.parts));
        var note = Assert.Single(part.notes);
        Assert.Equal(480, part.position);
        Assert.Equal(0, note.position);
        Assert.Equal(480, note.duration);
        Assert.Equal(62, note.tone);
        dispatcher.PumpAll();
    }
}

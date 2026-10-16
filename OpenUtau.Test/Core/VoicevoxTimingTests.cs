using System;
using System.Linq;
using OpenUtau.Core;
using OpenUtau.Core.Voicevox;
using Xunit;

namespace OpenUtau.Test.Core;

public class VoicevoxTimingTests {
    [Theory]
    [InlineData(0, 1000, 0)]
    [InlineData(1253.1, 3378.7, 80)]
    [InlineData(10833.333, 11333.333, 120)]
    public void LayoutCoversBothPaddingRegionsOnTheSameAbsoluteGrid(double start, double end, double leading) {
        var layout = VoicevoxUtils.ComputeLayout(start, end, leading);
        double frame = 1000d / VoicevoxUtils.fps;
        double head = (Math.Round(VoicevoxUtils.headS * VoicevoxUtils.fps) + 1) * frame;
        Assert.Equal(VoicevoxUtils.SnapMs(start) - head, layout.positionMs, 7);
        Assert.Equal(end + Math.Round(VoicevoxUtils.tailS * VoicevoxUtils.fps) * frame,
            layout.positionMs - layout.leadingMs + layout.estimatedLengthMs, 7);
    }

    [Fact]
    public void ManyFractionalNotesUseAbsoluteEndpointsInsteadOfAccumulatingRoundingError() {
        const double start = 1234.567;
        var notes = Enumerable.Range(0, 200).Select(i => new VoicevoxNote {
            lyric = "あ", positionMs = start + i * 57.3, durationMs = 57.3, tone = 60,
        }).ToArray();
        var query = VoicevoxUtils.NoteGroupsToVQuery(notes, new TimeAxis());
        Assert.Equal(202, query.notes.Count);
        Assert.Equal(VoicevoxUtils.ToFrame(start + notes.Length * 57.3) - VoicevoxUtils.ToFrame(start),
            query.notes.Skip(1).SkipLast(1).Sum(n => n.frame_length));
        Assert.All(query.notes, n => Assert.True(n.frame_length >= 2));
    }

    [Theory]
    [InlineData(new[] { 94, 8, 12, 94 }, 20)]
    [InlineData(new[] { 94, 94 }, 0)]
    [InlineData(new int[] { }, 0)]
    public void PitchMaskExcludesSilentPadding(int[] lengths, int voiced) {
        var mask = VoicevoxRenderer.PaddingVoicedMask(lengths);
        Assert.Equal(lengths.Sum(), mask.Length);
        Assert.Equal(voiced, mask.Count(x => x));
        if (lengths.Length > 0) {
            Assert.All(mask.Take(lengths[0]), x => Assert.False(x));
            Assert.All(mask.TakeLast(lengths[^1]), x => Assert.False(x));
        }
    }
}

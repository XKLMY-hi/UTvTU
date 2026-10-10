using System;
using OpenUtau.App.Views;
using Xunit;

namespace OpenUtau.Test.App {
    public sealed class RenderWindowModePolicyTests {
        [Fact]
        public void RecordingMixdown_UsesFloatFormatAndFullLiveChain() {
            var policy = new RenderWindowModePolicy(true, 48000, false);
            Assert.Equal("WAV 32-bit float", policy.FormatLabel);
            Assert.Equal("48000 Hz", policy.SampleRateLabel);
            Assert.Equal("render.contract.mixdown", policy.HintKey);
            Assert.True(policy.IncludesEffects);
            Assert.True(policy.IncludesTrackVolumeAndPan);
            Assert.True(policy.RangeSelectionEnabled);
            Assert.False(policy.TrackSelectionEnabled);
            Assert.Null(policy.TrackFilter(new[] { 1, 3 }));
        }

        [Fact]
        public void Stems_UsePcm16DryFullSongAndOnlySelectedTracks() {
            var policy = new RenderWindowModePolicy(false, 96000, false);
            Assert.Equal("WAV 16-bit", policy.FormatLabel);
            Assert.Equal("96000 Hz", policy.SampleRateLabel);
            Assert.Equal("render.contract.pertrack", policy.HintKey);
            Assert.False(policy.IncludesEffects);
            Assert.False(policy.IncludesTrackVolumeAndPan);
            Assert.False(policy.RangeSelectionEnabled);
            Assert.True(policy.TrackSelectionEnabled);
            Assert.Equal(new[] { 1, 3 }, policy.TrackFilter(new[] { 1, 3, 1 }));
            Assert.Empty(policy.TrackFilter(Array.Empty<int>())!);
        }

        [Fact]
        public void MixdownShowsAllTracksWithoutChangingRememberedStemChoices() {
            var remembered = new[] { true, false, true, false };
            var mixdown = new RenderWindowModePolicy(true, 44100, false);
            var stems = new RenderWindowModePolicy(false, 44100, false);
            Assert.Equal(new[] { true, true, true, true }, mixdown.DisplayTrackSelection(remembered));
            Assert.Equal(new[] { true, false, true, false }, remembered);
            Assert.Equal(remembered, stems.DisplayTrackSelection(remembered));
        }

        [Theory]
        [InlineData(true, false, true, false, true)]
        [InlineData(false, false, true, true, false)]
        [InlineData(true, true, false, false, false)]
        [InlineData(false, true, false, false, false)]
        public void BusyLocksInputsAndRestoresOnlyEachModesRealControls(bool mixdown, bool busy,
            bool inputsEnabled, bool tracksEnabled, bool rangeEnabled) {
            var policy = new RenderWindowModePolicy(mixdown, 44100, busy);
            Assert.Equal(inputsEnabled, policy.InputsEnabled);
            Assert.Equal(tracksEnabled, policy.TrackSelectionEnabled);
            Assert.Equal(rangeEnabled, policy.RangeSelectionEnabled);
            Assert.Equal(mixdown, policy.IncludesEffects);
            Assert.Equal(mixdown, policy.IncludesTrackVolumeAndPan);
        }
    }
}

using System.Collections.Generic;
using OpenUtau.App.ViewModels;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Test.App {
    [Collection("Theme")]
    public class UpstreamSmallFixTests {
        [Fact]
        public void EmptyTargetVoiceColorsDoNotThrow() {
            var mapping = new VoiceColorMappingViewModel(new[] { "", "soft" }, System.Array.Empty<string>(), "Track");
            Assert.Equal(2, mapping.ColorMappings.Count);
            Assert.All(mapping.ColorMappings, row => Assert.Empty(row.NewColors));
        }

        [Fact]
        public void PortamentoPresetCopiesPointsOnCaptureAndEveryApplication() {
            var points = new List<PitchPoint> { new(-40, 0), new(0, 30), new(60, 0) };
            var preset = new NotePresets.PortamentoPreset("Curve", points);
            points[1].Y = 99;
            var first = NotePropertiesViewModel.CreatePitchFromPreset(preset);
            var second = NotePropertiesViewModel.CreatePitchFromPreset(preset);
            Assert.Equal(30, first.data[1].Y);
            first.data[1].Y = -20;
            Assert.Equal(30, second.data[1].Y);
            Assert.Equal(30, preset.PitchPoints[1].Y);
        }
    }
}

using OpenUtau.App.Controls;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Avalonia.Headless.XUnit;
using Xunit;

namespace OpenUtau.Test.App;

public class PartRenderProgressTests {
    static (UProject project, UVoicePart part) CreatePartWithTwoPhrases() {
        var project = new UProject();
        project.RegisterExpression(new UExpressionDescriptor("pitch deviation", OpenUtau.Core.Format.Ustx.PITD, -1200, 1200, 0) {
            type = UExpressionType.Curve,
        });
        foreach (var abbr in new[] {
            OpenUtau.Core.Format.Ustx.VEL, OpenUtau.Core.Format.Ustx.VOL, OpenUtau.Core.Format.Ustx.MOD,
            OpenUtau.Core.Format.Ustx.DIR, OpenUtau.Core.Format.Ustx.SHFT, OpenUtau.Core.Format.Ustx.CLR, OpenUtau.Core.Format.Ustx.CLRY
        }) {
            project.RegisterExpression(new UExpressionDescriptor(abbr, abbr, 0, 100, 0));
        }
        project.RegisterExpression(new UExpressionDescriptor("resampler", OpenUtau.Core.Format.Ustx.ENG, 0, 0, 0) {
            type = UExpressionType.Options,
            options = new[] { "default" },
        });
        var track = new UTrack(project);
        project.tracks.Clear();
        project.tracks.Add(track);
        var originalSine = Preferences.Default.PlaySineWhenNoSinger;
        Preferences.Default.PlaySineWhenNoSinger = true;
        try {
            track.RendererSettings.Validate(track);
            var part = new UVoicePart { trackNo = 0, position = 0, Duration = 1920 };
            var n1 = UNote.Create(); n1.position = 0; n1.duration = 480; n1.tone = 60;
            var n2 = UNote.Create(); n2.position = 960; n2.duration = 480; n2.tone = 64;
            n1.Next = n2; n2.Prev = n1;
            part.notes.Add(n1);
            part.notes.Add(n2);
            project.parts.Add(part);
            part.renderPhrases = RenderPhrase.FromPart(project, track, part);
            return (project, part);
        } finally {
            Preferences.Default.PlaySineWhenNoSinger = originalSine;
        }
    }

    [AvaloniaFact]
    public void PhraseBlocksTrackRenderStatusPreserveUnchangedHashesAndResetOnChange() {
        var (project, part) = CreatePartWithTwoPhrases();
        Assert.Equal(2, part.renderPhrases.Count);
        var canvas = new PartsCanvas();
        var control = new PartControl(part, canvas);
        try {
            Assert.True(control.RebuildPhraseBlocks(part));
            Assert.Equal(2, control.PhraseBlockCount);
            Assert.False(control.IsPhraseBlockRendered(0));
            Assert.False(control.IsPhraseBlockRendered(1));

            control.UpdateRenderStatus(part.renderPhrases[0], true);
            Assert.True(control.IsPhraseBlockRendered(0));
            Assert.False(control.IsPhraseBlockRendered(1));

            // Rebuild with identical phrases keeps rendered state and needs no redundant redraw.
            Assert.False(control.RebuildPhraseBlocks(part));
            Assert.True(control.IsPhraseBlockRendered(0));
            Assert.False(control.IsPhraseBlockRendered(1));

            // Changing second note's tone changes its hash while keeping first phrase rendered.
            part.notes.Max!.tone = 67;
            var originalSine = Preferences.Default.PlaySineWhenNoSinger;
            Preferences.Default.PlaySineWhenNoSinger = true;
            try {
                part.renderPhrases = RenderPhrase.FromPart(project, project.tracks[0], part);
            } finally {
                Preferences.Default.PlaySineWhenNoSinger = originalSine;
            }
            control.UpdateRenderStatus(part.renderPhrases[1], true); // not yet rebuilt -> ignored
            Assert.False(control.IsPhraseBlockRendered(1));
            control.RebuildPhraseBlocks(part);
            Assert.True(control.IsPhraseBlockRendered(0));
            Assert.False(control.IsPhraseBlockRendered(1));

            // Reset all clears rendered blocks.
            control.SetAllBlocks(false);
            Assert.False(control.IsPhraseBlockRendered(0));
            Assert.False(control.IsPhraseBlockRendered(1));
        } finally {
            control.Dispose();
        }
    }
}

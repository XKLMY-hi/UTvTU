using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Editing;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.Audio;

[Collection("AudioFixture")]
public class RenderedPitchRangeTests {
    sealed class Renderer : IRenderer {
        public USingerType SingerType => USingerType.Classic;
        public bool SupportsRenderPitch => true;
        public bool SupportsExpression(UExpressionDescriptor descriptor) => true;
        public RenderResult Layout(RenderPhrase phrase) => new();
        public Task<RenderResult> Render(RenderPhrase phrase, Progress progress, int trackNo, CancellationTokenSource cancellation, bool isPreRender = false) => throw new NotSupportedException();
        public UExpressionDescriptor[] GetSuggestedExpressions(USinger singer, URenderSettings settings) => Array.Empty<UExpressionDescriptor>();
        public override string ToString() => Renderers.CLASSIC;
        public RenderPitchResult LoadRenderedPitch(RenderPhrase phrase) => new() {
            ticks = new float[] { -200, 0, 50, 100, 200, 400, 900 },
            tones = new float[] { 90, 61, 90, 61, 61, 61, 90 },
            voiced = new[] { false, true, false, true, true, true, true },
        };
    }
    [Fact]
    public void LoadingIntoAnOffsetPartKeepsUnvoicedEditsAndPitchOutsideTheSelectedPhrase() {
        ToolsManager.Inst.Initialize();
        var singer = DummyVoicebank.Load(DummyVoicebank.Create(Path.Combine(Path.GetTempPath(), "UTvTU-pitchrange-" + Guid.NewGuid().ToString("N"))));
        var (project, part) = ClassicRenderSetup.BuildProject(singer, ("a", 60, 480));
        part.position = 2400;
        using (var session = ClassicRenderSetup.StartPhonemizer(project)) {
            ClassicRenderSetup.PhonemizeAndWait(project, part, timeoutMs: 3000);
        }
        using var dispatcher = DocManagerTestSetup.EnterScopedDispatcher();
        project.tracks[0].RendererSettings.Renderer = new Renderer();
        var curve = new UCurve(project.expressions[OpenUtau.Core.Format.Ustx.PITD]);
        curve.Set(50, 777, 50, 777); curve.Set(700, 555, 700, 555);
        part.curves.Add(curve);
        new LoadRenderedPitch().RunAsync(project, part, part.notes.ToList(), DocManager.Inst, (_, _) => { }, CancellationToken.None);
        dispatcher.PumpAll();
        Assert.Equal(777, curve.Sample(50));
        Assert.Equal(555, curve.Sample(700));
        Assert.NotEqual(0, curve.Sample(200));
    }
}

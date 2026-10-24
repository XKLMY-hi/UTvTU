using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.Core.ExpressionGraph;
using OpenUtau.Core.Format;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Test.Core;

[Collection("AudioFixture")]
public class GraphProjectPersistenceTests {
    [Fact]
    public void UstxpRetainsGraphsAndMasksAndDoesNotResurrectRemovedGraphs() {
        string folder = Path.Combine(Path.GetTempPath(), "utvtu-graph-project-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "graph.ustxp");
        try {
            var project = Ustx.Create();
            project.expressionGraphs = new() { ExpressionGraphEdits.CreateDefault("g", "Graph", Renderers.CLASSIC) };
            project.defaultExpressionGraphs = new() { [Renderers.CLASSIC] = "g" };
            project.tracks[0].ExpressionGraph = "g";
            var part = new UVoicePart { trackNo = 0, duration = 480 };
            part.maskedCurves.Add(new UMaskedCurve(Ustx.PITO));
            part.maskedCurves[0].SetValues(new[] { (0, 6000.25f), (5, 6100.5f), (100, 6300f) });
            project.parts.Add(part);
            // Product serializer + product loader, including Plus unknown-key passthrough.
            project.BeforeSave();
            File.WriteAllText(file, Ustxp.SerializeForSave(project));
            project.AfterSave();
            var read = Ustxp.Load(file);
            Assert.Equal("g", read.tracks[0].ExpressionGraph);
            Assert.Single(read.expressionGraphs!);
            Assert.Equal("g", read.defaultExpressionGraphs![Renderers.CLASSIC]);
            Assert.Equal(6000.25f, ((UVoicePart)read.parts[0]).maskedCurves[0].runs[0].ys[0]);
            Assert.Equal(2, ((UVoicePart)read.parts[0]).maskedCurves[0].runs.Count);
            var draft = new ExpressionGraphEdits.Draft(read);
            draft.Remove("g");
            new SetExpressionGraphsCommand(read, draft.ToState()).Execute();
            string saved = Ustxp.SerializeForSave(read);
            Assert.DoesNotContain("expression_graphs:", saved);
            Assert.DoesNotContain("default_expression_graphs:", saved);
            Assert.DoesNotContain("expression_graph:", saved);
        } finally {
            if (File.Exists(file)) File.Delete(file);
            if (File.Exists(file + ".bak")) File.Delete(file + ".bak");
            Directory.Delete(folder);
        }
    }
}

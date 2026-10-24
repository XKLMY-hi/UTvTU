using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.App.Views;
using OpenUtau.Core;
using OpenUtau.Core.ExpressionGraph;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.App;

[Collection("WorkspaceSession")]
public class ExpressionGraphInputTests : IDisposable {
    readonly DocManagerTestSetup.ScopedDispatcher dispatcher = DocManagerTestSetup.EnterScopedDispatcher();
    readonly UProject previous = DocManager.Inst.Project;
    readonly UProject project = OpenUtau.Core.Format.Ustx.Create();
    public ExpressionGraphInputTests() => DocManager.Inst.ExecuteCmd(new LoadProjectNotification(project));
    public void Dispose() {
        DocManager.Inst.ExecuteCmd(new LoadProjectNotification(previous));
        dispatcher.Dispose();
    }
    static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    static Border[] Nodes(ExpressionGraphCanvas canvas) => canvas.GetVisualDescendants().OfType<Border>().Where(b => b.Parent is Canvas).ToArray();
    static Point Center(Control c, Window window) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), window)!.Value;

    [AvaloniaFact]
    public void EmbeddedEditorLinksWithPointerAndSupportsGroupCopyAndUndo() {
        var tool = new ExpressionsDialog { DataContext = new ExpressionsViewModel() };
        var window = new Window { Width = 1250, Height = 800, Content = tool };
        try {
            window.Show();
            tool.FindControl<TabControl>("ExpressionPages")!.SelectedIndex = 1;
            ExpressionGraphEdits.Apply(project, draft => {
                var graph = new UExpressionGraph { id = "g", renderer = Renderers.CLASSIC, name = "Graph" };
                ExpressionGraphEdits.AddNode(graph, GraphNodeTypes.Constant, 20, 40);
                ExpressionGraphEdits.AddNode(graph, GraphNodeTypes.CurveOutput, 300, 40).Set("abbr", "dyn");
                draft.Graphs.Add(graph);
            });
            Pump(window);
            var editor = tool.FindControl<ExpressionGraphEditor>("GraphEditor")!;
            var canvas = editor.FindControl<ExpressionGraphCanvas>("Canvas")!;
            var nodes = Nodes(canvas);
            Assert.Equal(2, nodes.Length);
            // Node surfaces and output headers resolve dynamic roles after mounting.
            var surface = new SolidColorBrush(Color.Parse("#264658"));
            var titleColor = new SolidColorBrush(Color.Parse("#F4E6FF"));
            window.Resources["md3.surface-container"] = surface;
            window.Resources["md3.on-primary-container"] = titleColor;
            Pump(window);
            Assert.Same(surface, nodes[0].Background);
            Assert.Same(titleColor, nodes[1].GetVisualDescendants().OfType<TextBlock>().First().Foreground);
            var from = nodes[0].GetVisualDescendants().OfType<Ellipse>().Single();
            var to = nodes[1].GetVisualDescendants().OfType<Ellipse>().Single();
            var start = Center(from, window); var end = Center(to, window);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            window.MouseUp(end, MouseButton.Left);
            Pump(window);
            Assert.Single(project.expressionGraphs![0].links);
            canvas.SelectAll(); canvas.CopySelected(); canvas.DuplicateSelected(); Pump(window);
            Assert.Equal(4, project.expressionGraphs[0].nodes.Count);
            Assert.Equal(2, project.expressionGraphs[0].links.Count);
            DocManager.Inst.Undo(); Pump(window);
            Assert.Equal(2, project.expressionGraphs[0].nodes.Count);
            DocManager.Inst.Undo(); Pump(window);
            Assert.Empty(project.expressionGraphs[0].links);
            DocManager.Inst.Redo(); Pump(window);
            Assert.Single(project.expressionGraphs[0].links);
            var importedProject = OpenUtau.Core.Format.Ustx.Create();
            importedProject.RegisterExpression(new UExpressionDescriptor("Imported curve", "zz", 0, 100, 0) { type = UExpressionType.Curve });
            var importedGraph = new UExpressionGraph { id = "imported", name = "Imported", renderer = Renderers.CLASSIC };
            ExpressionGraphEdits.AddNode(importedGraph, GraphNodeTypes.CurveInput, 0, 0).Set("abbr", "zz");
            ExpressionGraphFile.Create(importedProject, importedGraph).ImportInto(project);
            Pump(window);
            var form = (ExpressionsViewModel)tool.DataContext!;
            Assert.Contains(form.Expressions, expression => expression.Abbr == "zz");
            // Applying the old expression form after using the graph tab must retain
            // imported descriptors and the built-in masked pitch descriptors.
            form.Apply();
            Assert.True(project.expressions.ContainsKey("zz"));
            Assert.Equal(UExpressionType.MaskedCurve, project.expressions[OpenUtau.Core.Format.Ustx.PITO].type);
            Assert.Equal(UExpressionType.MaskedCurve, project.expressions[OpenUtau.Core.Format.Ustx.RPIT].type);
        } finally { window.Content = null; window.Close(); }
    }

    [AvaloniaFact]
    public void TrackGraphChoiceUsesTheSharedWorldlineSlotAndRejectsOldProjectEdits() {
        project.expressionGraphs = new() {
            ExpressionGraphEdits.CreateDefault("a", "Graph A", Renderers.WORLDLINE_R),
            ExpressionGraphEdits.CreateDefault("b", "Graph B", Renderers.WORLDLINE_R2),
            ExpressionGraphEdits.CreateDefault("c", "Classic", Renderers.CLASSIC),
        };
        project.defaultExpressionGraphs = new() { [Renderers.WORLDLINE_R] = "a" };
        project.tracks[0].RendererSettings.renderer = Renderers.WORLDLINE_R11;
        var model = new TrackSettingsViewModel(project.tracks[0]);
        Assert.Equal(3, model.Graphs.Count);
        model.Graph = model.Graphs.Last(); model.Finish();
        Assert.Equal("b", project.tracks[0].ExpressionGraph);
        DocManager.Inst.Undo();
        Assert.Null(project.tracks[0].ExpressionGraph);
        DocManager.Inst.ExecuteCmd(new LoadProjectNotification(new UProject()));
        model.Finish();
        Assert.Null(project.tracks[0].ExpressionGraph);
    }
}

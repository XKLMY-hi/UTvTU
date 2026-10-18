using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Pointer = Avalonia.Input.Pointer;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.App.Views;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.App;

[Collection("WorkspaceSession")]
public class PitchToolInputTests : IDisposable {
    sealed class Tip : IValueTip {
        public void ShowValueTip() { }
        public void HideValueTip() { }
        public void UpdateValueTip(string text) { }
    }
    readonly DocManagerTestSetup.ScopedDispatcher dispatcher = DocManagerTestSetup.EnterScopedDispatcher();
    readonly UProject previous = DocManager.Inst.Project;
    readonly Preferences.SerializablePreferences preferences = Preferences.Default;
    readonly string dataPath = PathManager.Inst.DataPath;
    readonly UProject project = OpenUtau.Core.Format.Ustx.Create();
    readonly UVoicePart part = new() { Duration = 1920 };
    readonly string directory = Path.Combine(Path.GetTempPath(), "utvtu-pitch-input-" + Guid.NewGuid().ToString("N"));
    readonly ITestOutputHelper output;
    public PitchToolInputTests(ITestOutputHelper output) {
        this.output = output;
        Directory.CreateDirectory(directory);
        typeof(PathManager).GetProperty(nameof(PathManager.DataPath)).SetValue(PathManager.Inst, directory);
        Preferences.Default = new Preferences.SerializablePreferences { ShowTips = false };
        part.notes.Add(project.CreateNote(60, 0, 1440));
        project.parts.Add(part);
        DocManager.Inst.ExecuteCmd(new LoadProjectNotification(project));
        DocManager.Inst.SearchAllLegacyPlugins();
    }
    static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    static NoteEditState State(PianoRoll roll) => (NoteEditState)typeof(PianoRoll).GetField("editState", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(roll);
    (PianoRollViewModel vm, PianoRoll roll, Window window, NotesCanvas canvas) Open(int tool) {
        var vm = new PianoRollViewModel();
        vm.NotesViewModel.Part = part; vm.ToolIndex = tool; vm.PitchOverwrite = false;
        var roll = new PianoRoll(vm);
        var window = new Window { Width = 1100, Height = 850, Content = roll };
        window.Show(); Pump(window);
        roll.InitializePianoRollWindowAsync();
        vm.NotesViewModel.TickWidth = .5;
        vm.NotesViewModel.TickOffset = 0;
        vm.NotesViewModel.TrackHeight = 20;
        vm.NotesViewModel.TrackOffset = vm.NotesViewModel.TrackCount - 1 - 60 - 8;
        Pump(window);
        return (vm, roll, window, roll.GetVisualDescendants().OfType<NotesCanvas>().Single());
    }
    [AvaloniaTheory]
    [InlineData(7)]
    [InlineData(8)]
    public void CurvesSurviveMouseReleaseAndCommitOnTheConfirmationClick(int tool) {
        var (vm, roll, window, canvas) = Open(tool);
        try {
            Point At(int tick, double tone) => canvas.TranslatePoint(vm.NotesViewModel.TickToneToPoint(tick, tone), window).Value;
            var start = At(400, 60); var end = At(800, 62);
            output.WriteLine($"start={start} end={end} bounds={canvas.Bounds} hit={window.InputHitTest(start)?.GetType().Name} part={vm.NotesViewModel.Part != null} tool={vm.EditTool.CurrentTool}");
            window.MouseDown(start, MouseButton.Left);
            Assert.IsType<PitchCurveState>(State(roll));
            window.MouseMove(end);
            Assert.IsType<PitchCurveState>(State(roll));
            window.MouseUp(end, MouseButton.Left); Pump(window);
            output.WriteLine($"after release: {State(roll)?.GetType().Name}, curves={part.curves.Count}");
            Assert.True(Assert.IsType<PitchCurveState>(State(roll)).IsInAdjustingPhase);
            Assert.Empty(part.curves);
            window.MouseMove(end + new Vector(15, -8));
            window.MouseDown(end, MouseButton.Left); window.MouseUp(end, MouseButton.Left); Pump(window);
            Assert.Contains(part.curves, c => c.abbr == "pitd" && c.ys.Any(y => y != 0));
            Assert.Null(State(roll));
            Assert.Null(DocManager.Inst.ActiveUndoGroup);
            DocManager.Inst.Undo();
            Assert.All(part.curves, c => Assert.All(c.ys, y => Assert.Equal(0, y)));
            DocManager.Inst.Redo();
            Assert.Contains(part.curves, c => c.ys.Any(y => y != 0));
        } finally { window.Content = null; roll.Dispose(); vm.Dispose(); window.Close(); }
    }
    [AvaloniaFact]
    public void PendingCurveSupportsEscapeAndEnterAndToolSwitchWithoutAnEmptyUndoGroup() {
        var (vm, roll, window, canvas) = Open(8);
        try {
            void Pending() {
                var start = canvas.TranslatePoint(vm.NotesViewModel.TickToneToPoint(400, 60), window).Value;
                var end = canvas.TranslatePoint(vm.NotesViewModel.TickToneToPoint(800, 62), window).Value;
                window.MouseDown(start, MouseButton.Left); window.MouseMove(end); window.MouseUp(end, MouseButton.Left);
                Assert.True(Assert.IsType<PitchCurveState>(State(roll)).IsInAdjustingPhase);
            }
            void Key(Key key) => roll.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
            Pending(); Key(Avalonia.Input.Key.Escape);
            Assert.Null(State(roll)); Assert.Empty(part.curves); Assert.Null(DocManager.Inst.ActiveUndoGroup);
            Pending(); vm.ToolIndex = 7;
            Assert.Null(State(roll)); Assert.Empty(part.curves);
            Pending(); Key(Avalonia.Input.Key.Enter);
            Assert.Null(State(roll)); Assert.Contains(part.curves, c => c.ys.Any(y => y != 0));
            Assert.Null(DocManager.Inst.ActiveUndoGroup);
        } finally { window.Content = null; roll.Dispose(); vm.Dispose(); window.Close(); }
    }

    [AvaloniaFact]
    public void SmoothingAnExistingDeviationPreservesOutsideEditsAndUndoRedo() {
        var curve = new UCurve(project.expressions["pitd"]);
        curve.Set(600, 1000, 600, 1000); curve.Set(700, 321, 700, 321); part.curves.Add(curve);
        using var vm = new PianoRollViewModel();
        vm.NotesViewModel.Part = part; vm.NotesViewModel.TickWidth = .5; vm.NotesViewModel.TrackHeight = 20;
        var state = new SmoothenPitchState(new Border(), vm, new Tip(), false);
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        var point = vm.NotesViewModel.TickToneToPoint(600, 60);
        try {
            state.Begin(pointer, point); state.Update(pointer, point); state.End(pointer, point);
            Assert.InRange(curve.Sample(600), 1, 999); Assert.Equal(321, curve.Sample(700));
            DocManager.Inst.Undo(); Assert.Equal(1000, curve.Sample(600)); Assert.Equal(321, curve.Sample(700));
            DocManager.Inst.Redo(); Assert.InRange(curve.Sample(600), 1, 999);
        } finally { state.Cancel(); }
    }

    [AvaloniaFact]
    public void SmoothingWithoutAnOverwritePitchSourceDoesNotWriteZeroValuedFakeSamples() {
        var curve = new UCurve(project.expressions["pitd"]);
        curve.Set(600, 1000, 600, 1000); part.curves.Add(curve);
        var xs = curve.xs.ToArray(); var ys = curve.ys.ToArray();
        using var vm = new PianoRollViewModel();
        vm.NotesViewModel.Part = part; vm.NotesViewModel.TickWidth = .5; vm.NotesViewModel.TrackHeight = 20;
        var state = new SmoothenPitchState(new Border(), vm, new Tip(), true);
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        var point = vm.NotesViewModel.TickToneToPoint(600, 60);
        try {
            state.Begin(pointer, point); state.Update(pointer, point); state.End(pointer, point);
            Assert.Equal(xs, curve.xs); Assert.Equal(ys, curve.ys);
            Assert.Null(DocManager.Inst.ActiveUndoGroup);
        } finally { state.Cancel(); }
    }

    [AvaloniaFact]
    public void SmoothingTheOriginalNotePitchCreatesDeviationEvenWithoutAPreexistingCurve() {
        var note = part.notes.First();
        note.pitch.data.Clear();
        note.pitch.data.Add(new PitchPoint(0, 0));
        note.pitch.data.Add(new PitchPoint(600, 0));
        note.pitch.data.Add(new PitchPoint(625, 100));
        note.pitch.data.Add(new PitchPoint(650, 0));
        note.pitch.data.Add(new PitchPoint(1500, 0));
        var (vm, roll, window, canvas) = Open(9);
        try {
            var point = canvas.TranslatePoint(vm.NotesViewModel.TickToneToPoint(600, 60), window).Value;
            window.MouseDown(point, MouseButton.Left); window.MouseMove(point + new Vector(10, 0)); window.MouseUp(point, MouseButton.Left); Pump(window);
            Assert.Contains(part.curves, c => c.abbr == "pitd" && c.ys.Any(y => y != 0));
            Assert.Null(DocManager.Inst.ActiveUndoGroup);
        } finally { window.Content = null; roll.Dispose(); vm.Dispose(); window.Close(); }
    }
    public void Dispose() {
        if (DocManager.Inst.ActiveUndoGroup != null) DocManager.Inst.EndUndoGroup();
        DocManager.Inst.ExecuteCmd(new LoadProjectNotification(previous));
        Preferences.Default = preferences;
        typeof(PathManager).GetProperty(nameof(PathManager.DataPath)).SetValue(PathManager.Inst, dataPath);
        dispatcher.Dispose();
    }
}

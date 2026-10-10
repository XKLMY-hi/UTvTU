using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Test.TestSupport;
using ReactiveUI;
using Xunit;

namespace OpenUtau.Test.App {
    // A single expression row and a model: no PianoRoll, rendering, plugin or audio services.
    [Collection("WorkspaceSession")]
    public class NotePropertyExpressionInputTests : IDisposable {
        sealed class DescriptorOnlyRenderer : IRenderer {
            public USingerType SingerType => USingerType.Classic;
            public bool SupportsRenderPitch => false;
            public bool SupportsExpression(UExpressionDescriptor descriptor) => true;
            public RenderResult Layout(RenderPhrase phrase) => throw new NotSupportedException();
            public Task<RenderResult> Render(RenderPhrase phrase, OpenUtau.Core.Render.Progress progress, int trackNo,
                CancellationTokenSource cancellation, bool isPreRender = false) => throw new NotSupportedException();
            public RenderPitchResult LoadRenderedPitch(RenderPhrase phrase) => throw new NotSupportedException();
            public UExpressionDescriptor[] GetSuggestedExpressions(USinger singer, URenderSettings settings) => Array.Empty<UExpressionDescriptor>();
        }
        readonly DocManagerTestSetup.ScopedDispatcher dispatcher =
            DocManagerTestSetup.EnterScopedDispatcher(nullChannel: true, installScheduler: false);
        readonly UProject previous = DocManager.Inst.Project;
        readonly UProject project = new();
        readonly UVoicePart part = new() { Duration = 1920 };
        readonly UNote first;
        readonly UNote second;
        readonly NotePropertiesViewModel parent;
        readonly NotePropertyExpViewModel expression;
        readonly UExpressionDescriptor descriptor = new("Test expression", "test", -100, 100, 0);

        public NotePropertyExpressionInputTests() {
            project.RegisterExpression(descriptor);
            first = project.CreateNote(60, 0, 240);
            second = project.CreateNote(60, 480, 240);
            first.phonemeIndexes = new[] { 0 };
            second.phonemeIndexes = new[] { 0 };
            part.notes.Add(first);
            part.notes.Add(second);
            project.parts.Add(part);
            first.SetExpression(project, project.tracks[0], "test", new float?[] { 12 });
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(project));
            parent = new NotePropertiesViewModel { Part = part };
            expression = new NotePropertyExpViewModel(descriptor, parent);
            parent.Expressions.Add(expression);
            Select(first);
        }
        void Select(UNote note) {
            var selection = new NoteSelectionViewModel();
            selection.Add(note);
            MessageBus.Current.SendMessage(new NotesSelectionEvent(selection));
        }
        float? Value(UNote note) => note.phonemeExpressions.SingleOrDefault(e => e.abbr == "test")?.value;
        static KeyEventArgs Key(Control control, Key key, KeyModifiers modifiers = KeyModifiers.None) {
            var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers };
            control.RaiseEvent(args);
            return args;
        }
        NotePropertyExpression Row() => new() { DataContext = expression };
        static Window Mount(Control control) {
            var window = new Window { Width = 360, Height = 90, Content = control };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return window;
        }
        static Pointer Press(Slider slider) {
            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            Visual root = TopLevel.GetTopLevel(slider) ?? (Visual)slider;
            var point = slider.TranslatePoint(new Point(slider.Bounds.Width / 2, slider.Bounds.Height / 2), root)!.Value;
            slider.RaiseEvent(new PointerPressedEventArgs(slider, pointer, root, point, 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
            return pointer;
        }

        [AvaloniaFact]
        public void InvalidNonFiniteAndOutOfRangeText_PreservesExpressionAndCreatesNoHistory() {
            foreach (object invalid in new object[] { "abc", "NaN", float.NaN, float.PositiveInfinity, float.NegativeInfinity, "101", "-101" }) {
                using var edit = expression.BeginInputEdit();
                Assert.False(edit.ApplyExpression(expression, invalid));
                Assert.Equal(12f, Value(first));
                Assert.Null(DocManager.Inst.ActiveUndoGroup);
            }
        }
        [AvaloniaFact]
        public void BlankClearsTheOverride_AndUndoRestoresIt() {
            using (var edit = expression.BeginInputEdit()) Assert.True(edit.ApplyExpression(expression, "   "));
            Assert.Null(Value(first));
            DocManager.Inst.Undo();
            Assert.Equal(12f, Value(first));
        }
        [AvaloniaFact]
        public void SelectionProjectAndPartChanges_RejectTheCapturedTarget() {
            using var selectionEdit = expression.BeginInputEdit();
            Select(second);
            Assert.False(selectionEdit.ApplyExpression(expression, "30"));
            Assert.Equal(12f, Value(first));
            Assert.Null(Value(second));
            using var removedEdit = expression.BeginInputEdit();
            project.parts.Remove(part);
            Assert.False(removedEdit.ApplyExpression(expression, "30"));
            project.parts.Add(part);
            using var projectEdit = expression.BeginInputEdit();
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(new UProject()));
            Assert.False(projectEdit.ApplyExpression(expression, "30"));
            Assert.Null(Value(second));
        }
        [AvaloniaFact]
        public void ContinuedGestureAfterSaveSeal_ReopensOwnGroup_AndNeverClosesForeignGroup() {
            using var edit = expression.BeginInputEdit();
            Assert.True(edit.ApplyExpression(expression, 20f));
            var beforeSave = DocManager.Inst.ActiveUndoGroup;
            DocManager.Inst.EndUndoGroup(); // The same sealing operation used by Save/Undo.
            Assert.True(edit.ApplyExpression(expression, 25f));
            Assert.NotSame(beforeSave, DocManager.Inst.ActiveUndoGroup);
            edit.Dispose();
            DocManager.Inst.Undo();
            Assert.Equal(20f, Value(first));
            DocManager.Inst.Undo();
            Assert.Equal(12f, Value(first));
            using var foreignAttempt = expression.BeginInputEdit();
            DocManager.Inst.StartUndoGroup();
            var foreign = DocManager.Inst.ActiveUndoGroup;
            Assert.False(foreignAttempt.ApplyExpression(expression, 40f));
            foreignAttempt.Dispose();
            Assert.Same(foreign, DocManager.Inst.ActiveUndoGroup);
            Assert.Empty(foreign!.Commands);
        }
        [AvaloniaFact]
        public void TextEnterCommits_EscapeCancels_AndLostFocusRejectsInvalidText() {
            var row = Row();
            var window = Mount(row);
            try {
                row.ValueTextBox.Focus();
                row.ValueTextBox.Text = "33";
                Assert.True(Key(row.ValueTextBox, Avalonia.Input.Key.Enter).Handled);
                Assert.Equal(33f, Value(first));
                Assert.Null(DocManager.Inst.ActiveUndoGroup);
                row.ValueTextBox.Focus();
                row.ValueTextBox.Text = "80";
                Assert.True(Key(row.ValueTextBox, Avalonia.Input.Key.Escape).Handled);
                Assert.Equal(33f, Value(first));
                Assert.Equal("33", row.ValueTextBox.Text);
                row.ValueTextBox.Focus();
                row.ValueTextBox.Text = "abc";
                row.Focus();
                Assert.Equal(33f, Value(first));
                Assert.Equal("33", row.ValueTextBox.Text);
            } finally { window.Close(); }
        }
        [AvaloniaFact]
        public void TextEditCannotWriteTheNewSelection_WhenFocusLeavesLater() {
            var row = Row();
            var window = Mount(row);
            try {
                row.ValueTextBox.Focus();
                row.ValueTextBox.Text = "70";
                Select(second);
                row.Focus();
                Assert.Equal(12f, Value(first));
                Assert.Null(Value(second));
                Assert.Null(DocManager.Inst.ActiveUndoGroup);
            } finally { window.Close(); }
        }
        [AvaloniaFact]
        public void SliderKeyboardCommits_AndDetachEndsOwnDragWithoutWritingAfterRemount() {
            var row = Row();
            var host = new Panel { Children = { row } };
            var window = Mount(host);
            try {
                Key(row.slider, Avalonia.Input.Key.Right);
                Assert.Equal(13f, Value(first));
                Assert.Null(DocManager.Inst.ActiveUndoGroup);
                var pointer = Press(row.slider);
                row.slider.SetCurrentValue(RangeBase.ValueProperty, 20d);
                Assert.Equal(20f, Value(first));
                Assert.NotNull(DocManager.Inst.ActiveUndoGroup);
                host.Children.Remove(row);
                Assert.Null(DocManager.Inst.ActiveUndoGroup);
                pointer.Capture(null);
                row.slider.SetCurrentValue(RangeBase.ValueProperty, 60d);
                Assert.Equal(20f, Value(first));
                host.Children.Add(row);
                Key(row.slider, Avalonia.Input.Key.Home);
                Assert.Equal(-100f, Value(first));
            } finally { window.Close(); }
        }
        [AvaloniaFact]
        public void CaptureLostStopsDrag_AndAnotherControlsUndoGroupSurvivesDetach() {
            var row = Row();
            var host = new Panel { Children = { row } };
            var window = Mount(host);
            try {
                var pointer = Press(row.slider);
                row.slider.SetCurrentValue(RangeBase.ValueProperty, 20d);
                pointer.Capture(row.slider);
                pointer.Capture(null);
                Assert.Null(DocManager.Inst.ActiveUndoGroup);
                row.slider.SetCurrentValue(RangeBase.ValueProperty, 50d);
                Assert.Equal(20f, Value(first));
                DocManager.Inst.StartUndoGroup();
                var foreign = DocManager.Inst.ActiveUndoGroup;
                host.Children.Remove(row);
                Assert.Same(foreign, DocManager.Inst.ActiveUndoGroup);
                Assert.False(NotePropertiesViewModel.PanelControlPressed);
            } finally { window.Close(); }
        }

        [AvaloniaFact]
        public void RealSliderThumbCaptureLost_StopsTheExpressionGesture() {
            var row = Row();
            var window = Mount(row);
            try {
                var pointer = Press(row.slider);
                row.slider.SetCurrentValue(RangeBase.ValueProperty, 20d);
                Assert.NotNull(DocManager.Inst.ActiveUndoGroup);
                var thumb = row.slider.GetVisualDescendants().OfType<Thumb>().Single();
                pointer.Capture(thumb);
                pointer.Capture(null);
                Assert.Null(DocManager.Inst.ActiveUndoGroup);
                row.slider.SetCurrentValue(RangeBase.ValueProperty, 70d);
                Assert.Equal(20f, Value(first));
            } finally { window.Close(); }
        }
        [AvaloniaFact]
        public void DisposeReleasesChildren_RejectsQueuedCallbacks_AndStopsActiveInput() {
            using var edit = expression.BeginInputEdit();
            Assert.True(edit.ApplyExpression(expression, 22f));
            var worker = new Thread(() => parent.OnNext(new SetNotesSameExpressionCommand(project, project.tracks[0], part, new[] { first }, "test", 40f), false));
            worker.Start(); worker.Join();
            parent.Dispose(); parent.Dispose();
            Dispatcher.UIThread.RunJobs();
            Select(second);
            parent.LoadPart(part);
            Assert.True(parent.IsDisposed);
            Assert.True(expression.IsDisposed);
            Assert.Empty(parent.Expressions);
            Assert.Null(parent.Part);
            Assert.False(edit.ApplyExpression(expression, 50f));
            Assert.Null(DocManager.Inst.ActiveUndoGroup);
            Assert.Equal(22f, Value(first));
        }
        [AvaloniaFact]
        public void BasicParameterSession_RejectsInvalidNumbersAndChangedSelection() {
            using var edit = parent.BeginInputEdit();
            Assert.False(edit.ApplyParameter("Tuning", "NaN"));
            Assert.False(edit.ApplyParameter("PortamentoLength_TextBox", "abc"));
            Assert.Equal(0, first.tuning);
            Assert.True(edit.ApplyParameter("Tuning", "23"));
            Assert.Equal(23, first.tuning);
            Select(second);
            Assert.False(edit.ApplyParameter("Tuning", "67"));
            Assert.Equal(0, second.tuning);
            Assert.Null(DocManager.Inst.ActiveUndoGroup);
        }

        [AvaloniaFact]
        public void BasicTextCancellationAndInvalidBlur_RestoreTheCurrentDisplay() {
            var control = new NotePropertiesControl();
            using var model = (NotePropertiesViewModel)control.DataContext!;
            DocManager.Inst.ExecuteCmd(new LoadPartNotification(part, project, 0));
            Select(first);
            control.GetLogicalDescendants().OfType<Expander>().First().IsExpanded = true;
            var window = new Window { Width = 460, Height = 240, Content = control };
            window.Show();
            window.UpdateLayout();
            try {
                var box = control.GetLogicalDescendants().OfType<TextBox>().Single(b => (string?)b.Tag == "Tuning");
                box.Focus();
                box.Text = "23";
                Assert.True(Key(box, Avalonia.Input.Key.Enter).Handled);
                Assert.Equal(23, first.tuning);
                box.Focus();
                box.Text = "70";
                Assert.True(Key(box, Avalonia.Input.Key.Escape).Handled);
                Assert.Equal(23, first.tuning);
                Assert.Equal("23", box.Text);
                box.Focus();
                box.Text = "abc";
                control.Focus();
                Assert.Equal(23, first.tuning);
                Assert.Equal("23", box.Text);
                box.Focus();
                box.Text = "90";
                Select(second);
                control.Focus();
                Assert.Equal(23, first.tuning);
                Assert.Equal(0, second.tuning);
                Assert.Equal("0", box.Text);
            } finally { window.Close(); }
        }

        [AvaloniaFact]
        public void RefreshAfterEditing_PreservesThePanelsAutoVibratoOptions() {
            parent.AutoVibratoToggle = true;
            parent.AutoVibratoNoteLength = 960;
            using (var edit = parent.BeginInputEdit()) Assert.True(edit.ApplyParameter("Tuning", "25"));
            parent.RefreshInputs();
            Assert.True(parent.AutoVibratoToggle);
            Assert.Equal(960f, parent.AutoVibratoNoteLength);
            Assert.Equal(25, parent.Tuning);
            parent.AutoVibratoToggle = false;
            parent.AutoVibratoNoteLength = 720;
            expression.RefreshInput();
            Assert.False(parent.AutoVibratoToggle);
            Assert.Equal(720f, parent.AutoVibratoNoteLength);
        }

        [AvaloniaFact]
        public void NotificationBeforeMount_AndPartChangeWhileDetached_AreRestoredOnAttach() {
            var control = new NotePropertiesControl();
            using var model = (NotePropertiesViewModel)control.DataContext!;
            project.tracks[0].RendererSettings.Renderer = new DescriptorOnlyRenderer();
            DocManager.Inst.ExecuteCmd(new LoadPartNotification(part, project, 0));
            Assert.Same(part, model.Part);
            var beforeMount = Assert.Single(model.Expressions);
            var host = new Panel { Children = { control } };
            var window = Mount(host);
            try {
                Assert.True(beforeMount.IsDisposed);
                Assert.Single(control.ExpressionsPanel.Children);
                var old = Assert.IsType<NotePropertyExpression>(control.ExpressionsPanel.Children[0]);
                host.Children.Remove(control);
                var another = new UVoicePart { Duration = 1920 };
                project.parts.Add(another);
                DocManager.Inst.ExecuteCmd(new LoadPartNotification(another, project, 0));
                Assert.Same(another, model.Part);
                Assert.True(((NotePropertyExpViewModel)old.DataContext!).IsDisposed);
                host.Children.Add(control);
                Assert.Single(control.ExpressionsPanel.Children);
                Assert.NotSame(old, control.ExpressionsPanel.Children[0]);
                Assert.Same(another, model.Part);
            } finally { window.Close(); }
        }

        [AvaloniaFact]
        public void UndoDuringBasicSliderDrag_DoesNotCreateCommandsFromModelFeedback() {
            var control = new NotePropertiesControl();
            using var model = (NotePropertiesViewModel)control.DataContext!;
            DocManager.Inst.ExecuteCmd(new LoadPartNotification(part, project, 0));
            Select(first);
            var basic = control.GetLogicalDescendants().OfType<Expander>().First();
            basic.IsExpanded = true;
            var window = new Window { Width = 460, Height = 240, Content = control };
            window.Show();
            window.UpdateLayout();
            try {
                var tuning = control.GetLogicalDescendants().OfType<Slider>().Single(s => (string?)s.Tag == "Tuning");
                var pointer = Press(tuning);
                tuning.SetCurrentValue(RangeBase.ValueProperty, 20d);
                Assert.Equal(20, first.tuning);
                DocManager.Inst.Undo();
                Assert.Equal(0, first.tuning);
                Assert.Equal(0d, tuning.Value);
                Assert.Null(DocManager.Inst.ActiveUndoGroup);
                Assert.True(DocManager.Inst.GetRedoState(out _));
                tuning.SetCurrentValue(RangeBase.ValueProperty, 25d);
                Assert.Equal(25, first.tuning);
                Assert.NotNull(DocManager.Inst.ActiveUndoGroup);
                pointer.Capture(tuning.GetVisualDescendants().OfType<Thumb>().Single());
                pointer.Capture(null);
                Assert.Null(DocManager.Inst.ActiveUndoGroup);
                tuning.SetCurrentValue(RangeBase.ValueProperty, 70d);
                Assert.Equal(25, first.tuning);
            } finally { window.Close(); }
        }
        public void Dispose() {
            parent.Dispose();
            if (DocManager.Inst.ActiveUndoGroup != null) DocManager.Inst.EndUndoGroup();
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(previous));
            dispatcher.Dispose();
        }
    }
}

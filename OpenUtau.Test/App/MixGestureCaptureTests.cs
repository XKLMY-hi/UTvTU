using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using OpenUtau.App.Controls;
using OpenUtau.App.Views;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.App {
    [Collection("WorkspaceSession")]
    public class MixGestureCaptureTests : IDisposable {
        readonly DocManagerTestSetup.ScopedDispatcher dispatcher = DocManagerTestSetup.EnterScopedDispatcher();
        readonly UProject previous = DocManager.Inst.Project;
        readonly UProject project = new();
        public MixGestureCaptureTests() {
            project.tracks.Add(new UTrack("Other"));
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(project));
        }

        [AvaloniaFact]
        public void TrackHeaderFaderThumbCaptureLost_EndsItsMixGesture() {
            var (window, canvas, header, items) = TrackHeaderLayoutTests.Setup(105, track: project.tracks[0]);
            try {
                var slider = header.FindControl<Slider>("VolumeSlider")!;
                var thumb = slider.GetVisualDescendants().OfType<Thumb>().Single();
                var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
                pointer.Capture(thumb);
                header.ViewModel!.BeginMixGesture();
                header.ViewModel.Volume = -6;
                Assert.NotNull(DocManager.Inst.ActiveUndoGroup);
                pointer.Capture(null);
                Assert.Null(DocManager.Inst.ActiveUndoGroup);
                DocManager.Inst.Undo();
                Assert.Equal(0, project.tracks[0].Volume);
            } finally { items.Clear(); window.Close(); }
        }

        [AvaloniaFact]
        public void RackKnobCaptureLost_EndsItsMixGesture() {
            var dialog = new MixFxDialog(project.tracks[0]);
            try {
                dialog.Show(); dialog.UpdateLayout();
                var knob = dialog.GetVisualDescendants().OfType<Knob>().First();
                var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
                knob.RaiseEvent(new PointerPressedEventArgs(knob, pointer, knob, new Point(5, 5), 0,
                    new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
                    KeyModifiers.None));
                knob.Value = 3;
                Assert.NotNull(DocManager.Inst.ActiveUndoGroup);
                pointer.Capture(null);
                Assert.Null(DocManager.Inst.ActiveUndoGroup);
            } finally { dialog.Close(); }
        }

        public void Dispose() {
            if (DocManager.Inst.ActiveUndoGroup != null) DocManager.Inst.EndUndoGroup();
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(previous));
            dispatcher.Dispose();
        }
    }
}

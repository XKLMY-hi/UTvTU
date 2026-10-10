using System;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Linq;
using OpenUtau.App.Controls;
using OpenUtau.App.Views;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.App {
    [Collection("TrackMixEditing")]
    public class DawMixControlTests : IDisposable {
        readonly DocManagerTestSetup.ScopedDispatcher dispatcher =
            DocManagerTestSetup.EnterScopedDispatcher(nullChannel: true, installScheduler: false);
        readonly UProject previousProject = DocManager.Inst.Project;
        public void Dispose() {
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(previousProject));
            dispatcher.Dispose();
        }

        static KeyEventArgs KeyPress(Control control, Key key, KeyModifiers modifiers = KeyModifiers.None) {
            var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers };
            control.RaiseEvent(e);
            return e;
        }

        static DawFader NewFader() {
            var handle = new Border { Width = 24, Height = 14 };
            Canvas.SetLeft(handle, 24);
            var fader = new DawFader { Width = 80, Height = 500, Handle = handle };
            fader.Children.Add(handle);
            return fader;
        }

        static Pointer Press(DawFader fader, double y, bool left = true) {
            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            Visual root = TopLevel.GetTopLevel(fader) ?? (Visual)fader;
            var point = fader.TranslatePoint(new Point(30, y), root)!.Value;
            fader.RaiseEvent(new PointerPressedEventArgs(fader, pointer, root, point, 0,
                new PointerPointProperties(left ? RawInputModifiers.LeftMouseButton : RawInputModifiers.RightMouseButton,
                    left ? PointerUpdateKind.LeftButtonPressed : PointerUpdateKind.RightButtonPressed),
                KeyModifiers.None, 1));
            return pointer;
        }

        [AvaloniaFact]
        public void SelectedStrip_UsesOneAccentForNameReadoutAndHandle_WithoutChangingGeometry() {
            var strip = new MixerTrackStrip(new UTrack());
            try {
                MixerGeometryTests.InWindow(strip, 200, 900, window => {
                    var before = strip.Bounds.Size;
                    var foreground = strip.VolValueLabel.Foreground;
                    strip.IsSelected = true;
                    MixerGeometryTests.Settle(window);
                    Assert.NotEqual(foreground, strip.VolValueLabel.Foreground);
                    Assert.Equal(strip.VolValueLabel.Foreground, strip.TrackNameLabel.Foreground);
                    Assert.Equal(strip.VolValueLabel.Foreground, strip.FaderHandle.Background);
                    Assert.Equal(before, strip.Bounds.Size);
                    strip.IsSelected = false;
                    MixerGeometryTests.Settle(window);
                    Assert.Equal(foreground, strip.VolValueLabel.Foreground);
                });
            } finally { strip.DisposeSubscriptions(); }
        }

        [AvaloniaFact]
        public void TrackHeaderTemplates_ForwardTheirFontsAndColors_AndFocusDoesNotGrowButtons() {
            var (window, _, header, items) = TrackHeaderLayoutTests.Setup(105, 240);
            try {
                var label = header.TrackNameButton.GetVisualDescendants().OfType<TextBlock>()
                    .Single(text => text.Name == "PART_Label");
                Assert.Equal(header.TrackNameButton.FontSize, label.FontSize);
                Assert.Equal(header.TrackNameButton.Foreground, label.Foreground);
                var before = header.MuteButton.Bounds.Size;
                header.MuteButton.IsChecked = true;
                MixerGeometryTests.Settle(window);
                var glyph = header.MuteButton.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Label");
                Assert.Equal(header.MuteButton.Foreground, glyph.Foreground);
                Assert.Equal(header.SoloButton.Theme, header.MuteButton.Theme);
                header.MuteButton.Focus(NavigationMethod.Tab);
                MixerGeometryTests.Settle(window);
                var ring = header.MuteButton.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PART_FocusRing");
                Assert.Equal(1, ring.Opacity);
                Assert.Equal(before, header.MuteButton.Bounds.Size);
                header.MuteButton.IsEnabled = false;
                MixerGeometryTests.Settle(window);
                Assert.Equal(.38, header.MuteButton.Opacity, 6);
            } finally { items.Clear(); window.Close(); }
        }

        [AvaloniaFact]
        public void TrackHeaderSliders_UseTheirOwnedTemplates_AndNativeKeyboardInput() {
            var (window, _, header, items) = TrackHeaderLayoutTests.Setup(105, 240);
            try {
                foreach (var slider in new[] { header.VolumeSlider, header.PanSlider }) {
                    Assert.True(slider.Focusable);
                    Assert.DoesNotContain("fader", slider.Classes);
                    Assert.Contains(slider.GetVisualDescendants(), child => child is Track);
                    Assert.Contains(slider.GetVisualDescendants(), child => child is Thumb);
                    double before = slider.Value;
                    slider.Focus();
                    Assert.True(MainWindow.ShouldYieldShortcutToFocus(window.FocusManager!.GetFocusedElement(), Key.Home, KeyModifiers.None));
                    KeyPress(slider, Key.Right);
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(slider.Value > before);
                }
                Assert.Equal(header.VolumeSlider.Value, items[0].Volume);
                Assert.Equal(header.PanSlider.Value / 100, items[0].Pan);
            } finally { items.Clear(); window.Close(); }
        }

        [AvaloniaFact]
        public void NumericEditor_CannotResurrectTheValueAfterUndo() {
            var project = new UProject { Saved = true };
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(project));
            var strip = new MixerTrackStrip(project.tracks[0]);
            try {
                MixerGeometryTests.InWindow(strip, 200, 900, () => {
                    strip.VolumeField.BeginEdit();
                    strip.VolumeField.Editor.Text = "-9";
                    KeyPress(strip.VolumeField.Editor, Key.Enter);
                    Assert.Equal(-9, project.tracks[0].Volume);
                    strip.VolumeField.BeginEdit();
                    DocManager.Inst.Undo();
                    Assert.False(strip.VolumeField.IsEditing);
                    strip.VolumeField.Editor.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));
                    Assert.Equal(0, project.tracks[0].Volume);
                    Assert.False(DocManager.Inst.GetUndoState(out _));
                });
            } finally { strip.DisposeSubscriptions(); }
        }

        [AvaloniaFact]
        public void FaderAutomation_ReportsRealRange_AndWritesThroughTheUserEditBoundary() {
            var fader = NewFader();
            int edits = 0;
            fader.ValueEdited += (_, _) => edits++;
            var peer = ControlAutomationPeer.CreatePeerForElement(fader)!;
            var range = peer.GetProvider<IRangeValueProvider>()!;
            Assert.Equal(AutomationControlType.Slider, peer.GetAutomationControlType());
            Assert.Equal(-24, range.Minimum);
            Assert.Equal(12, range.Maximum);
            range.SetValue(-6);
            Assert.Equal(-6, fader.Value);
            Assert.Equal(1, edits);
            fader.IsEnabled = false;
            Assert.True(range.IsReadOnly);
            range.SetValue(2);
            Assert.Equal(-6, fader.Value);
        }

        [AvaloniaFact]
        public void FocusedRangeAndValueControls_KeepTheirKeys_AndGlobalSaveStillWorks() {
            foreach (var control in new Control[] { NewFader(), new Slider { Focusable = true }, new DawValueField() }) {
                MixerGeometryTests.InWindow(control, 150, 600, window => {
                    control.Focus();
                    Dispatcher.UIThread.RunJobs();
                    var focused = window.FocusManager!.GetFocusedElement();
                    Assert.NotNull(focused);
                    if (control is DawValueField) {
                        Assert.True(MainWindow.ShouldYieldShortcutToFocus(focused, Key.Enter, KeyModifiers.None));
                        Assert.True(MainWindow.ShouldYieldShortcutToFocus(focused, Key.F2, KeyModifiers.None));
                    } else {
                        foreach (Key key in new[] { Key.Up, Key.Down, Key.Home, Key.End, Key.PageUp, Key.PageDown })
                            Assert.True(MainWindow.ShouldYieldShortcutToFocus(focused, key, KeyModifiers.Shift));
                    }
                    Assert.False(MainWindow.ShouldYieldShortcutToFocus(focused, Key.S, KeyModifiers.Control));
                    Assert.False(MainWindow.ShouldYieldShortcutToFocus(focused, Key.Space, KeyModifiers.None));
                });
            }
        }

        [AvaloniaFact]
        public void FaderKeyboard_ClampsAndOffersFineAdjustment_WithoutFeedbackFromRefresh() {
            var fader = NewFader();
            int edits = 0;
            fader.ValueEdited += (_, _) => edits++;
            fader.Value = -6;
            Assert.Equal(0, edits);
            Assert.True(KeyPress(fader, Key.Up).Handled);
            KeyPress(fader, Key.Down, KeyModifiers.Shift);
            Assert.Equal(-5.1, fader.Value, 6);
            KeyPress(fader, Key.Home);
            KeyPress(fader, Key.Up);
            Assert.Equal(12, fader.Value);
            KeyPress(fader, Key.End);
            KeyPress(fader, Key.Down);
            Assert.Equal(-24, fader.Value);
            KeyPress(fader, Key.D0);
            Assert.Equal(0, fader.Value);
            Assert.Equal(5, edits);
        }

        [AvaloniaFact]
        public void FaderDrag_GrabsWithoutJump_AndEndsOnceOnCaptureLoss() {
            var fader = NewFader();
            int starts = 0, ends = 0;
            fader.GestureStarted += (_, _) => starts++;
            fader.GestureEnded += (_, _) => ends++;
            MixerGeometryTests.InWindow(fader, 100, 600, window => {
                double top = Canvas.GetTop(fader.Handle!);
                var pointer = Press(fader, top + 4);
                Assert.Equal(0, fader.Value, 6);
                Assert.True(fader.IsDragging);
                fader.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, fader, pointer, window,
                    fader.TranslatePoint(new Point(30, 450), window)!.Value, 0,
                    new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other), KeyModifiers.None));
                Assert.True(fader.Value < -15);
                pointer.Capture(null);
                fader.EndGesture();
                Assert.False(fader.IsDragging);
                Assert.Equal(1, starts);
                Assert.Equal(1, ends);
            });
        }

        [AvaloniaFact]
        public void FaderRightClickAndDisabledInput_DoNotEditOrStartGesture() {
            var fader = NewFader();
            int starts = 0;
            fader.GestureStarted += (_, _) => starts++;
            MixerGeometryTests.InWindow(fader, 100, 600, () => {
                Press(fader, 450, left: false);
                Assert.False(fader.IsDragging);
                Assert.Equal(0, fader.Value);
                fader.IsEnabled = false;
                KeyPress(fader, Key.Up);
                Press(fader, 450);
                Assert.Equal(0, starts);
                Assert.Equal(0, fader.Value);
            });
        }

        [AvaloniaFact]
        public void ValueField_EscapeThenLostFocus_CannotCommit() {
            var field = new DawValueField { Value = -3 };
            int commits = 0;
            field.ValueCommitted += (_, _) => commits++;
            MixerGeometryTests.InWindow(field, 100, 80, () => {
                KeyPress(field, Key.F2);
                Assert.True(field.IsEditing);
                field.Editor.Text = "-12";
                KeyPress(field.Editor, Key.Escape);
                field.Editor.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));
                Assert.False(field.IsEditing);
                Assert.True(field.Readout.IsVisible);
                Assert.Equal(-3, field.Value);
                Assert.Equal(0, commits);
            });
        }

        [AvaloniaTheory]
        [InlineData("NaN")]
        [InlineData("Infinity")]
        [InlineData("garbage")]
        public void ValueField_InvalidValuesNeverReachTheModel(string input) {
            var field = new DawValueField { Value = -3 };
            int commits = 0;
            field.ValueCommitted += (_, _) => commits++;
            MixerGeometryTests.InWindow(field, 100, 80, () => {
                field.BeginEdit();
                field.Editor.Text = input;
                KeyPress(field.Editor, Key.Enter);
                Assert.True(field.IsEditing);
                Assert.Equal(-3, field.Value);
                Assert.Equal(0, commits);
                field.Editor.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));
                Assert.False(field.IsEditing);
                Assert.Equal(0, commits);
            });
        }

        [AvaloniaFact]
        public void ValueField_ClampedCommitAndLostFocus_DispatchOnce() {
            var field = new DawValueField { Value = -3 };
            int commits = 0;
            field.ValueCommitted += (_, _) => commits++;
            MixerGeometryTests.InWindow(field, 100, 80, () => {
                field.BeginEdit();
                field.Editor.Text = "100";
                KeyPress(field.Editor, Key.Enter);
                field.Editor.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));
                Assert.Equal(12, field.Value);
                Assert.Equal(1, commits);
                Assert.False(field.IsEditing);
            });
        }

        [AvaloniaFact]
        public void StripKeyboardAndNumericEdit_UseDocumentUndo_AndOldEditorCannotWriteNewTrack() {
            var project = new UProject { Saved = true };
            project.tracks.Add(new UTrack("Harmony") { TrackNo = 1 });
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(project));
            var strip = new MixerTrackStrip(project.tracks[0]);
            try {
                MixerGeometryTests.InWindow(strip, 200, 900, () => {
                    KeyPress(strip.FaderBox, Key.Down);
                    Assert.Equal(-1, project.tracks[0].Volume);
                    DocManager.Inst.Undo();
                    Assert.Equal(0, project.tracks[0].Volume);
                    strip.VolumeField.BeginEdit();
                    strip.VolumeField.Editor.Text = "-9";
                    KeyPress(strip.VolumeField.Editor, Key.Enter);
                    Assert.Equal(-9, project.tracks[0].Volume);
                    DocManager.Inst.Undo();
                    Assert.Equal(0, project.tracks[0].Volume);
                    strip.VolumeField.BeginEdit();
                    strip.VolumeField.Editor.Text = "-18";
                    strip.Track = project.tracks[1];
                    KeyPress(strip.VolumeField.Editor, Key.Enter);
                    Assert.Equal(0, project.tracks[0].Volume);
                    Assert.Equal(0, project.tracks[1].Volume);
                });
            } finally { strip.DisposeSubscriptions(); }
        }

        [AvaloniaFact]
        public void DetachDuringDrag_EndsUndoGroup_AndRemountRestoresEditing() {
            var project = new UProject { Saved = true };
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(project));
            var strip = new MixerTrackStrip(project.tracks[0]);
            var host = new StackPanel { Children = { strip } };
            MixerGeometryTests.InWindow(host, 200, 900, window => {
                Press(strip.FaderBox, 450);
                Assert.True(strip.FaderBox.IsDragging);
                Assert.NotNull(DocManager.Inst.ActiveUndoGroup);
                host.Children.Remove(strip);
                Assert.False(strip.FaderBox.IsDragging);
                Assert.Null(DocManager.Inst.ActiveUndoGroup);
                DocManager.Inst.Undo();
                Assert.Equal(0, project.tracks[0].Volume);
                host.Children.Add(strip);
                MixerGeometryTests.Settle(window);
                KeyPress(strip.FaderBox, Key.Down);
                Assert.Equal(-1, project.tracks[0].Volume);
            });
            strip.DisposeSubscriptions();
        }
    }
}

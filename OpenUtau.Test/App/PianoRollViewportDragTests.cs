using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App.Controls;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Test.TestSupport;
using ReactiveUI;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// 卷帘视口指示条的**拖拽滚动**（上游 `9caec1a6`）。
    ///
    /// 上游把拖拽入口接在 `MainWindow.axaml.cs` 的部件指针状态机里（布局线文件），
    /// 本树改为 **`PartControl` 自带指针处理** + 一条 `PianoRollViewportScrollEvent`
    /// 递给卷帘 ⇒ 这里可以直接量：
    ///   · 命中区几何：把手命中 / 非命中 / 非打开部件 / 无视口 / 把手放不下；
    ///   · 拖拽行为：在把手按下右移 30px（tickWidth=0.3 ⇒ +100 tick）时，
    ///     发出的目标偏移 = 起始偏移 + 100，且**不进撤销栈**。
    /// </summary>
    public class PianoRollViewportDragTests {
        const double TickWidth = 0.3;
        const double TrackHeight = 120;
        const int Duration = 960;

        static readonly Pointer TestPointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);

        static (WindowEx win, PartControl control, PartsCanvas canvas, UPart part) Setup(double viewOffset = 100, double viewportTicks = 200) {
            DocManagerTestSetup.RunOnCurrentThread();
            var project = new UProject();
            project.timeAxis.BuildSegments(project);
            var part = new UVoicePart { trackNo = 0, position = 0, duration = Duration };
            project.parts.Add(part);
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(project));

            var canvas = new PartsCanvas();
            canvas.Bind(PartsCanvas.TickWidthProperty, Observable.Return(TickWidth));
            canvas.Bind(PartsCanvas.TrackHeightProperty, Observable.Return(TrackHeight));
            canvas.Bind(PartsCanvas.TickOffsetProperty, Observable.Return(0.0));
            canvas.Bind(PartsCanvas.TrackOffsetProperty, Observable.Return(0.0));
            var items = new ObservableCollection<UPart>();
            canvas.Items = items;
            items.Add(part);
            canvas.PianoRollOpenPart = part;
            canvas.PianoRollViewTickOffset = viewOffset;
            canvas.PianoRollViewViewportTicks = viewportTicks;

            var win = new WindowEx { Width = 600, Height = 300, Content = canvas };
            win.Classes.Set("no-motion", true);
            win.Show();
            win.Measure(new Size(600, 300));
            win.Arrange(new Rect(0, 0, 600, 300));
            Dispatcher.UIThread.RunJobs();
            win.UpdateLayout();
            var control = canvas.GetVisualDescendants().OfType<PartControl>().Single();
            return (win, control, canvas, part);
        }

        static void Press(Control target, Point p) =>
            target.RaiseEvent(new PointerPressedEventArgs(target, TestPointer, target, p, 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
                KeyModifiers.None, 1));

        static void Move(Control target, Point p) =>
            target.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, target, TestPointer, target, p, 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other),
                KeyModifiers.None));

        static void Release(Control target, Point p) =>
            target.RaiseEvent(new PointerReleasedEventArgs(target, TestPointer, target, p, 0,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
                KeyModifiers.None, MouseButton.Left));

        [AvaloniaFact]
        public void HandleHitTest_OnlyOnTheOpenPartsViewportGrip() {
            var (win, control, canvas, part) = Setup();
            try {
                // 视口 100..300 tick ⇒ 30..90 px；把手在视口中心（60px 处）
                Assert.True(control.HitPianoRollViewportHandle(new Point(60, TrackHeight / 2)),
                    "视口中心（把手）应当命中");
                Assert.False(control.HitPianoRollViewportHandle(new Point(5, TrackHeight / 2)),
                    "视口左侧（非把手）不应命中");
                Assert.False(control.HitPianoRollViewportHandle(new Point(200, TrackHeight / 2)),
                    "视口右侧（非把手）不应命中");

                // 不是"打开的那个部件" ⇒ 没有指示条 ⇒ 不命中
                canvas.PianoRollOpenPart = null;
                Assert.False(control.HitPianoRollViewportHandle(new Point(60, TrackHeight / 2)));

                // 视口不可见（viewportTicks = 0）⇒ 不命中
                canvas.PianoRollOpenPart = part;
                canvas.PianoRollViewViewportTicks = 0;
                Assert.False(control.HitPianoRollViewportHandle(new Point(60, TrackHeight / 2)));
            } finally {
                win.Close();
            }
        }

        [AvaloniaFact]
        public void DraggingTheGrip_ScrollsThePianoRollByTheDragDistance() {
            var (win, control, canvas, _) = Setup(viewOffset: 100, viewportTicks: 200);
            try {
                int undoDepthBefore = UndoDepth();
                var targets = new List<double>();
                using var sub = MessageBus.Current.Listen<PianoRollViewportScrollEvent>()
                    .ObserveOn(ImmediateScheduler.Instance)
                    .Subscribe(e => targets.Add(e.TickOffset));

                var grip = new Point(60, TrackHeight / 2);
                Press(control, grip);
                Assert.Equal(HandCursors.Grabbing, control.Cursor);
                Move(control, grip + new Point(30, 0));      // +30px ÷ 0.3 px/tick = +100 tick
                Release(control, grip + new Point(30, 0));

                Assert.NotEmpty(targets);
                Assert.Equal(100 + 100, targets[^1], 3);
                // 拖拽只改视口，不产生撤销步骤
                Assert.Equal(undoDepthBefore, UndoDepth());
                Assert.Null(control.Cursor);
            } finally {
                win.Close();
            }
        }

        [AvaloniaFact]
        public void HoveringTheGrip_ShowsTheGrabCursor() {
            var (win, control, _, _) = Setup();
            try {
                Move(control, new Point(60, TrackHeight / 2));
                Assert.Equal(HandCursors.Grab, control.Cursor);
                Move(control, new Point(5, TrackHeight / 2));
                Assert.Null(control.Cursor);
            } finally {
                win.Close();
            }
        }

        static int UndoDepth() {
            int depth = 0;
            while (DocManager.Inst.GetUndoState(out _)) {
                // GetUndoState 不消耗栈，这里只统计"是否有可撤销步骤"
                depth = 1;
                break;
            }
            return depth;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Test.TestSupport;
using ReactiveUI;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// 编排区重绘范围的**可测证据**（上游 `96473fa5`：Redraw only the open part when the
    /// piano roll viewport moves）。
    ///
    /// 背景：`PartControl` 原先在 `PianoRollViewTickOffset` / `PianoRollViewViewportTicks`
    /// 变化时各自 `InvalidateVisual()`，而这两个属性是**每个部件**都绑定到
    /// `PartsCanvas` 的 ⇒ 卷帘每滚一帧，编排区所有部件都重绘（含波形部件重建波形）。
    /// 上游的观察是：只有**打开的那个部件**会画卷帘视口高亮，而且 `PartsCanvas` 已经
    /// 自己失效了它（`InvalidatePartViewport()`）⇒ 其余部件的重绘是纯浪费。
    ///
    /// headless 渲染循环**尊重失效标记**（本仓实测：无改动再 tick 不重绘，
    /// `InvalidateVisual` 后 tick 才重绘）⇒ 可以用"每帧各部件 RenderCount 的增量"直接量化。
    /// </summary>
    public class PartRedrawScopeTests {
        const int PartCount = 3;

        static (WindowEx win, PartsCanvas canvas, List<PartControl> controls, List<UPart> parts) Setup() {
            DocManagerTestSetup.RunOnCurrentThread();
            var project = new UProject();
            project.timeAxis.BuildSegments(project);
            var parts = new List<UPart>();
            for (int i = 0; i < PartCount; i++) {
                var part = new UVoicePart { trackNo = 0, position = i * 480, duration = 960 };
                project.parts.Add(part);
                parts.Add(part);
            }
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(project));

            var canvas = new PartsCanvas();
            // 生产里这四条是 MainWindow.axaml 的绑定（CLR setter 是私有）⇒ 测试同样走绑定
            canvas.Bind(PartsCanvas.TickWidthProperty, Observable.Return(0.3));
            canvas.Bind(PartsCanvas.TrackHeightProperty, Observable.Return(120.0));
            canvas.Bind(PartsCanvas.TickOffsetProperty, Observable.Return(0.0));
            canvas.Bind(PartsCanvas.TrackOffsetProperty, Observable.Return(0.0));
            // Items 先给空集合、再逐个 Add ⇒ 触发 CollectionChanged 建出部件控件
            var items = new ObservableCollection<UPart>();
            canvas.Items = items;
            foreach (var part in parts) {
                items.Add(part);
            }
            canvas.PianoRollOpenPart = parts[0];
            canvas.PianoRollViewTickOffset = 0;
            canvas.PianoRollViewViewportTicks = 200;

            var win = new WindowEx { Width = 900, Height = 400, Content = canvas };
            win.Classes.Set("no-motion", true);
            win.Show();
            Layout(win);
            return (win, canvas, controls: canvas.GetVisualDescendants().OfType<PartControl>().ToList(), parts);
        }

        static void Layout(Window win) {
            win.Measure(new Size(900, 400));
            win.Arrange(new Rect(0, 0, 900, 400));
            Dispatcher.UIThread.RunJobs();
            win.UpdateLayout();
        }

        /// <summary>推进一帧渲染（headless 渲染循环只重绘失效的控件）。</summary>
        static void Frame(Window win) {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(30);
            Dispatcher.UIThread.RunJobs();
            win.UpdateLayout();
        }

        [AvaloniaFact]
        public void ScrollingThePianoRollViewport_RedrawsOnlyTheOpenPart() {
            var (win, canvas, controls, parts) = Setup();
            try {
                Assert.Equal(PartCount, controls.Count);
                // 首帧：三个部件各画一次（都可见）
                Frame(win);
                var before = controls.Select(c => c.RenderCount).ToArray();
                Assert.All(before, n => Assert.True(n > 0, $"部件首帧应当绘制过，实际 {n}"));

                // 卷帘滚动一帧 = 只改这两个视口属性（编排区里体现为"视口高亮挪一格"）
                canvas.PianoRollViewTickOffset = 60;
                canvas.PianoRollViewViewportTicks = 200;
                Frame(win);

                var after = controls.Select(c => c.RenderCount).ToArray();
                int redrawn = after.Zip(before, (a, b) => a - b).Count(d => d > 0);
                Assert.True(redrawn == 1,
                    $"卷帘滚动一帧只应重绘打开的那个部件，实际重绘 {redrawn} 个（增量 {string.Join(",", after.Zip(before, (a, b) => a - b))}）");

                // 被重绘的那个正好是 open part（PartsCanvas.InvalidatePartViewport 只失效它）
                int openIndex = parts.FindIndex(p => ReferenceEquals(p, canvas.PianoRollOpenPart));
                int redrawnIndex = after.Zip(before, (a, b) => a - b).ToList().FindIndex(d => d > 0);
                Assert.Equal(openIndex, redrawnIndex);

                // 再滚一帧：仍然只重绘那一个
                canvas.PianoRollViewTickOffset = 120;
                Frame(win);
                var after2 = controls.Select(c => c.RenderCount).ToArray();
                int redrawn2 = after2.Zip(after, (a, b) => a - b).Count(d => d > 0);
                Assert.True(redrawn2 == 1, $"第二次滚动同样只应重绘一个，实际 {redrawn2}");
            } finally {
                win.Close();
            }
        }

        [AvaloniaFact]
        public void OtherInvalidations_StillRedraw_NoRegressionOnSelectionAndText() {
            var (win, canvas, controls, parts) = Setup();
            try {
                Frame(win);
                var before = controls.Select(c => c.RenderCount).ToArray();

                // 选中态变化必须仍然触发重绘（本改动只摘掉"视口变化"这一条）
                MessageBus.Current.SendMessage(new PartsSelectionEvent(
                    new[] { parts[1] }, Array.Empty<UPart>()));
                Frame(win);
                var after = controls.Select(c => c.RenderCount).ToArray();
                Assert.True(after.Zip(before, (a, b) => a - b).Count(d => d > 0) >= 1,
                    "选中态变化仍应触发部件重绘");
            } finally {
                win.Close();
            }
        }
    }
}

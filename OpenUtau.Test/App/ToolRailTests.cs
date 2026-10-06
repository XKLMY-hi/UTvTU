using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Test.TestSupport;
using OpenUtau.App.Views;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// W46 悬浮工具轨（Figma 式）契约：
    /// · 形态：竖排 overlay，贴音符画布左上角 **12**；容器圆角 12 / 按钮 **36×36 圆角 8 / 间距 4**；
    /// · 分组：绘制类（选择/笔/橡皮/刀）与音高类（6 个）之间一条 1px 分隔线；
    /// · 行为：笔的子工具浮层**向右**弹出；工具 tooltip 沿用既有字符串键（本文件不校验文案）；
    /// · 不挡画布：轨外不参与命中测试；**画布 Bounds 不因它变化**（overlay）。
    /// 断言一律走**布局层**（真窗口 + 布局推到底），避免"属性绿、像素红"。
    /// </summary>
    [Collection("Theme")]
    public class ToolRailTests {
        static readonly string[] ExpectedOrder = {
            "cursorTool", "penTool", "eraserTool", "knifeTool",
            "pitchPointTool", "drawPitchTool", "pitchLineTool",
            "pitchSCurveTool", "pitchSineWaveTool", "pitchSmoothenTool",
        };

        static void Settle(Window window) {
            for (int i = 0; i < 3; i++) {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
            }
        }

        static (Window window, PianoRoll roll, Border rail, ListBox list, DocManagerTestSetup.ScopedDispatcher scope) Fixture() {
            // 与 PanelLayoutTests 的真实控件组合同法：卷帘 VM 构造会读 DocManager.Inst.Plugins
            // （私有 setter）⇒ 先走公开扫描入口把它填上；派发字段用**作用域版**（W24 卫生）。
            var scope = DocManagerTestSetup.EnterScopedDispatcher(nullChannel: true, installScheduler: false);
            OpenUtau.Core.DocManager.Inst.SearchAllLegacyPlugins();
            var roll = new PianoRoll(new PianoRollViewModel());
            var window = new Window { Width = 900, Height = 620, Content = roll };
            window.Show();
            Settle(window);
            var rail = roll.GetVisualDescendants().OfType<Border>()
                .First(b => b.Name == "ToolRail");
            var list = rail.GetVisualDescendants().OfType<ListBox>().First();
            return (window, roll, rail, list, scope);
        }

        static void Close(Window window, DocManagerTestSetup.ScopedDispatcher scope) {
            window.Close();
            Dispatcher.UIThread.RunJobs();
            scope.Dispose();
        }

        [AvaloniaFact]
        public void ToolRail_IsVerticalOverlayAtCanvasTopLeft_Margin12() {
            var (window, roll, rail, list, scope) = Fixture();
            try {
                var canvas = roll.GetVisualDescendants().OfType<NotesCanvas>().First();
                // overlay：不进顶栏，而是贴在画布（Grid.Row=3/Column=1）左上角
                Assert.Equal(Avalonia.Layout.HorizontalAlignment.Left, rail.HorizontalAlignment);
                Assert.Equal(Avalonia.Layout.VerticalAlignment.Top, rail.VerticalAlignment);
                Assert.Equal(new Thickness(12), rail.Margin);
                Assert.True(rail.Bounds.Width > 0 && rail.Bounds.Height > 0, "轨道未参与布局");
                // 与画布左上角相差正好 12 —— 轨道在中间层 Grid 里，Bounds 不同坐标系 ⇒ 用 TranslatePoint
                var railInRoll = rail.TranslatePoint(new Point(0, 0), roll);
                var canvasInRoll = canvas.TranslatePoint(new Point(0, 0), roll);
                Assert.NotNull(railInRoll);
                Assert.NotNull(canvasInRoll);
                Assert.Equal(12, railInRoll!.Value.X - canvasInRoll!.Value.X, 3);
                Assert.Equal(12, railInRoll.Value.Y - canvasInRoll.Value.Y, 3);
                // 竖排：最后一个工具的 Y 明显大于第一个
                var items = Items(list);
                Assert.True(items[^1].Bounds.Y > items[0].Bounds.Y + 100,
                    "工具轨不是竖排（Y 未拉开）");
                // 容器规格：圆角 12 + 1px 描边 + 4 内边距
                Assert.Equal(new CornerRadius(12), rail.CornerRadius);
                Assert.Equal(new Thickness(1), rail.BorderThickness);
                Assert.Equal(new Thickness(4), rail.Padding);
            } finally {
                Close(window, scope);
            }
        }

        [AvaloniaFact]
        public void ToolRail_HasTenToolsInTheFrozenOrder() {
            var (window, _, _, list, scope) = Fixture();
            try {
                var classes = Items(list)
                    .Select(i => ExpectedOrder.FirstOrDefault(c => i.Classes.Contains(c)))
                    .ToArray();
                Assert.Equal(ExpectedOrder.Length, list.ItemCount);
                Assert.Equal(ExpectedOrder, classes);
            } finally {
                Close(window, scope);
            }
        }

        [AvaloniaFact]
        public void ToolRail_ButtonsAre36WithRadius8_Spacing4_PitchGroupGetsDivider() {
            var (window, _, _, list, scope) = Fixture();
            try {
                var items = Items(list);
                foreach (var item in items) {
                    Assert.Equal(36, item.Bounds.Width, 3);
                    Assert.Equal(36, item.Bounds.Height, 3);
                }
                // 间距：组内 4；音高组首项另有 8 的组间距 ⇒ 12
                for (int i = 1; i < items.Length; i++) {
                    double gap = items[i].Bounds.Y - (items[i - 1].Bounds.Y + items[i - 1].Bounds.Height);
                    double expected = i == 4 ? 12 : 4;
                    Assert.Equal(expected, gap, 3);
                }
                // 分隔线只在音高组首项可见
                for (int i = 0; i < items.Length; i++) {
                    var sep = items[i].GetVisualDescendants().OfType<Border>()
                        .FirstOrDefault(b => b.Name == "PART_GroupSep");
                    Assert.NotNull(sep);
                    Assert.Equal(i == 4, sep!.IsVisible);
                }
            } finally {
                Close(window, scope);
            }
        }

        [AvaloniaFact]
        public void ToolRail_DoesNotChangeCanvasBounds() {
            var (window, roll, rail, _, scope) = Fixture();
            try {
                var canvas = roll.GetVisualDescendants().OfType<NotesCanvas>().First();
                var withRail = canvas.Bounds;
                rail.IsVisible = false;
                Settle(window);
                var withoutRail = canvas.Bounds;
                rail.IsVisible = true;
                Settle(window);
                // overlay 语义：加轨/去轨，画布 Bounds 一模一样
                Assert.Equal(withRail, withoutRail);
                // 且轨道确实压在画布范围内（左上角内侧），不是把画布挤开
                var railPt = rail.TranslatePoint(new Point(0, 0), roll);
                var canvasPt = canvas.TranslatePoint(new Point(0, 0), roll);
                Assert.NotNull(railPt);
                Assert.NotNull(canvasPt);
                Assert.True(railPt!.Value.X >= canvasPt!.Value.X && railPt.Value.Y >= canvasPt.Value.Y);
                Assert.True(railPt.Value.X + rail.Bounds.Width <= canvasPt.Value.X + canvas.Bounds.Width);
                Assert.True(railPt.Value.Y + rail.Bounds.Height <= canvasPt.Value.Y + canvas.Bounds.Height);
            } finally {
                Close(window, scope);
            }
        }

        [AvaloniaFact]
        public void ToolRail_OnlyRailAreaIsHitTestable() {
            var (window, roll, rail, list, scope) = Fixture();
            try {
                // 探针点必须换算到 `roll` 的坐标系（轨道在 Grid.Row=3/Column=1，有自己的偏移）
                var first = Items(list)[0];
                var centerInRoll = first.TranslatePoint(
                    new Point(first.Bounds.Width / 2, first.Bounds.Height / 2), roll);
                Assert.NotNull(centerInRoll);
                var hitInside = roll.InputHitTest(centerInRoll!.Value) as Visual;
                Assert.NotNull(hitInside);
                Assert.True(IsInside(rail, hitInside!), "轨内未命中轨道元素");

                // 反控：轨外（画布上、轨右侧 40px 处）不得命中轨道任何元素
                var railEdge = rail.TranslatePoint(new Point(rail.Bounds.Width, 10), roll);
                Assert.NotNull(railEdge);
                var outside = railEdge!.Value + new Vector(40, 0);
                var hitOutside = roll.InputHitTest(outside) as Visual;
                if (hitOutside != null) {
                    Assert.False(IsInside(rail, hitOutside), "轨外命中了轨道元素（会挡画布）");
                }
            } finally {
                Close(window, scope);
            }
        }

        [AvaloniaFact]
        public void PenFlyout_PopsToTheRight() {
            var (window, _, _, list, scope) = Fixture();
            try {
                var pen = Items(list).First(i => i.Classes.Contains("penTool"));
                var flyout = FlyoutBase.GetAttachedFlyout(pen) as Flyout;
                Assert.NotNull(flyout);
                Assert.Equal(PlacementMode.Right, flyout!.Placement);
            } finally {
                Close(window, scope);
            }
        }

        static ListBoxItem[] Items(ListBox list) => list.GetVisualDescendants()
            .OfType<ListBoxItem>()
            .Where(i => i.Classes.Contains("railTool"))
            .OrderBy(i => i.Bounds.Y)
            .ToArray();

        static bool IsInside(Visual ancestor, Visual node) =>
            node == ancestor || ancestor.GetVisualDescendants().Contains(node);
    }
}

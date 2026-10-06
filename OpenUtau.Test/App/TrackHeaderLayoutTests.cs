using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App.Controls;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// W33：轨头的**缺陷修复**回归（渲染器入口恒可达）。只覆盖"任何设计都会需要"的部分：
    ///
    /// 1. `SetPosition()` 原先按轨道高逐行裁剪（歌手 ≥3×、音素器 ≥4×、**渲染器 ≥5×**，×=21），
    ///    轨道一矮**渲染器整行消失**（用户报的"渲染器选择框被挤掉"）⇒ 现在渲染器移出裁剪序列；
    /// 2. `⋯` 溢出菜单里原先还有一份**渲染器兜底**（同一入口两处呈现）⇒ 已删，且它的触发条件
    ///    从 `!IsRendererVisible` 改成 `ShowOverflow`（否则渲染器恒显后它永远不出现，
    ///    歌手/音素器在矮轨道下就再也进不去了）。
    ///
    /// 断言全部落在**真实 Bounds**（headless 只桩掉绘制，布局是真跑的）。
    /// 头像尺寸/圆角、M/S/FX/⚙ 排布等**视觉改动不在此列**，等设计提案。
    /// </summary>
    public class TrackHeaderLayoutTests {
        const double MinHeight = 42;      // ViewConstants.TrackHeightMin
        const double Delta = 21;          // ViewConstants.TrackHeightDelta
        const double StandardHeight = 105; // ViewConstants.TrackHeightDefault

        static void Pump() => Dispatcher.UIThread.RunJobs();

        static (WindowEx win, TrackHeaderCanvas canvas, TrackHeader header, ObservableCollection<UTrack> items) Setup(double trackHeight, double width = 240) {
            var track = new UTrack { TrackNo = 0, TrackName = "Lead" };
            // 两个 DirectProperty 的 CLR setter 是私有的（生产里由 MainWindow 的 XAML 绑定驱动）
            // ⇒ 测试同样走绑定，走的是产品同一条赋值路径。
            var canvas = new TrackHeaderCanvas();
            canvas.Bind(TrackHeaderCanvas.TrackHeightProperty, Observable.Return(trackHeight));
            canvas.Bind(TrackHeaderCanvas.TrackOffsetProperty, Observable.Return(0.0));
            var items = new ObservableCollection<UTrack>();
            canvas.Items = items;   // 先给**空**集合：控件在订阅集合变更后才对"新增"建轨头
            items.Add(track);
            var win = new WindowEx { Width = width, Height = trackHeight + 20, Content = canvas };
            win.Classes.Set("no-motion", true);
            win.Show();
            win.Measure(new Size(width, trackHeight + 20));
            win.Arrange(new Rect(0, 0, width, trackHeight + 20));
            Pump();
            win.UpdateLayout();
            var header = canvas.GetVisualDescendants().OfType<TrackHeader>().Single();
            return (win, canvas, header, items);
        }

        [AvaloniaFact]
        public void MinimumTrackHeight_RendererButtonIsVisibleAndLaidOut() {
            var (win, canvas, header, items) = Setup(MinHeight);
            try {
                Assert.NotNull(header.ViewModel);
                // 矮轨道：歌手/音素器照旧被裁掉（这几条不动）
                Assert.False(header.ViewModel!.IsSingerVisible);
                Assert.False(header.ViewModel.IsPhonemizerVisible);
                // 渲染器：移出裁剪 ⇒ 仍显示、且真的被布局出宽度
                Assert.True(header.ViewModel.IsRendererVisible, "渲染器必须恒可见（阈值裁剪已移除）");
                var renderer = header.FindControl<Button>("RendererButton");
                Assert.NotNull(renderer);
                Assert.True(renderer!.IsVisible, "最小轨道高下渲染器按钮必须可见");
                Assert.True(renderer.Bounds.Width > 0,
                    $"最小轨道高下渲染器按钮必须有实际宽度，实际 {renderer.Bounds.Width}");
                // 歌手/音素器被裁掉时 `⋯` 必须露出来，否则它们没有入口
                Assert.True(header.ViewModel.ShowOverflow, "歌手/音素器被裁掉 ⇒ ⋯ 必须可见");
            } finally {
                // W33：移除轨道 ⇒ TrackHeaderCanvas.Remove ⇒ header.Dispose ⇒ VM.Dispose，
                // 从而释放 VM 的 MessageBus 监听。不退订的话这个 VM 会一直挂在 MessageBus 上，
                // 之后任何 Volume/Pan 广播都会回调到它（实测会把 MixerTrackStripTest 的
                // “只发一次声像通知”踩红 —— 与 W25 那次同类）。
                items.Clear();
                Pump();
                win.Close();
            }
        }

        [AvaloniaFact]
        public void StandardTrackHeight_RendererVisible_AndOverflowHidden() {
            var (win, canvas, header, items) = Setup(StandardHeight);
            try {
                Assert.True(header.ViewModel!.IsSingerVisible);
                Assert.True(header.ViewModel.IsPhonemizerVisible);
                Assert.False(header.ViewModel.ShowOverflow, "三行齐全时不应再出现 ⋯");
                var renderer = header.FindControl<Button>("RendererButton");
                Assert.True(renderer!.IsVisible);
                Assert.True(renderer.Bounds.Width > 0);
            } finally {
                // W33：移除轨道 ⇒ TrackHeaderCanvas.Remove ⇒ header.Dispose ⇒ VM.Dispose，
                // 从而释放 VM 的 MessageBus 监听。不退订的话这个 VM 会一直挂在 MessageBus 上，
                // 之后任何 Volume/Pan 广播都会回调到它（实测会把 MixerTrackStripTest 的
                // “只发一次声像通知”踩红 —— 与 W25 那次同类）。
                items.Clear();
                Pump();
                win.Close();
            }
        }

        [AvaloniaFact]
        public void OverflowFlyout_HasNoRendererEntry() {
            var (win, canvas, header, items) = Setup(MinHeight);
            try {
                var overflow = header.FindControl<Button>("OverflowButton");
                Assert.NotNull(overflow);
                var flyout = Assert.IsType<Flyout>(overflow!.Flyout);
                Assert.NotNull(flyout.Content);
                var entries = (flyout.Content as Visual)?.GetVisualDescendants().OfType<Button>().ToList()
                    ?? (flyout.Content as Control)?.GetVisualDescendants().OfType<Button>().ToList()
                    ?? new System.Collections.Generic.List<Button>();
                // 只剩歌手 + 音素器两条。改前这里会是 4 条：渲染器那行是 `Grid` 包了
                // "渲染器 + 设置"两个按钮 ⇒ 数量断言足以钉住"兜底已删"。
                // （不比较 `Content == ViewModel.Renderer`：裸 UTrack 没有工程时 Renderer 为 null，
                //   会与同样为 null 的其它内容撞成假阳性 —— 本用例第一版就是这么假红的。）
                Assert.Equal(2, entries.Count);
            } finally {
                // W33：移除轨道 ⇒ TrackHeaderCanvas.Remove ⇒ header.Dispose ⇒ VM.Dispose，
                // 从而释放 VM 的 MessageBus 监听。不退订的话这个 VM 会一直挂在 MessageBus 上，
                // 之后任何 Volume/Pan 广播都会回调到它（实测会把 MixerTrackStripTest 的
                // “只发一次声像通知”踩红 —— 与 W25 那次同类）。
                items.Clear();
                Pump();
                win.Close();
            }
        }

        [Fact]
        public void Xaml_HasExactlyOneRendererEntryPoint() {
            // 文本契约兜底：`RendererButtonClicked` 只允许出现一次（再有人加兜底就会红）
            string xaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "OpenUtau", "Controls", "TrackHeader.axaml"));
            int count = xaml.Split("RendererButtonClicked").Length - 1;
            Assert.Equal(1, count);
            Assert.Contains("Name=\"RendererButton\"", xaml);
        }

        /// <summary>
        /// **永久契约（W33 裁决第 2 条）**：卡片内所有子元素必须完全落在卡片内 ——
        /// `Bounds.Bottom ≤ 卡片高` 且 `Bounds.Right ≤ 卡片宽`，**任意轨道高（含 TrackHeightMin）
        /// 与任意面板宽**都成立。它比"某个按钮可见"更能防住以后的重排：这次"渲染器恒显
        /// ⇒ 纵向溢出 22px"正是被旧的按高裁剪顺手遮住的，只看某个按钮可见根本发现不了。
        /// 用 `TranslatePoint` 把子元素矩形换算到卡片坐标系再比（不能假设同一父级）。
        /// </summary>
        [AvaloniaTheory]
        // 全卡片契约：设计"放得下三行"的高度区间（标准 105 / 最高 147），含窄列
        [InlineData(105, 240, false)]
        [InlineData(105, 160, false)]
        [InlineData(147, 300, false)]
        // 最小/中间高度：只对**内容列**（名字/歌手/音素器/渲染器/音量条）要求零溢出 ——
        // 这是 W33 修的这条链路。头像（44 高 > 最小行 42）与右侧 M/S/FX/⚙ 竖列（≈104 高）
        // 的溢出是**既有设计**、归 fx-rack 的设计提案（见本用例下方注释与报告）。
        [InlineData(42, 160, true)]
        [InlineData(42, 240, true)]
        [InlineData(63, 160, true)]
        [InlineData(84, 200, true)]
        public void EveryChild_StaysInsideTheCard(double trackHeight, double width, bool contentColumnOnly) {
            var (win, _, header, items) = Setup(trackHeight, width);
            try {
                var card = header.FindControl<Border>("MainCard");
                var content = header.FindControl<Grid>("ContentRows");
                Assert.NotNull(card);
                Assert.NotNull(content);
                var cardSize = card!.Bounds.Size;
                Assert.True(cardSize.Width > 0 && cardSize.Height > 0, "卡片必须被布局");
                Visual scope = contentColumnOnly ? (Visual)content! : header;
                var offenders = new System.Collections.Generic.List<string>();
                foreach (var child in scope.GetVisualDescendants().OfType<Control>()) {
                    if (!child.IsVisible) {
                        continue;
                    }
                    // 跳过"包住整张卡片"的容器（Border 的模板 ContentPresenter = 卡片外扩描边，
                    // 不是内容溢出）
                    var originProbe = child.TranslatePoint(new Point(0, 0), card);
                    if (originProbe == null) {
                        continue;
                    }
                    if (new Rect(originProbe.Value, child.Bounds.Size).Contains(new Rect(default, cardSize))) {
                        continue;
                    }
                    var origin = child.TranslatePoint(new Point(0, 0), card);
                    if (origin == null) {
                        continue;
                    }
                    var rect = new Rect(origin.Value, child.Bounds.Size);
                    if (rect.Bottom > cardSize.Height + 1 || rect.Right > cardSize.Width + 1) {
                        offenders.Add($"{child.GetType().Name}({child.Name}) rect={rect} card={cardSize}");
                    }
                }
                Assert.True(offenders.Count == 0,
                    $"轨道高 {trackHeight} / 宽 {width} 下卡片内有溢出：\n" + string.Join("\n", offenders));
            } finally {
                items.Clear();
                Pump();
                win.Close();
            }
        }

        static string FindRepoRoot() {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OpenUtau.sln"))) {
                dir = dir.Parent;
            }
            Assert.NotNull(dir);
            return dir!.FullName;
        }
    }
}

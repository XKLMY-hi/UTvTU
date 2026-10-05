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

        static (WindowEx win, TrackHeaderCanvas canvas, TrackHeader header, ObservableCollection<UTrack> items) Setup(double trackHeight) {
            var track = new UTrack { TrackNo = 0, TrackName = "Lead" };
            // 两个 DirectProperty 的 CLR setter 是私有的（生产里由 MainWindow 的 XAML 绑定驱动）
            // ⇒ 测试同样走绑定，走的是产品同一条赋值路径。
            var canvas = new TrackHeaderCanvas();
            canvas.Bind(TrackHeaderCanvas.TrackHeightProperty, Observable.Return(trackHeight));
            canvas.Bind(TrackHeaderCanvas.TrackOffsetProperty, Observable.Return(0.0));
            var items = new ObservableCollection<UTrack>();
            canvas.Items = items;   // 先给**空**集合：控件在订阅集合变更后才对"新增"建轨头
            items.Add(track);
            var win = new WindowEx { Width = 240, Height = trackHeight + 20, Content = canvas };
            win.Classes.Set("no-motion", true);
            win.Show();
            win.Measure(new Size(240, trackHeight + 20));
            win.Arrange(new Rect(0, 0, 240, trackHeight + 20));
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.App;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// W16 面板系统契约测试。口径：**布局真跑**（headless 只桩掉绘制），因此一律断言真实
    /// `Bounds`（宽/位置/是否重叠），而不是属性值或像素。
    ///
    /// 覆盖四条已定口径：
    /// 1. 自身列不算保留宽（窄窗下拖 1px 不得吸附到 Min）；
    /// 2. 宿主尺寸变化后静默重新夹紧（不改写持久化的意图值，窗口变宽自动恢复）；
    /// 3. 放不下允许跌破 Min，且有效宽 &lt; 阈值 ⇒ 自动折叠（不留夹缝）；
    /// 4. `CenterMin` = 中央列**净**可用宽（分隔条自身 7px 恒占位）。
    /// 另覆盖：默认值、折叠无残留、拖拽夹紧、双击复位、持久化往返、1000×600 最小窗口、顶栏开关可达性。
    /// </summary>
    [Collection("Theme")]
    public class PanelLayoutTests {
        private const double TracksDefault = 248;
        private const double TracksMin = 200;
        private const double TracksMax = 420;
        private const double LibraryDefault = 272;
        private const double LibraryMin = 220;
        private const double LibraryMax = 480;
        private const double CenterMin = 320;

        private static void Pump() => Dispatcher.UIThread.RunJobs();

        private sealed class PanelFixture : IDisposable {
            public readonly PanelSlot Tracks = new("track-header", TracksDefault, TracksMin, TracksMax);
            public readonly PanelSlot Library = new("library", LibraryDefault, LibraryMin, LibraryMax);
            public readonly Border TracksHost = new();
            public readonly Border LibraryHost = new();
            public readonly Grid Center = new();
            public readonly PanelSplitter TracksSplitter;
            public readonly PanelSplitter LibrarySplitter;
            public readonly Grid Layout = new() { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto") };
            public readonly Window Window;

            public PanelFixture(double width, double height) {
                TracksSplitter = MakeSplitter(Tracks, panelColumn: 0, invert: false);   // 面板在分隔条左侧
                LibrarySplitter = MakeSplitter(Library, panelColumn: 4, invert: true);  // 面板在分隔条右侧
                BindHost(TracksHost, TracksSplitter);
                BindHost(LibraryHost, LibrarySplitter);

                Grid.SetColumn(TracksHost, 0);
                Grid.SetColumn(TracksSplitter, 1);
                Grid.SetColumn(Center, 2);
                Grid.SetColumn(LibrarySplitter, 3);
                Grid.SetColumn(LibraryHost, 4);
                Layout.Children.Add(TracksHost);
                Layout.Children.Add(TracksSplitter);
                Layout.Children.Add(Center);
                Layout.Children.Add(LibrarySplitter);
                Layout.Children.Add(LibraryHost);

                Window = new Window { Width = width, Height = height, Content = Layout };
                Window.Show();
                Pump();
                Window.UpdateLayout();
                Pump();
            }

            private static PanelSplitter MakeSplitter(PanelSlot slot, int panelColumn, bool invert) {
                var splitter = new PanelSplitter {
                    PanelColumn = panelColumn,
                    Invert = invert,
                    Min = slot.MinWidth,
                    Max = slot.MaxWidth,
                    DefaultWidth = slot.DefaultWidth,
                    CenterMin = CenterMin,
                };
                // 与产品 XAML 同构：Target ←→ PanelSlot.Width 双向
                splitter.Bind(PanelSplitter.TargetProperty,
                    new Binding(nameof(PanelSlot.Width)) { Source = slot, Mode = BindingMode.TwoWay });
                // 与产品 XAML 同构：面板折叠 ⇒ 分隔条隐藏（不留 7px 空档）
                splitter.Bind(Visual.IsVisibleProperty, new Binding("!IsCollapsed") { Source = slot });
                return splitter;
            }

            private static void BindHost(Border host, PanelSplitter splitter) {
                host.Bind(Layoutable.WidthProperty, new Binding(nameof(PanelSplitter.PanelWidth)) { Source = splitter });
                host.Bind(Visual.IsVisibleProperty, new Binding(nameof(PanelSplitter.PanelShown)) { Source = splitter });
            }

            public void Resize(double width, double height) {
                Window.Width = width;
                Window.Height = height;
                Pump();
                Window.UpdateLayout();
                Pump();
            }

            public void Dispose() => Window.Close();
        }

        // ── 1. 默认值 / 持久化往返 ──────────────────────────────────────────

        [AvaloniaFact]
        public void Defaults_AreExpandedAndSlimmerThanDesign() {
            var prefs = new Preferences.PanelLayoutPreferences();
            Assert.Equal(TracksDefault, prefs.TrackHeaderWidth);
            Assert.Equal(LibraryDefault, prefs.LibraryWidth);
            Assert.False(prefs.TrackHeaderCollapsed, "默认必须展开：素材库是插件浏览器入口（D9 主路径）");
            Assert.False(prefs.LibraryCollapsed, "默认必须展开");

            var tracks = new PanelSlot("t", TracksDefault, TracksMin, TracksMax);
            var library = new PanelSlot("l", LibraryDefault, LibraryMin, LibraryMax);
            Assert.Equal(TracksDefault, tracks.EffectiveWidth);
            Assert.Equal(LibraryDefault, library.EffectiveWidth);
        }

        [AvaloniaFact]
        public void Persistence_RoundTripsThroughPreferences() {
            var prefs = new Preferences.PanelLayoutPreferences();
            var tracks = new PanelSlot("t", TracksDefault, TracksMin, TracksMax) { Width = 300, IsCollapsed = true };
            var library = new PanelSlot("l", LibraryDefault, LibraryMin, LibraryMax) { Width = 400, IsCollapsed = false };

            MainWindowViewModel.SavePanelLayout(prefs, tracks, library);
            Assert.Equal(300, prefs.TrackHeaderWidth);
            Assert.True(prefs.TrackHeaderCollapsed);
            Assert.Equal(400, prefs.LibraryWidth);
            Assert.False(prefs.LibraryCollapsed);

            var tracks2 = new PanelSlot("t", TracksDefault, TracksMin, TracksMax);
            var library2 = new PanelSlot("l", LibraryDefault, LibraryMin, LibraryMax);
            MainWindowViewModel.LoadPanelLayout(prefs, tracks2, library2);
            Assert.Equal(300, tracks2.Width);
            Assert.True(tracks2.IsCollapsed);
            Assert.Equal(400, library2.Width);
            Assert.False(library2.IsCollapsed);
            Assert.Equal(0, tracks2.EffectiveWidth);   // 折叠 ⇒ 有效宽 0（不留空白）
        }

        [AvaloniaFact]
        public void ResetPanelLayout_RestoresDefaultsAndExpands() {
            var tracks = new PanelSlot("t", TracksDefault, TracksMin, TracksMax) { Width = 400, IsCollapsed = true };
            var library = new PanelSlot("l", LibraryDefault, LibraryMin, LibraryMax) { Width = 220, IsCollapsed = true };
            tracks.Reset();
            library.Reset();
            Assert.Equal(TracksDefault, tracks.Width);
            Assert.Equal(LibraryDefault, library.Width);
            Assert.False(tracks.IsCollapsed);
            Assert.False(library.IsCollapsed);
        }

        // ── 2. 布局层：宽度来自绑定 + 折叠无残留 ─────────────────────────────

        [AvaloniaFact]
        public void Panels_UseBoundWidths_AndCollapseLeavesNoResidual() {
            using var f = new PanelFixture(1440, 800);

            // 展开：面板真实宽 = 默认值，中央列在它右侧
            Assert.Equal(TracksDefault, f.TracksHost.Bounds.Width, 1);
            Assert.Equal(LibraryDefault, f.LibraryHost.Bounds.Width, 1);
            Assert.Equal(f.TracksHost.Bounds.Right + 7, f.Center.Bounds.X, 1);      // 左侧分隔条 7px
            Assert.Equal(f.Center.Bounds.Right + 7, f.LibraryHost.Bounds.X, 1);      // 右侧分隔条 7px
            Assert.True(f.Center.Bounds.Width >= CenterMin, $"中央列应 ≥ {CenterMin}，实际 {f.Center.Bounds.Width}");

            // 改意图值 ⇒ 布局跟着变（证明宽度真的来自绑定）
            f.Library.Width = 320;
            Pump();
            f.Window.UpdateLayout();
            Pump();
            Assert.Equal(320, f.LibraryHost.Bounds.Width, 1);

            // 折叠：面板隐藏 + 有效宽 0 + 中央列吃掉整行（隐藏控件的 Bounds 会保留上次排布值，
            // 所以"无残留"要看驱动值 PanelWidth 与邻居的占位，而不是隐藏元素的 Bounds）
            f.Library.IsCollapsed = true;
            Pump();
            f.Window.UpdateLayout();
            Pump();
            Assert.False(f.LibraryHost.IsVisible);
            Assert.False(f.LibrarySplitter.IsVisible);
            Assert.Equal(0, f.LibrarySplitter.PanelWidth, 1);
            Assert.Equal(1440, f.Center.Bounds.Right, 1);

            // 折叠轨头列同理（左侧不留空白：中央列左边界 = 0）
            f.Tracks.IsCollapsed = true;
            Pump();
            f.Window.UpdateLayout();
            Pump();
            Assert.False(f.TracksHost.IsVisible);
            Assert.Equal(0, f.TracksSplitter.PanelWidth, 1);
            Assert.Equal(0, f.Center.Bounds.X, 1);
        }

        // ── 3. 拖拽：夹紧 + 双击复位 + 窄窗不吸附（口径 1）──────────────────

        [AvaloniaFact]
        public void Drag_ClampsToMinAndMax_AndDoubleClickResets() {
            using var f = new PanelFixture(1440, 800);

            f.LibrarySplitter.ApplyDragDelta(-9999);
            Assert.Equal(LibraryMax, f.Library.Width, 1);      // 上限（Invert=true：向左拖才是变宽）
            f.LibrarySplitter.ApplyDragDelta(9999);
            Assert.Equal(LibraryMin, f.Library.Width, 1);      // 下限

            f.LibrarySplitter.ResetToDefault();
            Assert.Equal(LibraryDefault, f.Library.Width, 1);  // 双击复位

            // 轨头列在分隔条**左侧**（Invert=false）⇒ 向右拖 = 变宽
            f.TracksSplitter.ApplyDragDelta(60);
            Assert.Equal(TracksDefault + 60, f.Tracks.Width, 1);
            f.TracksSplitter.ResetToDefault();
            Assert.Equal(TracksDefault, f.Tracks.Width, 1);
        }

        [AvaloniaFact]
        public void NarrowWindow_DragOnePixel_DoesNotSnapToMin() {
            // 口径 1 回归：900 宽下"面板自身宽度"不得参与保留宽计算，否则拖 1px 会被兜到 Min
            foreach (double width in new[] { 900.0, 720.0 }) {
                using var f = new PanelFixture(width, 700);
                double before = f.Library.Width;
                Assert.True(f.LibrarySplitter.Bounds.Width > 0);

                f.LibrarySplitter.ApplyDragDelta(1);
                Assert.True(f.Library.Width >= LibraryMin,
                    $"{width} 宽下拖 1px 不应跌破 Min（现在是 {f.Library.Width}）");
                Assert.True(f.Library.Width <= before + 1.001,
                    $"{width} 宽下拖 1px 的增幅应 ≤ 1px（现在 {before} → {f.Library.Width}）");
                if (width >= 900) {
                    // 900 宽下两面板 + 中央区放得下 ⇒ 必须是"默认值 + 1"，绝不能跳到 Min
                    Assert.True(f.Library.Width > LibraryMin,
                        $"{width} 宽下放得下，不应吸附到 Min（现在是 {f.Library.Width}）");
                }
            }
        }

        // ── 4. 口径 2：宿主尺寸变化后静默夹紧（不改写意图值）────────────────

        [AvaloniaFact]
        public void HostShrink_ReclampsSilently_AndRestoresOnGrow() {
            using var f = new PanelFixture(1600, 800);

            f.Library.Width = LibraryMax;   // 用户在宽窗里拖到最大并（假定）持久化
            Pump();
            f.Window.UpdateLayout();
            Pump();
            Assert.Equal(LibraryMax, f.LibraryHost.Bounds.Width, 1);

            f.Resize(720, 700);
            Assert.True(f.LibrarySplitter.PanelWidth < LibraryMax,
                $"窄宿主下有效宽应被夹紧，实际 {f.LibrarySplitter.PanelWidth}");
            Assert.Equal(LibraryMax, f.Library.Width, 1);     // 意图值不被改写（持久化仍是 480）
            Assert.Equal(f.LibrarySplitter.PanelWidth, f.LibraryHost.Bounds.Width, 1);
            Assert.True(f.Center.Bounds.Width >= 250,
                $"夹紧后中央列应 ≥ 250，实际 {f.Center.Bounds.Width}");

            f.Resize(1600, 800);
            Assert.Equal(LibraryMax, f.LibrarySplitter.PanelWidth, 1);   // 变宽后自动回到用户值
            Assert.Equal(LibraryMax, f.LibraryHost.Bounds.Width, 1);
        }

        [AvaloniaFact]
        public void CenterMin_MeansNetWidth_SplitterPixelsIncluded() {
            // 口径 4：CenterMin 是中央列净宽 ⇒ 中央列 Bounds.Width 不得低于 CenterMin（在放得下时）
            using var f = new PanelFixture(1200, 700);
            f.Library.Width = LibraryMax;
            Pump();
            f.Window.UpdateLayout();
            Pump();
            Assert.True(f.Center.Bounds.Width >= CenterMin,
                $"中央列净宽应 ≥ CenterMin={CenterMin}，实际 {f.Center.Bounds.Width}");
        }

        // ── 5. 口径 3：放不下 ⇒ 允许跌破 Min 且 <120px 自动折叠 ──────────────

        [AvaloniaFact]
        public void TooNarrow_AutoCollapsesInsteadOfLeavingACrack() {
            using var f = new PanelFixture(700, 600);
            f.Library.Width = LibraryMax;
            f.Tracks.Width = TracksMax;
            Pump();
            f.Window.UpdateLayout();
            Pump();

            // "放不下"的解法有两类：跌破 Min 缩到可用宽，或直接折叠；二者都不得留夹缝。
            // 本场景（700 宽 + 两面板都要最大）实际解法是"轨头先自动折叠、素材库拿到剩余宽"。
            bool resolved = f.LibrarySplitter.PanelWidth < LibraryMin
                || !f.LibrarySplitter.PanelShown
                || !f.TracksSplitter.PanelShown
                || f.TracksSplitter.PanelWidth < TracksMin;
            Assert.True(resolved,
                $"放不下时应跌破 Min 或折叠，实际 library={f.LibrarySplitter.PanelWidth}/shown={f.LibrarySplitter.PanelShown}、" +
                $"tracks={f.TracksSplitter.PanelWidth}/shown={f.TracksSplitter.PanelShown}");

            // 不留夹缝：未显示的面板有效宽必须为 0（隐藏控件的 Bounds 会留上次排布值，故看驱动值）
            foreach (var (splitter, host, name) in new[] {
                (f.LibrarySplitter, f.LibraryHost, "library"),
                (f.TracksSplitter, f.TracksHost, "tracks") }) {
                if (!splitter.PanelShown) {
                    Assert.Equal(0, splitter.PanelWidth, 1);
                    Assert.False(host.IsVisible);
                }
            }
            Assert.True(f.Center.Bounds.Width >= 250, $"中央区只剩 {f.Center.Bounds.Width}");
        }

        // ── 6. 三档宽度：不重叠 + （中央区 ≥250 或面板已折叠）────────────────

        [AvaloniaTheory]
        [InlineData(300.0)]
        [InlineData(400.0)]
        [InlineData(720.0)]
        [InlineData(1000.0)]
        [InlineData(1440.0)]
        public void VariousWidths_DoNotOverlap_AndKeepCenterOrCollapse(double width) {
            using var f = new PanelFixture(width, 600);
            f.Library.Width = LibraryMax;    // 最坏情况：面板意图值取最大
            f.Tracks.Width = TracksMax;
            Pump();
            f.Window.UpdateLayout();
            Pump();

            // 不重叠：把可见的列按 X 排序，后一个的左边界不得小于前一个的右边界
            var visible = new List<Control>();
            if (f.TracksHost.IsVisible) visible.Add(f.TracksHost);
            if (f.TracksSplitter.Bounds.Width > 0 && f.TracksSplitter.IsVisible) visible.Add(f.TracksSplitter);
            visible.Add(f.Center);
            if (f.LibrarySplitter.Bounds.Width > 0 && f.LibrarySplitter.IsVisible) visible.Add(f.LibrarySplitter);
            if (f.LibraryHost.IsVisible) visible.Add(f.LibraryHost);
            visible.Sort((a, b) => a.Bounds.X.CompareTo(b.Bounds.X));
            for (int i = 1; i < visible.Count; i++) {
                Assert.True(visible[i].Bounds.X >= visible[i - 1].Bounds.Right - 0.5,
                    $"{width} 宽下 {visible[i - 1].Name ?? visible[i - 1].GetType().Name} 与 " +
                    $"{visible[i].Name ?? visible[i].GetType().Name} 重叠：" +
                    $"{visible[i - 1].Bounds} / {visible[i].Bounds}");
            }

            // 中央区要么够用，要么面板已折叠
            bool centerOk = f.Center.Bounds.Width >= 250;
            bool collapsed = !f.TracksHost.IsVisible || !f.LibraryHost.IsVisible;
            Assert.True(centerOk || collapsed,
                $"{width} 宽下中央区只剩 {f.Center.Bounds.Width} 且面板没折叠");

            // 任何情况下都不得超出窗口
            Assert.True(visible[^1].Bounds.Right <= width + 0.5, $"{width} 宽下内容溢出了窗口");
        }

        // ── 7. 顶栏开关始终可达（含混音台视图）──────────────────────────────

        [AvaloniaFact]
        public void TopBarLayoutMenu_IsReachable_InAllWorkViews() {
            // 折叠入口必须在顶栏「布局」flyout 里（面板折叠后仍找得回来）
            string xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Views", "MainWindow.axaml"));
            foreach (string key in new[] { "panel.toggle.tracks", "panel.toggle.library", "panel.reset" }) {
                Assert.Contains($"{{DynamicResource {key}}}", xaml);
            }
            Assert.Contains("ToggleType=\"CheckBox\"", xaml);
            Assert.Contains("x:Name=\"TopRightCluster\"", xaml);
            // 该 flyout 在右簇里 ⇒ 三个工作视图的 chrome 都要显示右簇（含混音台）
            foreach (var surface in new[] { AppSurface.Workspace, AppSurface.PianoRoll, AppSurface.Mixer }) {
                Assert.True(ViewSwitcherPolicy.ChromeFor(surface).ShowRightCluster,
                    $"{surface} 视图必须显示右簇，否则折叠后无法从顶栏找回来");
            }
        }

        // ── 8. 与既有契约不冲突（结构 + 样式）───────────────────────────────

        [AvaloniaFact]
        public void MainWindow_Xaml_WiresThePanelSystem() {
            string xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Views", "MainWindow.axaml"));
            // 五列骨架：面板/分隔条/中央/分隔条/面板
            Assert.Contains("x:Name=\"MainLayout\" ColumnDefinitions=\"Auto,Auto,*,Auto,Auto\"", xaml);
            // 面板宽/显隐取自分隔条的有效值（意图值与有效值分离，口径 2）
            Assert.Contains("Width=\"{Binding #TracksPanelSplitter.PanelWidth}\"", xaml);
            Assert.Contains("IsVisible=\"{Binding #TracksPanelSplitter.PanelShown}\"", xaml);
            Assert.Contains("Width=\"{Binding #LibraryPanelSplitter.PanelWidth}\"", xaml);
            Assert.Contains("IsVisible=\"{Binding #LibraryPanelSplitter.PanelShown}\"", xaml);
            // 分隔条参数：PanelColumn 必填、CenterMin、双击复位由控件负责
            Assert.Contains("PanelColumn=\"0\"", xaml);
            Assert.Contains("PanelColumn=\"4\"", xaml);
            Assert.Contains("CenterMin=\"320\"", xaml);
            Assert.Contains("DragCompleted=\"OnPanelSplitterDragCompleted\"", xaml);
            // 折叠入口：面板头部 chevron
            Assert.Contains("OnCollapseTracksPanel", xaml);
            Assert.Contains("OnCollapseLibraryPanel", xaml);
            Assert.Contains("Classes=\"panelToggle\"", xaml);
            // 视图宿主跟着列数走：卷帘铺 5 列、混音台让出素材库列（D9）铺 4 列
            Assert.Contains("Grid.ColumnSpan=\"5\"", xaml);
            Assert.Contains("Grid.Column=\"0\" Grid.ColumnSpan=\"4\"", xaml);
            // 最小窗口抬到可布局的下限
            Assert.Contains("MinWidth=\"800\"", xaml);
        }

        [AvaloniaFact]
        public void Splitter_UsesMd3Language_AndWideHitArea() {
            string xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Controls", "PanelSplitter.axaml"));
            // 细轨 1px + 命中区 7px
            Assert.Contains("<Setter Property=\"Width\" Value=\"1\"/>", xaml);
            Assert.Contains("Width=\"7\"", xaml);
            Assert.Contains("Cursor=\"SizeWestEast\"", xaml);
            // hover / 拖拽中走 md3.primary
            Assert.Contains("UserControl:pointerover Border.panelSplitterTrack", xaml);
            Assert.Contains("UserControl.dragging Border.panelSplitterTrack", xaml);
            Assert.Contains("{DynamicResource md3.primary}", xaml);
            Assert.Contains("{DynamicResource md3.outline-variant}", xaml);
        }

        // ══════════════════ 纵向模式（W20：PanelRow ≥ 0） ══════════════════
        // 与列模式**逐条对称**：意图值/有效值分离、保留尺寸按升序、放不下就折叠、
        // CenterMin = 中央行**净**高、双击复位、Invert 方向对称。
        // 列模式的 18 例一条不改，本组只覆盖纵向新增分支。

        private const double ExpDefault = 150;
        private const double ExpMin = 132;
        private const double ExpMax = 600;
        private const double CenterMinHeight = 120;
        private const double ExpCollapseThreshold = 80;
        private const double VerticalTopBar = 40;

        private sealed class VerticalFixture : IDisposable {
            public readonly PanelSlot Exp = new("pianoroll-exp", ExpDefault, ExpMin, ExpMax);
            public readonly PanelSlot Upper = new("upper", 160, 100, 400);
            public readonly Border ExpHost = new();
            public readonly Border UpperHost = new();
            public readonly Grid Center = new();
            public readonly PanelSplitter ExpSplitter;
            public readonly PanelSplitter? UpperSplitter;
            public readonly Grid Layout;
            public readonly Window Window;

            /// <param name="withUpper">再加一块**更靠上**的行面板（用于"升序保留"与无环回归）。</param>
            public VerticalFixture(double width, double height, bool withUpper = false) {
                // 行：0=顶部固定条 40；[1=上面板；2=上面板分隔条]；*=中央行；4=下面板分隔条；5=下面板
                Layout = withUpper
                    ? new Grid { RowDefinitions = new RowDefinitions("40,Auto,Auto,*,Auto,Auto") }
                    : new Grid { RowDefinitions = new RowDefinitions("40,Auto,*,Auto") };
                ExpSplitter = MakeRowSplitter(Exp, panelRow: withUpper ? 5 : 3, invert: true);
                BindRowHost(ExpHost, ExpSplitter);
                // 顶部固定条：必须是**真实存在的控件** —— 保留尺寸按子控件 Bounds 累加
                // （与列口径一致），只有 RowDefinitions 里的空行是量不到的。
                var topBar = new Border { Height = VerticalTopBar };
                Layout.Children.Add(topBar);
                Grid.SetRow(topBar, 0);
                Layout.Children.Add(ExpHost);
                Grid.SetRow(ExpHost, withUpper ? 5 : 3);
                Layout.Children.Add(ExpSplitter);
                Grid.SetRow(ExpSplitter, withUpper ? 4 : 1);
                Layout.Children.Add(Center);
                Grid.SetRow(Center, withUpper ? 3 : 2);
                Grid.SetRowSpan(Center, 1);
                if (withUpper) {
                    UpperSplitter = MakeRowSplitter(Upper, panelRow: 1, invert: false);
                    BindRowHost(UpperHost, UpperSplitter);
                    Layout.Children.Add(UpperHost);
                    Grid.SetRow(UpperHost, 1);
                    Layout.Children.Add(UpperSplitter);
                    Grid.SetRow(UpperSplitter, 2);
                }
                Window = new Window { Width = width, Height = height, Content = Layout };
                Window.Show();
                Settle();
            }

            private static PanelSplitter MakeRowSplitter(PanelSlot slot, int panelRow, bool invert) {
                var splitter = new PanelSplitter {
                    PanelRow = panelRow,
                    Invert = invert,
                    Min = slot.MinWidth,
                    Max = slot.MaxWidth,
                    DefaultWidth = slot.DefaultWidth,
                    CenterMin = CenterMinHeight,
                    CollapseThreshold = ExpCollapseThreshold,
                };
                // 与产品 XAML 同构：Target ←→ PanelSlot.Width 双向
                splitter.Bind(PanelSplitter.TargetProperty,
                    new Binding(nameof(PanelSlot.Width)) { Source = slot, Mode = BindingMode.TwoWay });
                return splitter;
            }

            private static void BindRowHost(Border host, PanelSplitter splitter) {
                host.Bind(Layoutable.HeightProperty, new Binding(nameof(PanelSplitter.PanelHeight)) { Source = splitter });
                host.Bind(Visual.IsVisibleProperty, new Binding(nameof(PanelSplitter.PanelShown)) { Source = splitter });
            }

            public void Resize(double width, double height) {
                Window.Width = width;
                Window.Height = height;
                Settle();
            }

            /// <summary>布局稳定：多跑几轮（两态振荡会在这里暴露成"值一直在变"或直接抛异常）。</summary>
            public void Settle() {
                for (int i = 0; i < 3; i++) {
                    Pump();
                    Window.UpdateLayout();
                }
                Pump();
            }

            public void Dispose() => Window.Close();
        }

        [AvaloniaFact]
        public void Vertical_DragChangesHeight_AndHostShrinkReclampsWithoutRewritingIntent() {
            using var f = new VerticalFixture(900, 700);
            Assert.Equal(ExpDefault, f.ExpSplitter.PanelHeight, 1);
            Assert.Equal(ExpDefault, f.ExpHost.Bounds.Height, 1);

            // Invert=true（面板在分隔条**下方**）⇒ 向下拖变矮、向上拖变高。
            // 向上 100 ⇒ 150 + 100 = 250（仍在 [Min 132, Max 600] 内，不会被 Min 兜住）
            f.ExpSplitter.ApplyDragDelta(-100);
            f.Settle();
            Assert.Equal(250, f.ExpSplitter.PanelHeight, 1);
            Assert.Equal(250, f.ExpHost.Bounds.Height, 1);
            Assert.Equal(250, f.Exp.Width, 1);            // 意图值同步（TwoWay）

            // 宿主变矮 ⇒ 静默重新夹紧有效高（300 − 7 − CenterMin120 = 173），但**不改写意图值**
            f.Resize(900, 300);
            Assert.True(f.ExpSplitter.PanelHeight < 250,
                $"矮宿主下有效高应被夹紧，实际 {f.ExpSplitter.PanelHeight}");
            Assert.Equal(250, f.Exp.Width, 1);            // 意图值仍是用户拖到的 250
            Assert.True(f.Center.Bounds.Height >= CenterMinHeight,
                $"中央行净高必须 ≥ CenterMin，实际 {f.Center.Bounds.Height}");

            // 宿主变高 ⇒ 自动回到意图值
            f.Resize(900, 700);
            Assert.Equal(250, f.ExpSplitter.PanelHeight, 1);
        }

        [AvaloniaFact]
        public void Vertical_CenterMin_IsNetHeight_SplitterPixelsIncluded() {
            using var f = new VerticalFixture(900, 480);
            double total = f.Layout.Bounds.Height;
            double pieces = VerticalTopBar + f.ExpSplitter.Bounds.Height + f.Center.Bounds.Height + f.ExpSplitter.PanelHeight;
            // 分隔条 7px 恒占位（不计入任何面板），中央行拿到的是**净**高
            Assert.Equal(7, f.ExpSplitter.Bounds.Height, 1);
            Assert.True(f.Center.Bounds.Height >= CenterMinHeight,
                $"中央行净高必须 ≥ CenterMin，实际 {f.Center.Bounds.Height}");
            Assert.Equal(total, pieces, 1);
        }

        [AvaloniaFact]
        public void Vertical_TooShort_AutoCollapsesInsteadOfLeavingACrack() {
            // 宿主高度小到"顶部条 + 分隔条 + CenterMin"之后只剩 < 阈值 ⇒ 面板折叠、高度 0
            using var f = new VerticalFixture(900, 200);
            Assert.False(f.ExpSplitter.PanelShown, "放不下时应折叠");
            Assert.Equal(0, f.ExpSplitter.PanelHeight, 1);
            Assert.False(f.ExpHost.IsVisible);
            // 不留夹缝：中央行拿走全部剩余高
            Assert.True(f.Center.Bounds.Height >= CenterMinHeight,
                $"折叠后中央行应拿到剩余空间，实际 {f.Center.Bounds.Height}");
            // 高度恢复后自动展开
            f.Resize(900, 700);
            Assert.True(f.ExpSplitter.PanelShown);
            Assert.Equal(ExpDefault, f.ExpSplitter.PanelHeight, 1);
        }

        [AvaloniaFact]
        public void Vertical_TwoRowPanels_ReserveAscending_NoLayoutLoop() {
            // 上面板（PanelRow=1）+ 下面板（PanelRow=5）：按行升序定优先级 ——
            // 更靠上的用"当前有效高"预留，更靠下的只用 Min 预留 ⇒ 依赖单向、无环。
            // 若写成互相按当前值夹紧，这里会抛 InvalidOperationException: Infinite layout loop detected。
            using var f = new VerticalFixture(900, 700, withUpper: true);
            Assert.NotNull(f.UpperSplitter);
            f.UpperSplitter!.ApplyDragDelta(40);       // 上面板加高（Invert=false：向下拖变高）
            f.ExpSplitter.ApplyDragDelta(20);          // 下面板变矮（Invert=true：向下拖变矮）
            f.Settle();

            double upper1 = f.UpperSplitter.PanelHeight;
            double lower1 = f.ExpSplitter.PanelHeight;
            Assert.True(upper1 > 160, $"上面板应被加高，实际 {upper1}");
            Assert.True(lower1 < ExpDefault + 0.5, $"下面板应变矮，实际 {lower1}");

            // 窄宿主：下面板被夹，上面板仍按当前有效高保留 ⇒ 连续几轮布局后值必须稳定
            f.Resize(900, 420);
            double upperA = f.UpperSplitter.PanelHeight;
            double lowerA = f.ExpSplitter.PanelHeight;
            f.Settle();
            f.Settle();
            Assert.Equal(upperA, f.UpperSplitter.PanelHeight, 1);
            Assert.Equal(lowerA, f.ExpSplitter.PanelHeight, 1);
            // 上面板优先于下面板（升序保留）：下面板先被夹到 Min 或折叠
            Assert.True(f.ExpSplitter.PanelHeight <= lower1 + 0.5,
                "下面板不应在窄宿主下反而变高");
            Assert.True(upperA >= f.Upper.MinWidth - 0.5, $"上面板应保留在其 Min 之上，实际 {upperA}");
        }

        [AvaloniaFact]
        public void Vertical_DoubleClickReset_ReturnsToDefaultHeight_AndReportsDragCompleted() {
            using var f = new VerticalFixture(900, 700);
            f.ExpSplitter.ApplyDragDelta(60);
            f.Settle();
            Assert.NotEqual(ExpDefault, f.ExpSplitter.PanelHeight, 1);

            int completed = 0;
            f.ExpSplitter.DragCompleted += (_, _) => completed++;
            f.ExpSplitter.ResetToDefault();
            f.Settle();
            Assert.Equal(ExpDefault, f.ExpSplitter.PanelHeight, 1);
            Assert.Equal(1, completed);
        }

        [AvaloniaFact]
        public void Vertical_InvertIsSymmetricToColumns() {
            // Invert=true：面板在分隔条下方 ⇒ 向上拖变高（Y 位移取负）；
            // Invert=false：面板在分隔条上方 ⇒ 向下拖变高。两者方向严格相反。
            using var f = new VerticalFixture(900, 700, withUpper: true);
            double before = f.ExpSplitter.PanelHeight;
            f.ExpSplitter.ApplyDragDelta(-30);
            f.Settle();
            Assert.Equal(before + 30, f.ExpSplitter.PanelHeight, 1);

            double upperBefore = f.UpperSplitter!.PanelHeight;
            f.UpperSplitter.ApplyDragDelta(30);
            f.Settle();
            Assert.Equal(upperBefore + 30, f.UpperSplitter.PanelHeight, 1);
        }

        [AvaloniaFact]
        public void Vertical_OrientationVisual_KeepsSevenPixelHitArea() {
            using var f = new VerticalFixture(900, 700);
            // 纵向：分隔条自身只占 7px **高**（横条），不再占据整列宽
            Assert.True(f.ExpSplitter.IsVertical);
            Assert.Equal(7, f.ExpSplitter.Bounds.Height, 1);
            Assert.True(f.ExpSplitter.Bounds.Width > 100, "纵向分隔条应铺满宿主宽（横条）");
            // 列模式不受影响
            var columnSplitter = new PanelSplitter { PanelColumn = 0 };
            Assert.False(columnSplitter.IsVertical);
        }

        // ══════════════ 卷帘表达式面板：真实控件接线（W20） ══════════════
        // 与上面的合成 fixture 不同，这一组直接 new 真实 `PianoRoll` + `PianoRollViewModel`，
        // 断言**真实 Bounds**：面板高 = 绑定值、折叠后为 0、恢复后回到意图值。

        [AvaloniaFact]
        public void PianoRoll_ExpPanel_RealControl_BoundsFollowPanelWiring() {
            OpenUtau.Test.TestSupport.DocManagerTestSetup.RunOnCurrentThread();
            // 卷帘 VM 构造会读 DocManager.Inst.Plugins（私有 setter）⇒ 用公开的扫描入口把它填上，
            // 否则 headless 下 Plugins 为 null、VM 构造直接抛 ArgumentNullException（既有可测性缺口）。
            OpenUtau.Core.DocManager.Inst.SearchAllLegacyPlugins();
            // 这两个用例会**真实落盘**（Preferences.Save），也会读持久化值 ⇒ 必须先把状态钉成
            // 已知起点，否则上一次失败跑留下的 "折叠=true" 会让下一次从折叠态开始（测试不是幂等的）。
            Preferences.Default.PanelLayout.PianoRollExpHeight = 150;
            Preferences.Default.PanelLayout.PianoRollExpCollapsed = false;
            Preferences.Default.ShowExpressions = true;
            Preferences.Save();            var project = new OpenUtau.Core.Ustx.UProject();
            project.timeAxis.BuildSegments(project);
            OpenUtau.Core.DocManager.Inst.ExecuteCmd(new OpenUtau.Core.LoadProjectNotification(project));

            var vm = new OpenUtau.App.ViewModels.PianoRollViewModel();
            var roll = new PianoRoll(vm);
            var window = new Window { Width = 1000, Height = 760, Content = roll };
            try {
                window.Show();
                SettleWindow(window);

                var splitter = roll.FindControl<PanelSplitter>("ExpPanelSplitter");
                Assert.NotNull(splitter);
                Assert.True(splitter!.IsVertical, "卷帘表达式面板必须是纵向模式（PanelRow ≥ 0）");
                Assert.Equal(5, splitter.PanelRow);
                Assert.True(splitter.Invert, "面板在分隔条下方 ⇒ Invert=true（向下拖变矮）");

                var canvases = roll.GetVisualDescendants().OfType<ExpressionCanvas>().ToList();
                Assert.NotEmpty(canvases);
                Assert.True(splitter.PanelShown, "默认应展开");
                Assert.Equal(150, splitter.PanelHeight, 1);       // DefaultWidth=150（与接入前一致）
                Assert.Equal(splitter.PanelHeight, canvases[0].Bounds.Height, 1);
                Assert.True(canvases[0].IsVisible);
                var notesCanvas = roll.GetVisualDescendants().OfType<NotesCanvas>().First();
                double centerBefore = notesCanvas.Bounds.Height;

                // 折叠：不显示 + 高 0（不留夹缝）
                roll.ExpPanel.ToggleCollapse();
                SettleWindow(window);
                Assert.False(splitter.PanelShown, "折叠后 PanelShown 必须为 false");
                Assert.Equal(0, splitter.PanelHeight, 1);
                Assert.False(canvases[0].IsVisible);
                // 不留夹缝：让出的高全给中央画布（面板 150 + 分隔条 7）。
                // **不能**断言 `splitter.Bounds.Height == 0`：隐藏元素的 Bounds 会保留上次排布值
                // （W16 配方专门记过这个坑），要看邻居怎么占位。
                Assert.Equal(centerBefore + 150 + 7, notesCanvas.Bounds.Height, 1);

                // 再展开：回到默认高（意图值未被折叠改写）
                roll.ExpPanel.ToggleCollapse();
                SettleWindow(window);
                Assert.True(splitter.PanelShown);
                Assert.Equal(150, splitter.PanelHeight, 1);
                Assert.Equal(splitter.PanelHeight, canvases[0].Bounds.Height, 1);
            } finally {
                Preferences.Default.PanelLayout.PianoRollExpHeight = 150;
                Preferences.Default.PanelLayout.PianoRollExpCollapsed = false;
                Preferences.Default.ShowExpressions = true;
                Preferences.Save();
                window.Close();
            }
        }

        [AvaloniaFact]
        public void PianoRoll_ExpPanel_SharesStateWithShowExpressions_AndPersists() {
            OpenUtau.Test.TestSupport.DocManagerTestSetup.RunOnCurrentThread();
            // 卷帘 VM 构造会读 DocManager.Inst.Plugins（私有 setter）⇒ 用公开的扫描入口把它填上，
            // 否则 headless 下 Plugins 为 null、VM 构造直接抛 ArgumentNullException（既有可测性缺口）。
            OpenUtau.Core.DocManager.Inst.SearchAllLegacyPlugins();
            // 这两个用例会**真实落盘**（Preferences.Save），也会读持久化值 ⇒ 必须先把状态钉成
            // 已知起点，否则上一次失败跑留下的 "折叠=true" 会让下一次从折叠态开始（测试不是幂等的）。
            Preferences.Default.PanelLayout.PianoRollExpHeight = 150;
            Preferences.Default.PanelLayout.PianoRollExpCollapsed = false;
            Preferences.Default.ShowExpressions = true;
            Preferences.Save();            var project = new OpenUtau.Core.Ustx.UProject();
            project.timeAxis.BuildSegments(project);
            OpenUtau.Core.DocManager.Inst.ExecuteCmd(new OpenUtau.Core.LoadProjectNotification(project));
            var vm = new OpenUtau.App.ViewModels.PianoRollViewModel();
            var roll = new PianoRoll(vm);
            var window = new Window { Width = 1000, Height = 760, Content = roll };
            bool oldShow = Preferences.Default.ShowExpressions;
            bool oldCollapsed = Preferences.Default.PanelLayout.PianoRollExpCollapsed;
            double oldHeight = Preferences.Default.PanelLayout.PianoRollExpHeight;
            try {
                window.Show();
                SettleWindow(window);
                // 折叠态与 ShowExpressions 是**同一状态**：改任一侧，另一侧跟随
                roll.ExpPanel.IsCollapsed = true;
                Assert.False(vm.NotesViewModel.ShowExpressions, "面板折叠 ⇒ 表达式视图关闭");
                vm.NotesViewModel.ShowExpressions = true;
                Assert.False(roll.ExpPanel.IsCollapsed, "表达式视图打开 ⇒ 面板展开");
                // 落盘：折叠态与高度都写进 PanelLayout（不新增平行存储）
                roll.ExpPanel.IsCollapsed = true;
                Assert.True(Preferences.Default.PanelLayout.PianoRollExpCollapsed);
                roll.ExpPanel.IsCollapsed = false;
                Assert.False(Preferences.Default.PanelLayout.PianoRollExpCollapsed);
                Assert.Equal(roll.ExpPanel.Width, Preferences.Default.PanelLayout.PianoRollExpHeight, 1);
            } finally {
                Preferences.Default.ShowExpressions = oldShow;
                Preferences.Default.PanelLayout.PianoRollExpCollapsed = oldCollapsed;
                Preferences.Default.PanelLayout.PianoRollExpHeight = oldHeight;
                Preferences.Save();   // 恢复也要落盘，否则污染后续跑
                window.Close();
            }
        }

        [Fact]
        public void PianoRoll_ExpPanel_XamlWiringMatchesRecipe() {
            // 文本契约：接线必须按配方（容器绑 PanelHeight/PanelShown、PanelRow 必填、
            // 旧的自绘 GridSplitter 不得残留、折叠入口必须在面板**外**才点得回来）
            string xaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "OpenUtau", "Controls", "PianoRoll.axaml"));
            Assert.Contains("c:PanelSplitter", xaml);
            Assert.Contains("x:Name=\"ExpPanelSplitter\"", xaml);
            Assert.Contains("PanelRow=\"5\"", xaml);
            Assert.Contains("Invert=\"True\"", xaml);
            Assert.Contains("CollapseThreshold=\"80\"", xaml);
            Assert.Contains("#ExpPanelSplitter.PanelHeight", xaml);
            Assert.Contains("#ExpPanelSplitter.PanelShown", xaml);
            Assert.Contains("OnExpPanelToggle", xaml);
            Assert.DoesNotContain("<GridSplitter", xaml);
            int toggleAt = xaml.IndexOf("Name=\"ExpPanelToggle\"", StringComparison.Ordinal);
            int panelAt = xaml.IndexOf("x:Name=\"ExpPanelSplitter\"", StringComparison.Ordinal);
            Assert.True(toggleAt > 0 && panelAt > 0 && toggleAt < panelAt,
                "折叠入口必须出现在面板之前（工具行），否则面板折叠后无法再打开");
        }

        private static void SettleWindow(Window window) {
            for (int i = 0; i < 3; i++) {
                Pump();
                window.UpdateLayout();
            }
            Pump();
        }

        private static string FindRepoRoot() {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OpenUtau.sln"))) {
                dir = dir.Parent;
            }
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        [AvaloniaFact]
        public void NewPanelStrings_ResolveInBothLanguages() {
            var en = new Dictionary<string, string>();
            string original = Preferences.Default.Language;
            try {
                OpenUtau.App.App.SetLanguage("en-US");
                foreach (string key in new[] { "panel.toggle.tracks", "panel.toggle.library", "panel.reset", "panel.collapse.tracks", "panel.collapse.library", "panel.drag.hint" }) {
                    Assert.True(ThemeManager.TryGetString(key, out string value), $"EN 缺键：{key}");
                    Assert.NotEqual(key, value);
                    en[key] = value;
                }
                OpenUtau.App.App.SetLanguage("zh-CN");
                foreach (string key in en.Keys) {
                    Assert.True(ThemeManager.TryGetString(key, out string value), $"zh-CN 缺键：{key}");
                    Assert.NotEqual(en[key], value);
                }
            } finally {
                OpenUtau.App.App.SetLanguage(string.IsNullOrWhiteSpace(original) ? "en-US" : original);
            }
        }
    }
}

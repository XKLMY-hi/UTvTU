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

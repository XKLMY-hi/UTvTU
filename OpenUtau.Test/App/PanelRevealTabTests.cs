using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// W34 折叠面板「快捷展开」边缘标签（设计稿 §8 候选 1）的布局层契约。
    ///
    /// 这里刻意**不 new MainWindow**（太重），而是搭一个与产品同构的最小宿主：
    /// 中央列（`*`）上铺一层无背景的 `Panel` overlay，里面放 `PanelRevealTab`，
    /// 面板列与分隔条列由真实 `PanelSlot` + `PanelSplitter` 驱动 —— 断言的是真实 `Bounds`。
    ///
    /// 三条要证的东西：
    /// ① 折叠时标签可见、尺寸 14×48、落在「折叠处」的角上；
    /// ② 标签**零布局占用**：有/无标签（折叠 vs 展开）时，分隔条列与中央列的 `Bounds` 一致；
    /// ③ 点击后回到**持久化宽度**（不是强制默认宽），且标签随即隐藏。
    /// </summary>
    public class PanelRevealTabTests {
        const double SplitterColumn = 7;
        static void Pump() => Dispatcher.UIThread.RunJobs();

        sealed class Fixture : IDisposable {
            public readonly PanelSlot Slot = new("track-header", 300, 200, 420);
            public readonly Border Host = new();
            public readonly PanelSplitter Splitter;
            public readonly PanelRevealTab Tab = new();
            public readonly Grid Overlay = new();
            public readonly Grid Layout = new() { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*") };
            public readonly Window Window;

            public Fixture() {
                Splitter = new PanelSplitter {
                    PanelColumn = 0,
                    Min = Slot.MinWidth,
                    Max = Slot.MaxWidth,
                    DefaultWidth = Slot.DefaultWidth,
                    CenterMin = 320,
                };
                Splitter.Bind(PanelSplitter.TargetProperty,
                    new Avalonia.Data.Binding(nameof(PanelSlot.Width)) { Source = Slot, Mode = Avalonia.Data.BindingMode.TwoWay });
                Splitter.Bind(Visual.IsVisibleProperty, new Avalonia.Data.Binding("!IsCollapsed") { Source = Slot });
                Host.Bind(Avalonia.Layout.Layoutable.WidthProperty, new Avalonia.Data.Binding(nameof(PanelSplitter.PanelWidth)) { Source = Splitter });
                Host.Bind(Visual.IsVisibleProperty, new Avalonia.Data.Binding(nameof(PanelSplitter.PanelShown)) { Source = Splitter });

                // overlay：铺在中央列上（无背景 ⇒ 自身不吃点击），标签贴左上角
                Tab.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
                Tab.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
                Tab.Margin = new Thickness(8);
                Tab.Bind(Visual.IsVisibleProperty, new Avalonia.Data.Binding("IsCollapsed") { Source = Slot });
                Overlay.Children.Add(Tab);

                Grid.SetColumn(Host, 0);
                Grid.SetColumn(Splitter, 1);
                Grid.SetColumn(Overlay, 2);
                Layout.Children.Add(Host);
                Layout.Children.Add(Splitter);
                Layout.Children.Add(Overlay);

                Window = new Window { Width = 900, Height = 300, Content = Layout };
                Window.Show();
                Settle();
            }

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
        public void Collapsed_TabIsVisibleAtFoldWithDeclaredSize() {
            using var f = new Fixture();
            f.Slot.IsCollapsed = true;   // 标签只在折叠时出现
            f.Settle();
            Assert.True(f.Tab.IsVisible, "面板折叠时标签必须可见（它就是折叠处的入口）");
            Assert.Equal(14, f.Tab.Bounds.Width, 1);
            Assert.Equal(48, f.Tab.Bounds.Height, 1);
            // 落在中央列左上角、距边 8（折叠处那一侧）
            Assert.Equal(8, f.Tab.Bounds.X, 1);
            Assert.Equal(8, f.Tab.Bounds.Y, 1);
        }

        [AvaloniaFact]
        public void Expanded_TabIsHidden_AndNeighboursAreUnchanged() {
            using var f = new Fixture();
            f.Settle();
            // 折叠态：面板列宽 0、分隔条列 7
            double splitterWidthCollapsed = f.Splitter.Bounds.Width;
            double centerWidthCollapsed = f.Overlay.Bounds.Width;

            f.Slot.IsCollapsed = false;
            f.Settle();
            Assert.False(f.Tab.IsVisible, "面板展开后标签必须隐藏");
            double splitterWidthExpanded = f.Splitter.Bounds.Width;
            double centerWidthExpanded = f.Overlay.Bounds.Width;

            // 零占用：标签出现/消失都不改分隔条列与中央列的几何
            Assert.Equal(SplitterColumn, splitterWidthCollapsed, 1);
            Assert.Equal(SplitterColumn, splitterWidthExpanded, 1);
            Assert.Equal(centerWidthCollapsed, centerWidthExpanded, 1);
        }

        [AvaloniaFact]
        public void ClickingTab_RestoresPersistedWidth_NotDefault() {
            using var f = new Fixture();
            f.Settle();
            // 用户把面板拖到 300（非默认 300？默认 DefaultWidth=300 同值 ⇒ 用 Slot.Width 设 360 区分）
            f.Slot.Width = 360;
            f.Settle();
            f.Slot.IsCollapsed = true;
            f.Settle();
            Assert.True(f.Tab.IsVisible);
            f.Slot.IsCollapsed = false;   // 标签点击在 MainWindow 里走的就是这一条（RevealTracksPanel）
            f.Settle();
            Assert.Equal(360, f.Splitter.PanelWidth, 1);   // 回到**持久化宽度**，不是 DefaultWidth
            Assert.False(f.Tab.IsVisible);
        }

        [AvaloniaFact]
        public void Tab_IsKeyboardReachable_AndClickRaisesExpandRequest() {
            using var f = new Fixture();
            f.Slot.IsCollapsed = true;
            f.Settle();
            var button = f.Tab.GetVisualDescendants().OfType<Button>().Single();
            Assert.True(button.Focusable, "标签必须能 Tab 聚焦（Enter/Space 激活由 Button 自带）");
            Assert.Equal(1.0, button.Bounds.Width / 14, 1);   // 常态仍是 14 宽
            int fired = 0;
            f.Tab.ExpandRequested += (_, _) => fired++;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, fired);                            // 点击确实抛出展开请求
        }

        /// <summary>
        /// ① 行方向（卷帘表达式区）的**横向变体**：48×14 胶囊（不是 14×48 竖条）、贴宿主**下缘**，
        /// 悬停/聚焦改成**高度**展开（向上生长）。位置由宿主 `VerticalAlignment=Bottom` 决定。
        /// </summary>
        [AvaloniaFact]
        public void HorizontalVariant_Is48x14_AndGrowsUpward() {
            var tab = new PanelRevealTab { AnchorBottom = true };
            var host = new Panel { Height = 200, Width = 200 };
            host.Children.Add(tab);
            tab.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            tab.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
            tab.Margin = new Thickness(8);
            var window = new Window { Width = 220, Height = 220, Content = host };
            try {
                window.Show();
                for (int i = 0; i < 3; i++) {
                    Pump();
                    window.UpdateLayout();
                }
                Assert.Equal(48, tab.Bounds.Width, 1);
                Assert.Equal(14, tab.Bounds.Height, 1);
                Assert.Equal(200 - 8, tab.Bounds.Bottom, 1);   // 贴下缘（Margin 8）
                string xaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "OpenUtau", "Controls", "PanelRevealTab.axaml"));
                Assert.Contains("Button.revealTab.horizontal:pointerover", xaml);
                int hoverBlock = xaml.IndexOf("Button.revealTab.horizontal:pointerover", StringComparison.Ordinal);
                Assert.Contains("Height", xaml.Substring(hoverBlock, 400));   // 展开的是高度 ⇒ 向上生长
            } finally {
                window.Close();
            }
        }

        /// <summary>
        /// ① 卷帘集成：表达式区（行方向）折叠时，**左下角**出现横向标签；点它 ⇒ 面板展开。
        /// 用真实 `PianoRoll` 宿主，断言真实 `Bounds`。
        /// </summary>
        [AvaloniaFact]
        public void PianoRoll_ExpPanelCollapsed_ShowsHorizontalTabAtBottomLeft() {
            OpenUtau.Test.TestSupport.DocManagerTestSetup.RunOnCurrentThread();
            OpenUtau.Core.DocManager.Inst.SearchAllLegacyPlugins();
            var project = new OpenUtau.Core.Ustx.UProject();
            project.timeAxis.BuildSegments(project);
            OpenUtau.Core.DocManager.Inst.ExecuteCmd(new OpenUtau.Core.LoadProjectNotification(project));
            var vm = new PianoRollViewModel();
            var roll = new PianoRoll(vm);
            var window = new Window { Width = 1000, Height = 760, Content = roll };
            bool oldCollapsed = Preferences.Default.PanelLayout.PianoRollExpCollapsed;
            try {
                roll.ExpPanel.IsCollapsed = true;
                window.Show();
                for (int i = 0; i < 3; i++) {
                    Pump();
                    window.UpdateLayout();
                }
                var tab = roll.FindControl<PanelRevealTab>("ExpRevealTab");
                Assert.NotNull(tab);
                Assert.True(tab!.IsVisible, "表达式区折叠时左下角标签必须可见");
                Assert.Equal(48, tab.Bounds.Width, 1);
                Assert.Equal(14, tab.Bounds.Height, 1);
                Assert.True(tab.Bounds.X < roll.Bounds.Width / 3, $"标签应在左侧，实际 X={tab.Bounds.X}");
                Assert.True(tab.Bounds.Bottom > roll.Bounds.Height * 0.5, $"标签应贴下缘，实际 Bottom={tab.Bounds.Bottom}");
                var button = tab.GetVisualDescendants().OfType<Button>().Single();
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump();
                Assert.False(roll.ExpPanel.IsCollapsed, "点击标签必须把表达式面板展开");
                Assert.False(tab.IsVisible, "展开后标签必须隐藏");
            } finally {
                Preferences.Default.PanelLayout.PianoRollExpCollapsed = oldCollapsed;
                roll.Dispose();
                window.Close();
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

        /// <summary>
        /// 真机 FAIL 的**防复发断言组**（W44 复验）：只断"`Bounds` 是 14×48"是不够的 ——
        /// 真机上它被不透明的中央区盖住 ⇒ 看不见、`InputHitTest` 也命不中。一次钉四件事：
        /// ① 标签**中心点**的命中测试落在标签子树内；② 容器背景画刷**非空**（不是只有命中区）；
        /// ③ 字形渲染尺寸 14×14；④ 聚焦即刻展开到 92×48 + `TabIndex=0`（键盘可达）。
        /// 宿主按**生产同形**搭：不透明中央区**先声明**、overlay **后声明**。
        /// </summary>
        [AvaloniaFact]
        public void Tab_IsHitTestable_HasChrome_AndExpandsOnFocus() {
            var slot = new PanelSlot("track-header", 300, 200, 420) { IsCollapsed = true };
            var opaqueCenter = new Border { Background = Avalonia.Media.Brushes.Black };   // 模拟不透明中央区
            var overlay = new Panel();
            var tab = new PanelRevealTab();
            tab.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            tab.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            tab.Margin = new Thickness(8);
            tab.Bind(Visual.IsVisibleProperty, new Avalonia.Data.Binding("IsCollapsed") { Source = slot });
            overlay.Children.Add(tab);
            var root = new Grid();
            root.Children.Add(opaqueCenter);   // 先声明 = 画在下面
            root.Children.Add(overlay);        // 后声明 = 画在上面（生产里的正确顺序）
            var window = new Window { Width = 400, Height = 300, Content = root };
            try {
                window.Show();
                for (int i = 0; i < 3; i++) {
                    Pump();
                    window.UpdateLayout();
                }
                var button = tab.GetVisualDescendants().OfType<Button>().Single();
                var chrome = tab.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("revealChrome"));
                var glyph = tab.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>()
                    .First(p => p.Classes.Contains("revealGlyph"));

                var center = tab.TranslatePoint(new Point(tab.Bounds.Width / 2, tab.Bounds.Height / 2), window);
                Assert.NotNull(center);
                var hit = window.InputHitTest(center!.Value);
                Assert.NotNull(hit);
                Assert.True(hit is Visual hitVisual && IsWithin(hitVisual, tab), $"标签中心点命中的是 {hit!.GetType().Name}，不是标签自身/子元素");
                Assert.NotNull(chrome.Background);
                // 字形尺寸：断**声明值**（样式给出的 14×14）而不是 Bounds —— 本主题下 `Path` 在
                // Panel 里 Arrange 后 Bounds 仍可能报 0（实测），而"字形偏小/半渲染"的回归
                // 是从样式取值这里进来的。容器 14 宽 ⇒ 14 已是上限（规格：18–20 字形放 36 容器）。
                Assert.Equal(14, glyph.Width, 1);
                Assert.Equal(14, glyph.Height, 1);
                Assert.True(button.Focusable);
                Assert.Equal(0, KeyboardNavigation.GetTabIndex(button));
                button.Focus();
                Pump();
                window.UpdateLayout();
                Assert.True(button.IsFocused, "标签按钮必须能拿到焦点（键盘路径的前提）");
                Assert.Equal(92, chrome.Bounds.Width, 1);          // 聚焦态 = 92×48
                Assert.Equal(48, chrome.Bounds.Height, 1);
            } finally {
                window.Close();
            }
        }

        static bool IsWithin(Visual candidate, Visual ancestor) {
            for (Visual? v = candidate; v != null; v = v.GetVisualParent()) {
                if (ReferenceEquals(v, ancestor)) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// **z 序契约**（真机 FAIL 的根因护栏）：MainWindow 里这段 overlay 必须声明在**中央区之后**
        /// —— z 序 = 声明顺序，中央区不透明 ⇒ 声明在它前面就会被整块盖住，也就命中不到。
        /// 当时缺的正是这一条：`Bounds` 断言全绿，真机却看不见。
        /// </summary>
        [Fact]
        public void MainWindow_DeclaresRevealOverlay_AfterTheOpaqueCenter() {
            string xaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "OpenUtau", "Views", "MainWindow.axaml"));
            int overlayAt = xaml.IndexOf("x:Name=\"TracksRevealTab\"", StringComparison.Ordinal);
            int centerAt = xaml.IndexOf("x:Name=\"ArrangementArea\"", StringComparison.Ordinal);
            Assert.True(overlayAt > 0, "找不到快捷展开标签");
            Assert.True(centerAt > 0, "找不到中央编排区");
            Assert.True(overlayAt > centerAt,
                "快捷展开 overlay 必须声明在中央区**之后**（否则被不透明中央区盖住：看不见也点不到）");
        }
    }
}

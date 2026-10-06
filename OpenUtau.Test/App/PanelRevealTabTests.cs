using System;
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
    }
}

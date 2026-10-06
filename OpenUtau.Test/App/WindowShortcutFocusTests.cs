using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App;
using OpenUtau.App.Commands;
using OpenUtau.App.Views;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// W47 窗口级快捷键 vs 焦点控件标准键语义。
    ///
    /// 背景（W44 真机反馈"Enter/Space 不展开"的根因面）：窗口用
    /// `AddHandler(KeyDownEvent, …, Tunnel|Bubble, handledEventsToo:true)` 收全部按键，
    /// 于是焦点控件的标准激活键/文本输入可能被窗口级命令抢先吞掉。
    ///
    /// 这里断言的是真实产品策略函数 <see cref="MainWindow.ShouldYieldShortcutToFocus"/>（可测的接缝）：
    /// ① 焦点在 Button 上：Space/Enter **让位**（激活交给控件），且注册表里 Space 仍是播放 ⇒ 窗口不该抢；
    /// ② 焦点在**画布**上：Space **不让位** ⇒ 窗口级命令照常（空格仍播放）；
    /// ③ 焦点在 TextBox 上：普通键（含 Space）**让位**（要能输入空格），但 Ctrl 组合**不让位**（全局键保留）；
    /// ④ 命令注册表契约：Ctrl+S / Ctrl+Shift+R / Ctrl+M / Ctrl+W / Space 的映射保持绿。
    /// </summary>
    public class WindowShortcutFocusTests {
        static void Pump() => Dispatcher.UIThread.RunJobs();

        /// <summary>搭最小宿主，拿到"真实控件 + 真实焦点"（不 new MainWindow —— 太重）。</summary>
        static (Window win, T control) Host<T>(T control) where T : Control {
            var win = new OpenUtau.App.Controls.WindowEx { Width = 300, Height = 200, Content = control };
            win.Show();
            Pump();
            control.Focus();
            Pump();
            return (win, control);
        }

        // ① 焦点在 Button 上 ⇒ Space / Enter 让位（激活交给控件自己）
        [AvaloniaFact]
        public void ButtonFocused_YieldsSpaceAndEnter() {
            var (win, button) = Host(new Button { Content = "播放" });
            try {
                Assert.Equal(button, win.FocusManager?.GetFocusedElement());
                Assert.True(MainWindow.ShouldYieldShortcutToFocus(win.FocusManager?.GetFocusedElement(), Key.Space, KeyModifiers.None));
                Assert.True(MainWindow.ShouldYieldShortcutToFocus(win.FocusManager?.GetFocusedElement(), Key.Enter, KeyModifiers.None));
                // 但命令手势不让位：Ctrl 组合仍走全局键
                Assert.False(MainWindow.ShouldYieldShortcutToFocus(win.FocusManager?.GetFocusedElement(), Key.S, KeyModifiers.Control));
                // 且 Space 在注册表里确实是"播放"⇒ 若不让位就会抢走激活语义（这正是缺陷面）
                Assert.Equal("playback.playpause", SpaceCommandId());
            } finally {
                win.Close();
            }
        }

        // 同族的 ToggleButton/CheckBox/RadioButton 与 MenuItem/ComboBox 同样让位（Button 基类覆盖前三者）
        [AvaloniaFact]
        public void ToggleLikeAndMenuControls_AlsoYieldSpace() {
            foreach (Control c in new Control[] {
                new ToggleButton { Content = "t" }, new CheckBox { Content = "c" },
                new RadioButton { Content = "r" }, new MenuItem { Header = "m" },
                new ComboBox { ItemsSource = new[] { "a" }, SelectedIndex = 0 },
            }) {
                var (win, control) = Host(c);
                try {
                    Assert.True(MainWindow.ShouldYieldShortcutToFocus(win.FocusManager?.GetFocusedElement(), Key.Space, KeyModifiers.None),
                        $"{c.GetType().Name} 上 Space 应让位");
                    Assert.True(MainWindow.ShouldYieldShortcutToFocus(win.FocusManager?.GetFocusedElement(), Key.Enter, KeyModifiers.None),
                        $"{c.GetType().Name} 上 Enter 应让位");
                } finally {
                    win.Close();
                }
            }
        }

        // ② 焦点在画布上 ⇒ 不让位（窗口级 Space=播放 照旧）
        [AvaloniaFact]
        public void CanvasFocused_DoesNotYield_SoSpaceStillPlays() {
            var (win, canvas) = Host(new Canvas { Focusable = true, Background = Avalonia.Media.Brushes.Transparent });
            try {
                Assert.Equal(canvas, win.FocusManager?.GetFocusedElement());
                Assert.False(MainWindow.ShouldYieldShortcutToFocus(win.FocusManager?.GetFocusedElement(), Key.Space, KeyModifiers.None));
                Assert.Equal("playback.playpause", SpaceCommandId());
            } finally {
                win.Close();
            }
        }

        // ③ 焦点在 TextBox 上 ⇒ 普通键（含 Space）让位（要能输入空格）；Ctrl 组合不让位（全局键保留）
        [AvaloniaFact]
        public void TextBoxFocused_YieldsTypingKeysButKeepsCtrlCombos() {
            var box = new TextBox();
            var (win, _) = Host(box);
            try {
                box.Focus();
                Pump();
                Assert.True(MainWindow.ShouldYieldShortcutToFocus(win.FocusManager?.GetFocusedElement(), Key.Space, KeyModifiers.None));
                Assert.True(MainWindow.ShouldYieldShortcutToFocus(win.FocusManager?.GetFocusedElement(), Key.A, KeyModifiers.None));
                Assert.False(MainWindow.ShouldYieldShortcutToFocus(win.FocusManager?.GetFocusedElement(), Key.S, KeyModifiers.Control));
                Assert.False(MainWindow.ShouldYieldShortcutToFocus(win.FocusManager?.GetFocusedElement(), Key.M, KeyModifiers.Control));
            } finally {
                win.Close();
            }
        }

        // ④ 命令注册表契约保持：全局键与"窗口级非全局键"的分工不变
        [AvaloniaFact]
        public void CommandRegistry_KeepsGlobalAndWindowShortcuts() {
            var preFocus = CommandRegistry.All.Where(c => c.PreFocus).Select(c => c.Id).OrderBy(x => x).ToArray();
            // W48：三条视图切换也必须在第一趟 —— 第二趟(:1642)位于「卷帘显示 + 卷帘持有焦点」守卫(:1636)之后，
            // 而那正是 Ctrl+1/Ctrl+3（从卷帘切回工作台/混音台）要生效的场景。
            Assert.Equal(new[] { "file.save", "tools.mixer", "tools.mixerattach",
                "view.mixer", "view.pianoroll", "view.workspace" }, preFocus);
            Assert.Equal("playback.playpause", SpaceCommandId());
            var all = CommandRegistry.All.Select(c => c.Id).ToArray();
            Assert.Contains("file.render", all);          // 导出仍在命令表内
            Assert.Contains("edit.undo", all);
        }

        static string? SpaceCommandId() =>
            CommandRegistry.All
                .Select(c => (c.Id, g: CommandRegistry.Resolve(c.Gesture, KeyModifiers.Control)))
                .FirstOrDefault(t => t.g?.Key == Key.Space).Id;
    }
}

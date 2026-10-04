using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using OpenUtau.App.Controls;
using OpenUtau.Core.Util;

namespace OpenUtau.App.Views {
    /// <summary>
    /// 钢琴卷帘分离窗口（S5/A2）：纯宿主 —— <see cref="PianoRoll"/> 的订阅与表达式附着
    /// 都随控件本身存活，故 reparent 期间窗口侧一律不做启停。
    /// 用户关窗 = 控件收回视图区（<see cref="ReturnToHost"/>）；宿主回收/退出走 <see cref="ReleaseControl"/>。
    /// </summary>
    public partial class PianoRollDetachedWindow : WindowEx {
        private readonly PianoRoll pianoRoll;
        private bool released;
        private bool closed;

        /// <summary>用户关闭分离窗口时的回调：宿主把控件放回视图区（控件继续存活）。</summary>
        public Action? ReturnToHost { get; set; }

        public PianoRollDetachedWindow(PianoRoll pianoRoll) {
            InitializeComponent();
            this.pianoRoll = pianoRoll;
            DataContext = pianoRoll.DataContext;

            PianoRollContainer.Content = pianoRoll;

            if (Preferences.Default.PianorollWindowSize.TryGetPosition(out int x, out int y)) {
                Position = new PixelPoint(x, y);
            }
            WindowState = (WindowState)Preferences.Default.PianorollWindowSize.State;
        }

        public void WindowGotFocus(object sender, FocusChangedEventArgs e) {
            if (e.Source is PianoRollDetachedWindow) {
                pianoRoll.Focus();
            }
        }

        public void WindowClosing(object? sender, WindowClosingEventArgs e) {
            Preferences.Default.PianorollWindowSize.Set(Width, Height, Position.X, Position.Y, (int)WindowState);
            Preferences.Save();
            // 不再取消关闭（旧实现是「隐藏」）：关窗即收回视图区，由 OnClosed 走 ReturnToHost
        }

        public void WindowDeactivated(object sender, EventArgs args) {
            pianoRoll.LyricBox?.EndEdit();
        }

        /// <summary>
        /// 把控件交回宿主：摘掉 Content → 关窗；不触发收回归位。
        /// 幂等（窗口已关时只清 Content），宿主回收与用户关窗两条路都安全。
        /// </summary>
        public void ReleaseControl() {
            released = true;
            PianoRollContainer.Content = null;
            if (!closed) {
                Close();
            }
        }

        /// <summary>
        /// 旧 <c>ForceClose()</c> 已删除：控件生命周期不再属于窗口（关窗只做归还/回收）。
        /// </summary>
        protected override void OnClosed(EventArgs e) {
            base.OnClosed(e);
            closed = true;
            if (released) {
                return;
            }
            var returnToHost = ReturnToHost;
            ReturnToHost = null;
            released = true;
            returnToHost?.Invoke();
        }
    }
}

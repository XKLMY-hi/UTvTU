using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenUtau.App.Controls;
using OpenUtau.App.Views;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// task-21 回归：**跨窗口 reparent 必须冲洗旧窗口的挂起布局**，
    /// 否则旧窗队列里残留的子树会在新窗口里被 `InvalidateArrange` ⇒
    /// `ArgumentException: Attempt to call InvalidateArrange on wrong LayoutManager`（未处理 → 程序退出）。
    ///
    /// 机制（Avalonia 12.1.0 `LayoutManager.cs` 源码 + 本仓实测）：
    /// `Content = null` 摘除子树时 Avalonia 会把整棵子树入队到**旧窗口**的 measure 队列并调度一次 pass
    /// （实测摘除瞬间 `_toMeasure=[PROBE,ContentPresenter,CHILD,…]`、`_queued=true`）；
    /// 若随后把子树挂到另一个窗口，旧窗那次 pass 的 `ExecuteArrangePass` 会走到
    /// `_toArrangeAfterMeasure → InvalidateArrange(control)`，此时 `control.GetLayoutRoot()` 已是新窗口
    /// ⇒ 抛异常。修法见 <see cref="MainWindow.DetachAndFlush"/>（摘树后立刻在旧窗口跑一次布局，
    /// 此刻子树没有新家，布局遍历判它 NotVisible 直接跳过）。
    ///
    /// 说明：这些用例用**真实 Window + 真实 LayoutManager**（headless 只桩掉渲染后台），
    /// 因此覆盖的就是崩溃所在的那条布局路径；Win32 消息循环本身不覆盖。
    /// </summary>
    [Collection("Theme")]
    public class DetachReparentLayoutTests {
        /// <summary>
        /// 模拟 W1 的 MixerControl：`LayoutUpdated` 里按视口写 MinHeight（值稳定后不再写，收敛），
        /// 每次视口变化都会产生一次挂起 arrange —— 正是崩溃所需的"待处理布局"状态。
        /// </summary>
        private sealed class LayoutChurning : UserControl {
            public readonly Border Child = new Border();
            private double lastApplied = -1;
            public int Writes { get; private set; }
            public LayoutChurning() {
                Content = Child;
                LayoutUpdated += (_, _) => {
                    double viewport = Bounds.Height;
                    if (viewport > 0 && Math.Abs(lastApplied - viewport) >= 0.5) {
                        lastApplied = viewport;
                        Child.MinHeight = viewport;
                        Writes++;
                    }
                };
            }
        }

        private static void Pump() => Dispatcher.UIThread.RunJobs();

        [AvaloniaFact]
        public void DetachAndFlush_ReparentBetweenWindows_DoesNotThrow() {
            var probe = new LayoutChurning();
            var hostA = new ContentControl { Content = probe };
            var winA = new Window { Width = 400, Height = 300, Content = hostA };
            var winB = new Window { Width = 600, Height = 400, Content = new ContentControl() };
            try {
                winA.Show();
                Pump();
                winA.UpdateLayout();
                Pump();
                Assert.True(probe.Writes > 0, "前置条件：LayoutUpdated 至少写过一次（制造挂起布局）");

                // 产品路径：摘旧树 + 冲洗旧窗口布局
                MainWindow.DetachAndFlush(hostA);
                Assert.Null(hostA.Content);

                // 挂新树
                var hostB = (ContentControl)winB.Content!;
                hostB.Content = probe;
                winB.Show();

                // 旧窗/新窗各自再布局 + 泵消息：修复前这里会抛 wrong LayoutManager
                var ex = Record.Exception(() => {
                    Pump();
                    winA.UpdateLayout();
                    probe.InvalidateArrange();
                    winA.UpdateLayout();
                    winB.UpdateLayout();
                    Pump();
                });
                Assert.Null(ex);

                // 控件确实换了家，且新家布局有效
                Assert.Same(hostB, probe.Parent);
                Assert.Same(winB, TopLevel.GetTopLevel(probe));
            } finally {
                winA.Close();
                winB.Close();
            }
        }

        [AvaloniaFact]
        public void DetachAndFlush_IsSafeOnEmptyAndClosedHost() {
            // 幂等/空宿主：没有内容、或窗口已关（TopLevel 取不到）时都不该抛
            var host = new ContentControl();
            var win = new Window { Width = 200, Height = 120, Content = host };
            win.Show();
            Pump();
            Assert.Null(Record.Exception(() => MainWindow.DetachAndFlush(host)));
            Assert.Null(Record.Exception(() => MainWindow.DetachAndFlush(host)));
            win.Close();
            Assert.Null(Record.Exception(() => MainWindow.DetachAndFlush(host)));
        }

        /// <summary>
        /// 附B 契约的窗口级复查：6 次"分离 → 收回"往复（同一控件实例、不新建/不销毁），
        /// 每次都在两个真实 Window 之间 reparent，全程无异常且实例唯一。
        /// </summary>
        [AvaloniaFact]
        public void ReparentCycle_SixTimes_KeepsSingleInstanceAndDoesNotThrow() {
            var probe = new LayoutChurning();
            var hostView = new ContentControl { Content = probe };          // 主窗视图容器
            var hostDetached = new ContentControl();                        // 分离窗容器
            var winMain = new Window { Width = 500, Height = 320, Content = hostView };
            var winDetached = new Window { Width = 700, Height = 460, Content = hostDetached };
            var seen = new System.Collections.Generic.List<object>();
            try {
                winMain.Show();
                winDetached.Show();
                Pump();
                winMain.UpdateLayout();

                for (int cycle = 0; cycle < 6; cycle++) {
                    // 分离：主窗容器 → 分离窗容器
                    MainWindow.DetachAndFlush(hostView);
                    hostDetached.Content = probe;
                    winDetached.Show();
                    Pump();
                    seen.Add(probe);

                    // 收回：分离窗容器 → 主窗容器
                    MainWindow.DetachAndFlush(hostDetached);
                    hostView.Content = probe;
                    Pump();
                    seen.Add(probe);

                    // 每次往复都制造一次挂起布局，钉住"待处理状态 + reparent"这个组合
                    probe.InvalidateArrange();
                    winMain.UpdateLayout();
                    winDetached.UpdateLayout();
                    Pump();
                }

                Assert.All(seen, s => Assert.Same(probe, s));               // 控件实例唯一
                Assert.Same(hostView, probe.Parent);                        // 最终回到视图区
                Assert.Same(winMain, TopLevel.GetTopLevel(probe));
            } finally {
                winMain.Close();
                winDetached.Close();
            }
        }

        /// <summary>
        /// 附B 契约在真实控件上的复查：用 W1 的**真 MixerControl** 走一遍我的分离/收回路径，
        /// 断言「实例唯一 + VU 定时器随挂载/可见性启停」。timerRunning 是 MixerControl 的内部只读探针
        /// （W1 的 MixerGeometryTests 已单测其 gating，这里验的是与 reparent 的集成）。
        /// </summary>
        [AvaloniaFact]
        public void ReparentCycle_KeepsMixerInstanceAndVuTimerContract() {
            var mixer = new MixerControl();
            var hostView = new ContentControl { Content = mixer };
            var hostDetached = new ContentControl();
            var winMain = new Window { Width = 900, Height = 700, Content = hostView };
            var winDetached = new Window { Width = 900, Height = 700, Content = hostDetached };
            try {
                winMain.Show();
                Pump();
                Assert.True(mixer.LevelTimerRunning, "贴合挂载后 VU 定时器应在跑");

                // 分离：视图容器 → 分离窗
                MainWindow.DetachAndFlush(hostView);
                Pump();
                Assert.False(mixer.LevelTimerRunning, "摘下后 VU 定时器应停");

                hostDetached.Content = mixer;
                winDetached.Show();
                Pump();
                Assert.True(mixer.LevelTimerRunning, "换到分离窗后 VU 定时器应起");
                Assert.Same(mixer, hostDetached.Content);

                // 收回：分离窗 → 视图容器
                MainWindow.DetachAndFlush(hostDetached);
                hostView.Content = mixer;
                winDetached.Hide();
                Pump();
                Assert.True(mixer.LevelTimerRunning, "收回视图区后 VU 定时器应起");
                Assert.Same(mixer, hostView.Content);

                // 全程同一个控件实例（不新建、不销毁）
                Assert.Same(mixer, hostView.Content);
                Assert.Same(winMain, TopLevel.GetTopLevel(mixer));
            } finally {
                MainWindow.DetachAndFlush(hostView);
                MainWindow.DetachAndFlush(hostDetached);
                winMain.Close();
                winDetached.Close();
            }
        }

        /// <summary>
        /// 用户关分离窗口这条路：真实 <see cref="MixerWindow"/> 在 OnClosing 里摘控件 + 冲洗布局
        /// （窗口销毁后再摘就冲洗不到了），再经 ReturnToHost 把控件挂回视图区，全程不能触发布局异常。
        /// </summary>
        [AvaloniaFact]
        public void DetachedWindow_UserClose_ReturnsControlSafely() {
            // MixerWindow.OnClosing 会 Preferences.Save()（写测试输出目录的 prefs.json）：
            // 跑完删掉，避免把本用例状态留给下一次测试会话（全仓测试不写 prefs）。
            string prefsPath = OpenUtau.Core.PathManager.Inst.PrefsFilePath;
            bool prefsExisted = System.IO.File.Exists(prefsPath);

            var mixer = new MixerControl();
            var hostView = new ContentControl { Content = mixer };
            var winMain = new Window { Width = 900, Height = 700, Content = hostView };
            winMain.Show();
            Pump();

            MainWindow.DetachAndFlush(hostView);
            var winDetached = new MixerWindow(mixer);
            bool returned = false;
            winDetached.ReturnToHost = () => {
                returned = true;
                hostView.Content = mixer;   // 等价于 MainWindow.AttachMixerView 的挂回视图区
            };
            winDetached.Show();
            Pump();
            mixer.InvalidateArrange();      // 关窗前制造挂起布局（历史序列里的"待处理"状态）

            try {
                var ex = Record.Exception(() => {
                    winDetached.Close();    // 用户关窗
                    Pump();
                    winMain.UpdateLayout();
                    Pump();
                });
                Assert.Null(ex);
                Assert.True(returned, "关窗应触发 ReturnToHost");
                Assert.Same(hostView, mixer.Parent);
                Assert.True(mixer.LevelTimerRunning, "控件回到视图区后 VU 定时器应重新起表");
            } finally {
                MainWindow.DetachAndFlush(hostView);
                winMain.Close();
                if (!prefsExisted && System.IO.File.Exists(prefsPath)) {
                    try { System.IO.File.Delete(prefsPath); } catch { /* 清理失败不影响断言结果 */ }
                }
            }
        }
    }
}

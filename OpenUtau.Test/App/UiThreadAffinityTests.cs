using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// **线程亲和门回归测试**（W25，同类缺陷第 4 次：I1 链面板 → MixerControl → FxChainPanel → ExpSelector）。
    ///
    /// 这一类缺陷的形状固定：某个 `ICmdSubscriber` 在 `OnNext` 里改**绑到控件**的集合/属性，
    /// 生产路径被 `DocManager.ExecuteCmd` 的主线程守卫兜住所以不暴露；一旦工程在**非 UI 线程**被加载
    /// （后台渲染、headless 用例），就会撞 `Dispatcher.VerifyAccess`。
    ///
    /// 这里刻意**把集合绑到真实 `ItemsControl`**（只有真绑定才会触发跨线程校验），
    /// 再从后台线程发 `LoadProjectNotification`：
    /// · 修前 ⇒ 后台线程上直接抛 `InvalidOperationException/VerifyAccess`（本用例会红）；
    /// · 修后 ⇒ 守卫把改动 `Post` 回订阅线程，后台线程安然返回，回主线程 Pump 后集合已更新。
    /// </summary>
    public class UiThreadAffinityTests {
        static UProject NewProject() {
            var project = new UProject();
            project.timeAxis.BuildSegments(project);
            // 表达式集必须**非空**：`OnListChange()` 是 Clear + 逐条 Add，集合为空时 Clear 不产生
            // 集合变更事件 ⇒ 跨线程校验根本不会触发，用例也就抓不到这个缺陷。
            // 直接写模型的表达式表：`ConfigureExpressionsCommand` 走 `ExecuteCmd`，而命令必须在
            // 撤销组内才会真正执行 —— 这里不必进撤销栈，只需给 VM 一份非空表达式集。
            project.expressions["vel"] = new UExpressionDescriptor {
                name = "Velocity", abbr = "vel", type = UExpressionType.Numerical,
                min = 0, max = 200, defaultValue = 100,
            };
            project.expressions["tension"] = new UExpressionDescriptor {
                name = "Tension", abbr = "tension", type = UExpressionType.Numerical,
                min = 0, max = 200, defaultValue = 100,
            };
            return project;
        }

        static void Pump() {
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();
        }

        [AvaloniaFact]
        public void ExpSelector_ProjectLoadedOffThread_DoesNotTouchBoundCollectionOffThread() {
            DocManagerTestSetup.RunOnCurrentThread();
            var (project, show, height, collapsed) = Pin();
            var vm = new ExpSelectorViewModel();
            var window = new Window {
                Width = 600,
                Height = 300,
                // 真绑定：`ItemsControl` 会在集合变更时校验线程（headless 宿主下这个校验不一定抛，
                // 所以下面还要**自己记录"变更发生在哪个线程"** —— 那才是可断言的硬不变量）
                Content = new ItemsControl { ItemsSource = vm.Descriptors },
            };
            var changedOn = new List<Thread>();
            vm.Descriptors.CollectionChanged += (_, _) => changedOn.Add(Thread.CurrentThread);
            Exception? thrown = null;
            var worker = new Thread(() => {
                try {
                    DocManager.Inst.ExecuteCmd(new LoadProjectNotification(NewProject()));
                } catch (Exception e) {
                    thrown = e;
                }
            });
            try {
                window.Show();
                window.UpdateLayout();
                Pump();
                Assert.NotEmpty(vm.Descriptors);

                worker.Start();
                worker.Join();

                Assert.Null(thrown);                 // 后台线程不得因跨线程改绑定集合而炸
                Pump();                              // 让 Post 回来的那次刷新在属主线程落地
                Assert.NotEmpty(vm.Descriptors);
                // **硬不变量**：绑到控件的集合**绝不能在发起加载的那个后台线程上**被改。
                // 刻意不断言"等于测试起始线程"：全量跑时 Avalonia headless 的 dispatcher 线程与
                // xUnit 执行线程不一定是同一个 ⇒ 那种断言会在全量里假红（本轮踩过这个）。
                // 去掉守卫后这里会记录到 worker 线程 ⇒ 用例红（已做负向对照确认灵敏度）。
                // 去掉守卫后这里会记录到 worker 线程 ⇒ 用例红（已做负向对照确认灵敏度）。
                Assert.NotEmpty(changedOn);
                Assert.DoesNotContain(worker, changedOn);
                Assert.All(changedOn, t => Assert.NotSame(worker, t));
            } finally {
                vm.Dispose();
                window.Close();
                Restore(project, show, height, collapsed);
            }
        }

        [AvaloniaFact]
        public void ExpSelector_OffThreadSelectExpression_DoesNotThrow() {
            DocManagerTestSetup.RunOnCurrentThread();
            var (project, show, height, collapsed) = Pin();
            var vm = new ExpSelectorViewModel();
            var window = new Window {
                Width = 600,
                Height = 300,
                Content = new ItemsControl { ItemsSource = vm.Descriptors },
            };
            try {
                window.Show();
                window.UpdateLayout();
                Pump();
                string abbr = vm.Descriptors.First().abbr;

                Exception? thrown = null;
                var worker = new Thread(() => {
                    try {
                        // OnSelectExp 会写 `SelectedIndex`/`DisplayMode`（都绑到控件）
                        DocManager.Inst.ExecuteCmd(new SelectExpressionNotification(abbr, 0, true));
                    } catch (Exception e) {
                        thrown = e;
                    }
                });
                worker.Start();
                worker.Join();

                Assert.Null(thrown);
                Pump();
            } finally {
                vm.Dispose();
                window.Close();
                Restore(project, show, height, collapsed);
            }
        }

        /// <summary>钉住这次用例会动的全局状态（工程 + 卷帘面板相关偏好），返回原值。</summary>
        static (UProject? project, bool show, double height, bool collapsed) Pin() {
            var old = (DocManager.Inst.Project,
                Preferences.Default.ShowExpressions,
                Preferences.Default.PanelLayout.PianoRollExpHeight,
                Preferences.Default.PanelLayout.PianoRollExpCollapsed);
            Preferences.Default.PanelLayout.PianoRollExpHeight = 150;
            Preferences.Default.PanelLayout.PianoRollExpCollapsed = false;
            Preferences.Default.ShowExpressions = true;
            Preferences.Save();
            var project = NewProject();
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(project));
            return old;
        }

        static void Restore(UProject? project, bool show, double height, bool collapsed) {
            Preferences.Default.ShowExpressions = show;
            Preferences.Default.PanelLayout.PianoRollExpHeight = height;
            Preferences.Default.PanelLayout.PianoRollExpCollapsed = collapsed;
            Preferences.Save();
            if (project != null) {
                DocManager.Inst.ExecuteCmd(new LoadProjectNotification(project));
            }
        }
    }
}

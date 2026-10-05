using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core;

namespace OpenUtau.Test.TestSupport {
    /// <summary>
    /// Minimal DocManager setup for tests that exercise code paths calling
    /// DocManager.Inst.ExecuteCmd. Avoids the heavy Initialize() (plugin search,
    /// PhonemizerRunner) by wiring only what ExecuteCmd needs.
    ///
    /// **全局态纪律**（W14 修复满载并行 flake 时立的规矩）：
    /// <c>DocManager.mainThread</c> / <c>PostOnUIThread</c> / <c>mainScheduler</c> 是
    /// **进程级全局**，xUnit 只保证 collection 内串行 ⇒ 任何用例改动它们都会影响正在
    /// 并行执行的其它用例（典型症状：别的 collection 的后台命令被投递进本用例的记录器，
    /// 于是"恰好投递一次"这类断言随机挂）。
    /// 需要改动时一律用 <see cref="EnterScopedDispatcher"/>：它保存-恢复全部三项，并给测试
    /// 一个**确定性的入队式通道**（生产里 <c>SplashWindow.axaml.cs</c> 就是这么做的），
    /// 由测试线程 <see cref="ScopedDispatcher.PumpAll"/> 显式驱动。
    /// </summary>
    internal static class DocManagerTestSetup {
        static readonly FieldInfo MainThreadField = typeof(DocManager)
            .GetField("mainThread", BindingFlags.NonPublic | BindingFlags.Instance)!;
        static readonly FieldInfo MainSchedulerField = typeof(DocManager)
            .GetField("mainScheduler", BindingFlags.NonPublic | BindingFlags.Instance)!;

        /// <summary>
        /// 旧式（无恢复）接线：把 mainThread 指到当前线程，使命令内联执行。
        /// 保留给既有用例；**新用例请用 <see cref="EnterScopedDispatcher"/>**
        /// （它同样内联，但会在 Dispose 时恢复全局态）。
        /// </summary>
        public static void RunOnCurrentThread() {
            MainThreadField.SetValue(DocManager.Inst, Thread.CurrentThread);
            // ExecuteCmd falls back to PostOnUIThread only when off-main-thread;
            // with mainThread == current thread it is never hit, so leave it null.
        }

        /// <summary>当前 mainThread / PostOnUIThread（诊断用）。</summary>
        public static (Thread? mainThread, Action<Action>? post) Snapshot() =>
            ((Thread?)MainThreadField.GetValue(DocManager.Inst), DocManager.Inst.PostOnUIThread);

        /// <summary>
        /// 进入"测试替身 UI 线程"作用域：mainThread = 当前线程；PostOnUIThread 换成
        /// 入队式确定性通道（<paramref name="nullChannel"/> = true 时**故意不装通道**，
        /// 用于验证"无头/测试宿主没有投递通道"的兜底路径）。
        /// Dispose 时恢复 mainThread / PostOnUIThread / mainScheduler 并丢弃未消费动作。
        /// </summary>
        public static ScopedDispatcher EnterScopedDispatcher(bool nullChannel = false, bool installScheduler = true) {
            var doc = DocManager.Inst;
            var scope = new ScopedDispatcher(
                doc,
                (Thread?)MainThreadField.GetValue(doc),
                doc.PostOnUIThread,
                (TaskScheduler?)MainSchedulerField.GetValue(doc));
            MainThreadField.SetValue(doc, Thread.CurrentThread);
            if (installScheduler) {
                MainSchedulerField.SetValue(doc, TaskScheduler.Default);
            }
            if (!nullChannel) {
                doc.PostOnUIThread = scope.Enqueue;
            }
            return scope;
        }

        /// <summary>测试替身 UI 线程 + 入队式投递通道（确定性、可显式驱动）。</summary>
        internal sealed class ScopedDispatcher : IDisposable {
            readonly DocManager doc;
            readonly Thread? savedMainThread;
            readonly Action<Action>? savedPost;
            readonly TaskScheduler? savedScheduler;
            readonly List<Action> pending = new();

            public ScopedDispatcher(DocManager doc, Thread? savedMainThread, Action<Action>? savedPost, TaskScheduler? savedScheduler) {
                this.doc = doc;
                this.savedMainThread = savedMainThread;
                this.savedPost = savedPost;
                this.savedScheduler = savedScheduler;
            }

            /// <summary>投递通道：**只入队**，不执行（模拟 UI 线程消息循环）。</summary>
            public void Enqueue(Action action) {
                lock (pending) {
                    pending.Add(action);
                }
            }

            public int PendingCount {
                get { lock (pending) { return pending.Count; } }
            }

            /// <summary>在本（测试）线程上执行全部挂起动作；返回执行条数。
            /// 执行期间 <c>Thread.CurrentThread == mainThread</c>，与生产 UI 线程语义一致。</summary>
            public int PumpAll() {
                Action[] actions;
                lock (pending) {
                    actions = pending.ToArray();
                    pending.Clear();
                }
                foreach (var action in actions) {
                    action();
                }
                return actions.Length;
            }

            public void Dispose() {
                MainThreadField.SetValue(doc, savedMainThread);
                doc.PostOnUIThread = savedPost;
                MainSchedulerField.SetValue(doc, savedScheduler);
                lock (pending) {
                    pending.Clear();
                }
            }
        }
    }
}

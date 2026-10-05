using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using OpenUtau.Core;
using Xunit;

namespace OpenUtau.Test.Core {
    /// <summary>
    /// W14 上游 `832aea2c`（"波形通知不再来自后台线程"）意图的**本树口径**验证。
    ///
    /// 我们没有逐点包 <c>Task.Factory.StartNew(..., MainScheduler)</c>，而是把线程锚定
    /// 集中在 <see cref="DocManager.ExecuteCmd"/>：非主线程调用 → 经
    /// <see cref="DocManager.PostOnUIThread"/> 投递回 UI 线程；通道缺失时（无头/测试宿主/
    /// 启动早期）就地执行并记 warning，绝不 NRE。
    ///
    /// 与其它会改 DocManager 全局状态的用例串行执行。
    /// </summary>
    [Collection("AudioFixture")]
    public class DocManagerExecuteCmdThreadingTests {
        readonly ITestOutputHelper output;

        public DocManagerExecuteCmdThreadingTests(ITestOutputHelper output) {
            this.output = output;
        }

        static readonly FieldInfo MainThreadField = typeof(DocManager)
            .GetField("mainThread", BindingFlags.NonPublic | BindingFlags.Instance)!;

        /// <summary>在"当前线程 = 主线程"的前提下，从另一线程调用 ExecuteCmd。</summary>
        static void RunOffThread(Action<Thread> body) {
            var thread = new Thread(() => body(Thread.CurrentThread));
            thread.Start();
            thread.Join();
        }

        [Fact]
        public void OffThreadCommand_IsPostedToUiThread_NotExecutedInline() {
            var doc = DocManager.Inst;
            Thread? savedMain = (Thread?)MainThreadField.GetValue(doc);
            Action<Action>? savedPost = doc.PostOnUIThread;
            var queued = new List<Action>();
            try {
                MainThreadField.SetValue(doc, Thread.CurrentThread);   // 本测试线程 = 主线程
                doc.PostOnUIThread = action => queued.Add(action);      // 记录而非执行
                int playPosBefore = doc.playPosTick;

                RunOffThread(_ => doc.ExecuteCmd(new SetPlayPosTickNotification(4321)));

                output.WriteLine($"投递数={queued.Count}，playPosTick={doc.playPosTick}（应保持 {playPosBefore}）");
                Assert.Single(queued);                       // 恰好投递一次
                Assert.Equal(playPosBefore, doc.playPosTick); // 未在后台线程就地执行

                // 主线程上真正执行投递的动作 ⇒ 命令生效（这正是 832aea2c 的意图）
                queued[0]();
                output.WriteLine($"在主线程执行投递动作后 playPosTick={doc.playPosTick}");
                Assert.Equal(4321, doc.playPosTick);
            } finally {
                MainThreadField.SetValue(doc, savedMain);
                doc.PostOnUIThread = savedPost;
            }
        }

        [Fact]
        public void OffThreadCommand_WithoutUiChannel_ExecutesInline_WithoutThrowing() {
            var doc = DocManager.Inst;
            Thread? savedMain = (Thread?)MainThreadField.GetValue(doc);
            Action<Action>? savedPost = doc.PostOnUIThread;
            try {
                MainThreadField.SetValue(doc, Thread.CurrentThread);
                doc.PostOnUIThread = null;      // 无头/测试宿主的缺口场景（此前会 NRE）
                Exception? thrown = null;

                RunOffThread(t => {
                    try {
                        doc.ExecuteCmd(new SetPlayPosTickNotification(8765));
                    } catch (Exception e) {
                        thrown = e;
                    }
                });

                output.WriteLine($"无投递通道时：异常={(thrown == null ? "无" : thrown.GetType().Name)}，" +
                                 $"playPosTick={doc.playPosTick}");
                Assert.Null(thrown);                 // 硬化前：NullReferenceException
                Assert.Equal(8765, doc.playPosTick); // 就地执行，命令不丢
            } finally {
                MainThreadField.SetValue(doc, savedMain);
                doc.PostOnUIThread = savedPost;
            }
        }

        [Fact]
        public void OffThreadProgressNotification_DoesNotThrow_AndIsNotLoggedAsUiTouch() {
            // ProgressBarNotification 在守卫里被豁免了 warning（渲染进度每秒多次），
            // 但同样必须走投递而不是后台就地执行。
            var doc = DocManager.Inst;
            Thread? savedMain = (Thread?)MainThreadField.GetValue(doc);
            Action<Action>? savedPost = doc.PostOnUIThread;
            var queued = new List<Action>();
            try {
                MainThreadField.SetValue(doc, Thread.CurrentThread);
                doc.PostOnUIThread = action => queued.Add(action);
                Exception? thrown = null;
                RunOffThread(t => {
                    try {
                        doc.ExecuteCmd(new ProgressBarNotification(42, "rendering"));
                    } catch (Exception e) {
                        thrown = e;
                    }
                });
                output.WriteLine($"进度通知投递数={queued.Count}，异常={(thrown == null ? "无" : thrown.GetType().Name)}");
                Assert.Null(thrown);
                Assert.Single(queued);
            } finally {
                MainThreadField.SetValue(doc, savedMain);
                doc.PostOnUIThread = savedPost;
            }
        }
    }
}

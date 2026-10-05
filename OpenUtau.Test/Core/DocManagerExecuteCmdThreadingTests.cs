using System;
using System.Threading;
using OpenUtau.Core;
using OpenUtau.Test.TestSupport;
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
    /// **确定性接线**（W14 flake 修复）：全局线程态一律经
    /// <see cref="DocManagerTestSetup.EnterScopedDispatcher"/> 注入、作用域结束时恢复；
    /// 断言只看**本用例自己的哨兵通知**（经自建订阅者计数），不数队列长度——满载并行时
    /// 别的 collection 的后台命令也经过同一通道，按条数断言必然随机挂（修复前 Dark/Light
    /// 各红 1 例正是此因）。
    /// </summary>
    [Collection("AudioFixture")]
    public class DocManagerExecuteCmdThreadingTests {
        readonly ITestOutputHelper output;

        public DocManagerExecuteCmdThreadingTests(ITestOutputHelper output) {
            this.output = output;
        }

        /// <summary>哨兵通知：命令被"处理"（Publish 到订阅者）时才计数，免疫其它用例的并发命令。</summary>
        sealed class SentinelNotification : UNotification {
            public override string ToString() => "W14 sentinel";
        }

        sealed class SentinelCounter : ICmdSubscriber {
            public int Handled;
            public void OnNext(UCommand cmd, bool isUndo) {
                if (cmd is SentinelNotification) {
                    Handled++;
                }
            }
        }

        static void RunOffThread(Action body) {
            var thread = new Thread(() => body());
            thread.Start();
            thread.Join();
        }

        static void WithCounter(Action<SentinelCounter> body) {
            var counter = new SentinelCounter();
            DocManager.Inst.AddSubscriber(counter);
            try {
                body(counter);
            } finally {
                DocManager.Inst.RemoveSubscriber(counter);
            }
        }

        [Fact]
        public void OffThreadCommand_IsPostedToUiThread_NotExecutedInline() {
            var sentinel = new SentinelNotification();
            using var scope = DocManagerTestSetup.EnterScopedDispatcher();
            WithCounter(counter => {
                RunOffThread(() => DocManager.Inst.ExecuteCmd(sentinel));

                // 后台线程不得就地执行（生产语义：命令只能在 UI 线程跑）
                output.WriteLine($"后台调用后：哨兵处理次数={counter.Handled}，投递队列长度={scope.PendingCount}");
                Assert.Equal(0, counter.Handled);
                Assert.True(scope.PendingCount >= 1, "命令没有被投递");

                int pumped = scope.PumpAll();   // 在测试线程（=本作用域的"UI 线程"）驱动投递
                output.WriteLine($"Pump {pumped} 条后：哨兵处理次数={counter.Handled}");
                Assert.Equal(1, counter.Handled);   // 恰好处理一次（不重复投递、不丢失）
            });
        }

        [Fact]
        public void OffThreadCommand_WithoutUiChannel_ExecutesInline_WithoutThrowing() {
            var sentinel = new SentinelNotification();
            using var scope = DocManagerTestSetup.EnterScopedDispatcher(nullChannel: true);
            WithCounter(counter => {
                Exception? thrown = null;
                RunOffThread(() => {
                    try {
                        DocManager.Inst.ExecuteCmd(sentinel);
                    } catch (Exception e) {
                        thrown = e;
                    }
                });

                output.WriteLine($"无投递通道时：异常={(thrown == null ? "无" : thrown.GetType().Name)}，" +
                                 $"哨兵处理次数={counter.Handled}");
                Assert.Null(thrown);                 // 硬化前：NullReferenceException
                Assert.Equal(1, counter.Handled);     // 就地执行，命令不丢
            });
        }

        [Fact]
        public void OffThreadProgressNotification_IsPosted_AndPumpDoesNotThrow() {
            using var scope = DocManagerTestSetup.EnterScopedDispatcher();
            Exception? thrown = null;
            RunOffThread(() => {
                try {
                    // 渲染进度每秒多次：守卫里豁免 warning，但仍必须走投递而非后台就地执行
                    DocManager.Inst.ExecuteCmd(new ProgressBarNotification(42, "rendering"));
                } catch (Exception e) {
                    thrown = e;
                }
            });
            output.WriteLine($"进度通知：异常={(thrown == null ? "无" : thrown.GetType().Name)}，" +
                             $"投递队列长度={scope.PendingCount}");
            Assert.Null(thrown);
            Assert.True(scope.PendingCount >= 1);
            Assert.Null(Record.Exception(() => scope.PumpAll()));
        }

        [Fact]
        public void ScopedDispatcher_RestoresGlobalThreadState() {
            var before = DocManagerTestSetup.Snapshot();
            using (DocManagerTestSetup.EnterScopedDispatcher()) {
                var during = DocManagerTestSetup.Snapshot();
                // 作用域内"主线程"必须是**本测试线程**（确定性），不假设它和进入前的值不同
                // ——同 collection 的用例可能被 xUnit 复用同一线程（此处原为 NotSame 断言，
                // 满载并行时因线程复用而挂，是 W14 flake 的一部分）。
                Assert.Same(Thread.CurrentThread, during.mainThread);
                Assert.NotNull(during.post);
            }
            var after = DocManagerTestSetup.Snapshot();
            output.WriteLine($"恢复检查：mainThread 相同={ReferenceEquals(before.mainThread, after.mainThread)}，" +
                             $"PostOnUIThread 相同={ReferenceEquals(before.post, after.post)}");
            Assert.Same(before.mainThread, after.mainThread);
            Assert.Same(before.post, after.post);
        }
    }
}

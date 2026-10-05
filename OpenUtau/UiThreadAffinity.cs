using System;
using System.Threading;
using Avalonia.Threading;

namespace OpenUtau.App {
    /// <summary>
    /// **归属线程门**（W25）：把"只许在自己的 UI 线程上动绑定集合 / StyledProperty"这件事
    /// 收敛成一处口径，供 <c>ICmdSubscriber</c> / <c>MessageBus</c> 订阅者使用。
    ///
    /// 为什么不用 `Dispatcher.UIThread.CheckAccess()`：
    /// 它在 **headless 测试宿主**下会误判（测试里没有真正的 UI 线程/或者当前线程恰好"看似"可访问），
    /// 我们已经为此踩过两次坑 ⇒ 改为**锚定"创建（订阅）时的所属线程"**，与框架怎么判定无关。
    /// 为什么用 `Post` 而不是 `Invoke`：命令是 `DocManager.Publish` 在**持有 DocManager 锁**的情况下
    /// 派发的，同步等待 UI 线程会与那把锁互锁（链面板 I1 那次就是为此改成 Post）。
    ///
    /// 用法（务必"守卫在入口、真正的活在 Core 里"——否则入队后再次进入守卫会自我循环）：
    /// <code>
    /// readonly UiThreadAffinity affinity = new UiThreadAffinity();
    /// public void OnNext(UCommand cmd, bool isUndo) {
    ///     if (!affinity.IsOwner) { affinity.Post(() => OnNextCore(cmd, isUndo)); return; }
    ///     OnNextCore(cmd, isUndo);
    /// }
    /// </code>
    /// </summary>
    internal sealed class UiThreadAffinity {
        private readonly Thread owner = Thread.CurrentThread;

        /// <summary>当前线程是否就是订阅时所在的那个线程。</summary>
        public bool IsOwner => Thread.CurrentThread == owner;

        /// <summary>在该线程上就地执行；否则 `Post` 到 UI 线程重新入队（异步、不阻塞、不持锁等待）。</summary>
        public void Post(Action action) {
            if (IsOwner) {
                action();
                return;
            }
            Dispatcher.UIThread.Post(action);
        }
    }
}

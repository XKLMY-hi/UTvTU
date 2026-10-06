using System;
using System.IO;
using System.Threading;
using OpenUtau.App;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// W41 单实例判定契约（`Program.cs` 的 <see cref="SingleInstanceGuard"/>）。
    ///
    /// 背景：原版 OpenUTAU 与 Plus 的**进程名同为 "OpenUtau"**（两个不同应用）。判据一旦按进程名，
    /// 用户开着原版就没法开 Plus，且退出无任何提示（看起来像"程序坏了"）。
    ///
    /// 两条硬要求（本类就是判据）：
    ///   · 同名进程但 exe 路径不同 ⇒ **不阻断**（原版在跑，Plus 照常启动）；
    ///   · exe 路径相同 ⇒ **阻断**（同一个 Plus 重复启动 ⇒ 聚焦已有窗口后退出）。
    /// 另外锁必须是"路径派生 + 可接管遗弃锁"：异常退出不留僵尸锁。
    /// 全部**进程内**完成：判定函数吃纯数据，锁用真互斥体但不真起进程。
    /// </summary>
    public class SingleInstanceGuardTests {
        const string PlusA = @"G:\builds\plus-a\OpenUtau.exe";
        const string PlusB = @"D:\other\plus-b\OpenUtau.exe";   // 同名、不同路径（例如原版 OpenUTAU）

        static string UniqueExePath(string tag) =>
            Path.Combine(Path.GetTempPath(), $"w41-{tag}-{Guid.NewGuid():N}", "OpenUtau.exe");

        /// <summary>同名（OpenUtau.exe）但路径不同 ⇒ 不是同一个程序 ⇒ 不阻断，锁名也不同。</summary>
        [Fact]
        public void SameProcessName_DifferentExePath_DoesNotBlock() {
            Assert.False(SingleInstanceGuard.IsSameApp(PlusB, PlusA));
            Assert.Null(SingleInstanceGuard.FindRunningInstance(PlusA, 100,
                new[] { new SingleInstanceGuard.InstanceInfo(4321, PlusB) }));
            Assert.NotEqual(SingleInstanceGuard.MutexNameFor(PlusA), SingleInstanceGuard.MutexNameFor(PlusB));
        }

        /// <summary>路径相同（大小写/相对写法归一）⇒ 同一个程序 ⇒ 判为阻断，且真锁第二次拿不到。</summary>
        [Fact]
        public void SameExePath_Blocks_AndSecondClaimFails() {
            Assert.True(SingleInstanceGuard.IsSameApp(@"g:\BUILDS\PLUS-A\openutau.EXE", PlusA));
            var existing = SingleInstanceGuard.FindRunningInstance(PlusA, 100,
                new[] { new SingleInstanceGuard.InstanceInfo(4321, @"G:\builds\plus-a\OpenUtau.exe") });
            Assert.NotNull(existing);
            Assert.Equal(4321, existing!.Pid);

            string path = UniqueExePath("claim");
            Assert.True(SingleInstanceGuard.TryClaim(out var first, path, out bool abandoned));
            Assert.False(abandoned);
            Assert.NotNull(first);
            try {
                // ⚠ 必须**换线程**取第二次：Win32 互斥体对**同一线程**是可重入的
                // （owner 再 WaitOne 会立刻成功），只有别的线程/别的进程才会被挡住。
                bool? blocked = null;
                Mutex? second = null;
                var other = new Thread(() => blocked = SingleInstanceGuard.TryClaim(out second, path, out _));
                other.Start();
                other.Join();
                Assert.False(blocked);      // ← 阻断（第二个实例拿不到锁）
                Assert.Null(second);
            } finally {
                first!.Dispose();   // 正常退出：锁释放
            }
            Assert.True(SingleInstanceGuard.TryClaim(out var third, path, out _));          // 释放后能再拿
            third!.Dispose();
        }

        /// <summary>路径不同 ⇒ 两把独立的锁都能拿到（原版/便携版/开发构建共存）。</summary>
        [Fact]
        public void DifferentExePaths_HoldIndependentLocks() {
            string a = UniqueExePath("a");
            string b = UniqueExePath("b");
            Assert.True(SingleInstanceGuard.TryClaim(out var lockA, a, out _));
            Assert.True(SingleInstanceGuard.TryClaim(out var lockB, b, out _));
            try {
                Assert.NotEqual(SingleInstanceGuard.MutexNameFor(a), SingleInstanceGuard.MutexNameFor(b));
            } finally {
                lockA!.Dispose();
                lockB!.Dispose();
            }
        }

        /// <summary>
        /// 异常退出（持锁线程直接结束，没 ReleaseMutex）⇒ 下一个实例必须能接管：
        /// `AbandonedMutexException` 视为可获取，绝不能让用户"锁死打不开"。
        /// </summary>
        [Fact]
        public void AbandonedMutex_IsTreatedAsAcquirable() {
            string path = UniqueExePath("abandon");
            string name = SingleInstanceGuard.MutexNameFor(path);
            var holder = new Thread(() => {
                var m = new Mutex(initiallyOwned: false, name);
                m.WaitOne();
                leaked = m;      // 保引用：句柄不被回收，线程带锁退出 ⇒ 系统标记为"遗弃"
            });
            holder.Start();
            holder.Join();

            Assert.True(SingleInstanceGuard.TryClaim(out var claimed, path, out bool wasAbandoned));
            Assert.NotNull(claimed);
            if (OS.IsWindows()) {
                // Windows 上命名互斥体有明确遗弃语义；非 Windows 的命名内核对象不同，只要求"能拿到"
                Assert.True(wasAbandoned);
            }
            claimed!.Dispose();
            leaked!.Dispose();
            leaked = null;
        }

        static Mutex? leaked;
    }
}

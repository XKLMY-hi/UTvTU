using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace OpenUtau.Core.Vst {
    internal static class VstTestSetup {
        /// <summary>Register a fake entry so VstEffect can resolve its slot's PluginUid.</summary>
        public static void Register(VstPluginEntry entry) {
            var registry = VstPluginRegistry.Inst;
            var field = typeof(VstPluginRegistry).GetField("_entries",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var dict = (Dictionary<string, VstPluginEntry>)field!.GetValue(registry)!;
            dict[entry.Uid] = entry;
        }

        public static VstPluginSlot CreateSlot(string uid, int index = 0) =>
            new(index) { PluginUid = uid };

        public static VstPluginEntry MakeEntry(string uid, string name = "TestPlugin") =>
            new() {
                Uid = uid, Name = name, Vendor = "TestCo",
                Path = "/fake/path.vst3", Type = VstPluginType.VST3,
                IsEffect = true, SubCategories = new List<string> { "Fx" },
            };

        // ── VST 用例互斥闸（W23 #3 收口）────────────────────────────────

        static readonly object gate = new object();
        static int nextTrackNo = 10000;

        /// <summary>
        /// 取一个**用例专属**的 trackNo，用于裸 <c>new UTrack()</c> 的 VST 用例。
        ///
        /// 为什么需要：<c>VstPluginManager</c> 是进程级单例，实例表按 <c>(trackNo, slot)</c>
        /// 索引，而裸 <c>new UTrack()</c> 的 <c>TrackNo</c> 默认是 **0** ⇒ 所有不挂工程的用例
        /// （含并行 collection 里的用例）共用同一批键：别人的 <c>LoadEffect(0, slot)</c> 会顶掉
        /// 你的实例、别人的卸载/清空会让你 <c>GetEffect</c> 变 null。取专属 trackNo 后键彻底隔离。
        /// （工程内轨道不行：<c>UTrack.Validate</c> 会把 <c>TrackNo</c> 重置为 <c>tracks.IndexOf</c>。）
        /// </summary>
        public static int NextTrackNo() => Interlocked.Increment(ref nextTrackNo);

        /// <summary>
        /// VST 用例互斥闸：xUnit 的 collection 只保证**同 collection 内**串行，跨 collection
        /// 仍可能重叠（本项目已实测：本用例执行期间别的 collection 的后台线程命令会走同一通道）。
        /// <c>VstPluginManager.Bridge</c> 与实例表都是进程级全局 ⇒ 会改它们的用例体放进这道闸，
        /// 无论 collection 怎么划都不会互相顶掉。
        /// 注意：闸只由**测试代码**获取；产品代码（渲染线程的 GetEffect 等）不取闸，故不会死锁。
        /// </summary>
        public static void RunExclusive(Action body) {
            lock (gate) {
                body();
            }
        }

        /// <summary>取闸（供需要跨"安装→使用→卸载"整段持有闸的夹具，如假 VST 句柄）。
        /// 必须由获取它的同一线程释放。</summary>
        public static void EnterGate() => Monitor.Enter(gate);

        /// <summary>释放闸（与 <see cref="EnterGate"/> 配对）。</summary>
        public static void ExitGate() => Monitor.Exit(gate);
    }
}

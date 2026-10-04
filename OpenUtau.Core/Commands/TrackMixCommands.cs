using System;
using System.Threading.Tasks;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Vst;
using Serilog;

namespace OpenUtau.Core {
    /// <summary>Generic reversible command backed by Action delegates.</summary>
    public class LambdaCommand : UCommand {
        readonly Action _do, _undo; readonly string _desc;
        public LambdaCommand(Action doAction, Action undoAction, string desc) {
            _do = doAction; _undo = undoAction; _desc = desc; }
        public override void Execute() => _do();
        public override void Unexecute() => _undo();
        public override string ToString() => _desc;
    }

    /// <summary>
    /// 内置效果链的三个模块（与 DSP 链的固定顺序一致：EQ → 压缩 → 混响）。
    /// 效果链面板按它给内置伪插件与 VST 槽同一套命令语义（B2）。
    /// </summary>
    public enum MixFxModule {
        Eq,
        Compressor,
        Reverb,
    }

    /// <summary>
    /// Track-scoped convenience helpers.
    /// VST 槽位命令同时驱动实例生命周期（do/undo 触发 LoadEffectAsync/UnloadEffect），
    /// 并靠 VstSlotChangedNotification 让 UI 在异步加载完成后重建行。
    /// </summary>
    public static class TrackMixCommands {
        public static UCommand Mute(UTrack track, bool newValue) {
            bool old = track.Mute;
            return new LambdaCommand(
                () => { track.Mute = newValue; track.Muted = newValue; },
                () => { track.Mute = old; track.Muted = old; },
                $"Mute track {track.TrackName} = {newValue}");
        }
        public static UCommand Solo(UTrack track, bool newValue) {
            bool old = track.Solo;
            return new LambdaCommand(
                () => track.Solo = newValue,
                () => track.Solo = old,
                $"Solo track {track.TrackName} = {newValue}");
        }

        /// <summary>添加槽位并加载插件（do：写 UID + 异步 Load；undo：Unload + Clear）。</summary>
        public static UCommand AddVstSlot(UTrack track, int slotIndex, string pluginUid) {
            return new LambdaCommand(
                () => {
                    while (track.VstSlots.Count <= slotIndex)
                        track.VstSlots.Add(new VstPluginSlot(track.VstSlots.Count));
                    var slot = track.VstSlots[slotIndex];
                    slot.PluginUid = pluginUid;
                    if (!string.IsNullOrEmpty(pluginUid)) LoadAndNotify(track, slot);
                },
                () => {
                    if (slotIndex >= track.VstSlots.Count) return;
                    VstPluginManager.Inst.UnloadEffect(track.TrackNo, slotIndex);
                    track.VstSlots[slotIndex].Clear();
                },
                $"VST slot {track.TrackName}[{slotIndex}] ← {pluginUid}");
        }

        /// <summary>
        /// 移除槽位（do：Unload（内部 SaveState 写回 slot）+ Clear；undo：恢复 UID，
        /// 重新 Load 时 LoadAt 自动 RestoreState 还原参数）。
        /// </summary>
        public static UCommand RemoveVstSlot(UTrack track, int slotIndex) {
            string? oldUid = slotIndex < track.VstSlots.Count ? track.VstSlots[slotIndex].PluginUid : null;
            return new LambdaCommand(
                () => {
                    if (slotIndex >= track.VstSlots.Count) return;
                    VstPluginManager.Inst.UnloadEffect(track.TrackNo, slotIndex);
                    track.VstSlots[slotIndex].Clear();
                },
                () => {
                    while (track.VstSlots.Count <= slotIndex)
                        track.VstSlots.Add(new VstPluginSlot(track.VstSlots.Count));
                    if (slotIndex >= track.VstSlots.Count) return;
                    var slot = track.VstSlots[slotIndex];
                    slot.PluginUid = oldUid ?? "";
                    if (!string.IsNullOrEmpty(slot.PluginUid)) {
                        LoadAndNotify(track, slot);
                    }
                },
                $"Remove VST slot {track.TrackName}[{slotIndex}]");
        }

        /// <summary>
        /// 替换槽位插件（do：首次执行捕获旧 UID → 写新 UID + Load；undo：恢复旧 UID + Load）。
        /// 旧实例由 LoadAtAsync 内部替换并延迟销毁。
        /// </summary>
        public static UCommand SetVstPlugin(UTrack track, int slotIndex, string newUid) {
            return new SetVstPluginCommand(track, slotIndex, newUid);
        }

        sealed class SetVstPluginCommand : UCommand {
            readonly UTrack track;
            readonly int slotIndex;
            readonly string newUid;
            string oldUid = "";
            bool captured;

            public SetVstPluginCommand(UTrack track, int slotIndex, string newUid) {
                this.track = track;
                this.slotIndex = slotIndex;
                this.newUid = newUid;
            }

            public override void Execute() {
                if (!captured) {
                    // do 首次执行时捕获旧 UID（此时是真正替换前的值）
                    oldUid = slotIndex < track.VstSlots.Count ? track.VstSlots[slotIndex].PluginUid : "";
                    captured = true;
                }
                SetAndLoad(track, slotIndex, newUid);
            }

            public override void Unexecute() {
                SetAndLoad(track, slotIndex, oldUid);
            }

            public override string ToString() => $"VST slot {track.TrackName}[{slotIndex}] → {newUid}";
        }

        /// <summary>
        /// 槽位旁路（数据级，下次渲染快照生效——无需重载实例）。
        /// 自定义命令类：do 首次执行捕获旧值，取反**当前**值（toggle 语义——
        /// LambdaCommand 的固定 !old 在"撤销后新命令"场景会把启用点成关闭）。
        /// </summary>
        public static UCommand ToggleVstBypass(UTrack track, int slotIndex) {
            return new ToggleVstBypassCommand(track, slotIndex);
        }

        sealed class ToggleVstBypassCommand : UCommand {
            readonly UTrack track;
            readonly int slotIndex;
            bool oldValue;
            bool captured;

            public ToggleVstBypassCommand(UTrack track, int slotIndex) {
                this.track = track;
                this.slotIndex = slotIndex;
            }

            public override void Execute() {
                if (slotIndex >= track.VstSlots.Count) return;
                var slot = track.VstSlots[slotIndex];
                if (!captured) {
                    oldValue = slot.Bypassed;
                    captured = true;
                }
                slot.Bypassed = !oldValue;
            }

            public override void Unexecute() {
                if (slotIndex >= track.VstSlots.Count) return;
                track.VstSlots[slotIndex].Bypassed = oldValue;
            }

            public override string ToString() => $"Toggle VST bypass {track.TrackName}[{slotIndex}]";
        }

        // ── 内置效果链（MixFx）——效果链面板的 B2/B8 落点 ───────────────

        /// <summary>
        /// 内置模块电源（旁通的反面）：do 写目标值，undo 写回旧值。
        /// 面板/编辑器一律走它，避免"直接改 fx.EqEnabled"这种不进 undo 队列的写法。
        /// </summary>
        public static UCommand SetMixFxModule(UTrack track, MixFxModule module, bool enabled) {
            bool old = IsModuleEnabled(track.MixFx, module);
            return new LambdaCommand(
                () => SetModuleEnabled(track.MixFx, module, enabled),
                () => SetModuleEnabled(track.MixFx, module, old),
                $"MixFx {module} on {track.TrackName} = {enabled}");
        }

        /// <summary>链路总电源（内置段整体通过声 / 旁通）。</summary>
        public static UCommand SetMixFxEnabled(UTrack track, bool enabled) {
            bool old = track.MixFx?.Enabled ?? false;
            return new LambdaCommand(
                () => { if (track.MixFx != null) track.MixFx.Enabled = enabled; },
                () => { if (track.MixFx != null) track.MixFx.Enabled = old; },
                $"MixFx enabled on {track.TrackName} = {enabled}");
        }

        /// <summary>
        /// 替换整条内置效果链的模型引用（<paramref name="fx"/> = null 表示回到"未配置效果"）。
        /// do 捕获旧引用，undo 原样放回 —— 首次把内置模块加进链时用它，
        /// 保证"加内置效果"和"加 VST 插件"一样可撤销、且不留空 UMixFx。
        /// </summary>
        public static UCommand SetMixFx(UTrack track, UMixFx? fx) {
            UMixFx? old = track.MixFx;
            return new LambdaCommand(
                () => track.MixFx = fx,
                () => track.MixFx = old,
                $"Set MixFx on {track.TrackName}");
        }

        /// <summary>
        /// VST 槽位重排（效果链面板的拖拽/上下移落点）。
        ///
        /// 语义 = 把两个槽的**载荷**互换（PluginUid + 参数状态 StateData + Bypassed），
        /// 然后重载这两个下标上的实例；槽位列表与 SlotIndex 都不动。
        ///
        /// 为什么不是"移动列表元素"：实例数组 <c>VstTrackInstances._effects</c> 以**下标**为键
        /// （<c>LoadAt(index, slot)</c>），每个 <c>VstEffect</c> 又反向持有它构造时的 slot；
        /// 移动列表元素就必须重排 SlotIndex 并把每个受影响下标全部卸载重载（同样的原生开销 +
        /// 更多失败面）。互换载荷后重载这 2 个下标，得到完全相同的可听顺序与更小的改动面。
        ///
        /// 命令对合：do 与 undo 是同一条操作（交换两次即还原），因此 -Undo 往返天然成立；
        /// 重排要求"参数状态跟着插件走"，这正是连 StateData 一起交换的原因。
        /// </summary>
        public static UCommand ReorderVstSlot(UTrack track, int slotA, int slotB) {
            void Apply() {
                var slots = track.VstSlots;
                if (slots == null || slotA == slotB) return;
                if (slotA < 0 || slotB < 0 || slotA >= slots.Count || slotB >= slots.Count) return;

                // 1) 状态写回 + 卸载（UnloadEffect 内部 SaveState 写回各自槽；延迟销毁由管理器负责）
                VstPluginManager.Inst.UnloadEffect(track.TrackNo, slotA);
                VstPluginManager.Inst.UnloadEffect(track.TrackNo, slotB);

                // 2) 载荷互换（对合操作）
                var sa = slots[slotA];
                var sb = slots[slotB];
                (sa.PluginUid, sb.PluginUid) = (sb.PluginUid, sa.PluginUid);
                (sa.StateData, sb.StateData) = (sb.StateData, sa.StateData);
                (sa.Bypassed, sb.Bypassed) = (sb.Bypassed, sa.Bypassed);

                // 3) 下标归一化：SlotIndex 恒等于列表位置（损坏的工程值借此修正；
                //    它不参与 undo 还原——把损坏状态搬回去没有意义）
                sa.SlotIndex = slotA;
                sb.SlotIndex = slotB;

                // 4) 按新载荷重载这两个下标（异步；完成后发通知）
                LoadAndNotify(track, sa);
                LoadAndNotify(track, sb);
            }
            return new LambdaCommand(Apply, Apply, $"Reorder VST slots {track.TrackName}[{slotA}⇄{slotB}]");
        }

                /// <summary>
        /// 读内置模块电源。**模块 ↔ 模型字段的映射只此一处**（效果链面板的静态描述表
        /// 也走它，避免 App 层再抄一份 switch）。
        /// </summary>
        public static bool IsModuleEnabled(UMixFx? fx, MixFxModule module) => module switch {
            MixFxModule.Eq => fx?.EqEnabled ?? false,
            MixFxModule.Compressor => fx?.CompEnabled ?? false,
            MixFxModule.Reverb => fx?.ReverbEnabled ?? false,
            _ => false,
        };

        /// <summary>写内置模块电源（<paramref name="fx"/> 为 null 时不做任何事）。</summary>
        public static void SetModuleEnabled(UMixFx? fx, MixFxModule module, bool value) {
            if (fx == null) return;
            switch (module) {
                case MixFxModule.Eq: fx.EqEnabled = value; break;
                case MixFxModule.Compressor: fx.CompEnabled = value; break;
                case MixFxModule.Reverb: fx.ReverbEnabled = value; break;
            }
        }

        // ── helpers ───────────────────────────────────────────────

        static void SetAndLoad(UTrack track, int slotIndex, string newUid) {
            while (track.VstSlots.Count <= slotIndex)
                track.VstSlots.Add(new VstPluginSlot(track.VstSlots.Count));
            if (slotIndex >= track.VstSlots.Count) return;
            var slot = track.VstSlots[slotIndex];
            slot.PluginUid = newUid;
            if (!string.IsNullOrEmpty(newUid)) {
                LoadAndNotify(track, slot);
            } else {
                VstPluginManager.Inst.UnloadEffect(track.TrackNo, slotIndex);
            }
        }

        /// <summary>
        /// 异步加载实例（原生秒级，移出 UI 线程）；完成后发 VstSlotChangedNotification
        /// （DocManager.ExecuteCmd 非 UI 线程自动回投）。undo 恢复旧 UID 时由 do 闭包
        /// 首次执行的旧值记录——SetVstPlugin 的 undo 需要旧值，见闭包设计。
        /// </summary>
        static void LoadAndNotify(UTrack track, VstPluginSlot slot) {
            _ = Task.Run(async () => {
                try {
                    await VstPluginManager.Inst.LoadEffectAsync(track.TrackNo, slot);
                    DocManager.Inst.ExecuteCmd(new VstSlotChangedNotification(track.TrackNo, slot.SlotIndex));
                } catch (Exception ex) {
                    Log.Error(ex, $"[VstCmd] Load failed T{track.TrackNo}S{slot.SlotIndex}");
                }
            });
        }
    }
}

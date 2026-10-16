using System.Collections.Generic;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Audio;
using Serilog;

namespace OpenUtau.Core.Vst {
    /// <summary>
    /// Global VST instance registry.
    /// UI thread: LoadEffect / UnloadEffect.
    /// Audio thread: GetActiveEffects (lock-free read via volatile).
    /// </summary>
    public class VstPluginManager {
        private static VstPluginManager? _inst;
        public static VstPluginManager Inst => _inst ??= new();

        /// <summary>Test seam: inject a fake bridge for unit-testing VST lifecycle.</summary>
        public IVstBridge Bridge { get; set; } = RealVstBridge.Instance;

        private readonly object _lock = new();
        private readonly Dictionary<int, VstTrackInstances> _tracks = new();

        // ── Scanning ────────────────────────────────────────────

        public List<VstPluginInfo> ScanPlugins() {
            VstPluginRegistry.Inst.Rescan();
            return KnownPlugins.Values.ToList();
        }
        public Dictionary<string, VstPluginInfo> KnownPlugins => VstPluginRegistry.Inst.All
            .ToDictionary(e => e.Uid, e => new VstPluginInfo {
                PluginUid = e.Uid, PluginName = e.Name, PluginPath = e.Path,
                PluginType = e.Type, Vendor = e.Vendor,
                Subs = e.SubCategories.ToList(), IsEffect = e.IsEffect,
            });

        // ── Track instances ─────────────────────────────────────

        VstTrackInstances GetOrCreate(int trackNo) {
            lock (_lock) {
                if (!_tracks.TryGetValue(trackNo, out var ti)) {
                    ti = new VstTrackInstances(trackNo) { Bridge = Bridge };
                    _tracks[trackNo] = ti;
                } else {
                    ti.Bridge = Bridge;
                }
                return ti;
            }
        }

        /// <summary>Load/reload a VST effect. Called from UI thread.</summary>
        public VstEffect? LoadEffect(int trackNo, VstPluginSlot slot) {
            var ti = GetOrCreate(trackNo);
            return ti.LoadAt(slot.SlotIndex, slot);
        }

        /// <summary>
        /// Async load/reload — 原生 Load/Setup/RestoreState 移出 UI 线程（秒级阻塞消除），
        /// 同 track 并发加载由 _loadGate 串行化。返回加载后的实例（失败返回 null）。
        /// </summary>
        public Task<VstEffect?> LoadEffectAsync(int trackNo, VstPluginSlot slot, CancellationToken ct = default) {
            var ti = GetOrCreate(trackNo);
            return ti.LoadAtAsync(slot.SlotIndex, slot, ct);
        }

        /// <summary>Unload an effect. Called from UI thread.</summary>
        public void UnloadEffect(int trackNo, int slotIndex) {
            if (_tracks.TryGetValue(trackNo, out var ti))
                ti.UnloadAt(slotIndex);
        }

        /// <summary>Get active effects for a track (audio thread safe).</summary>
        public IReadOnlyList<VstEffect> GetActiveEffects(int trackNo) {
            if (_tracks.TryGetValue(trackNo, out var ti))
                return ti.GetActiveEffects();
            return System.Array.Empty<VstEffect>();
        }

        /// <summary>Get a specific slot's effect.</summary>
        public VstEffect? GetEffect(int trackNo, int slotIndex) {
            if (_tracks.TryGetValue(trackNo, out var ti))
                return ti[slotIndex];
            return null;
        }

        /// <summary>Save state for all loaded effects on a track（含旁通槽——旁通时 GUI 调参也必须持久化）。</summary>
        public void SaveAllStates(int trackNo) => SaveAllStates(trackNo, null);

        public void SaveAllStates(int trackNo, TimeSpan? budget = null) {
            if (!_tracks.TryGetValue(trackNo, out var ti)) return;
            var all = ti.GetAllEffects();
            var elapsed = Stopwatch.StartNew();
            foreach (var fx in all) {
                if (!ReferenceEquals(ti[fx.Slot.SlotIndex], fx) || fx.Slot.PluginUid != fx.Entry.Uid) continue;
                var state = budget.HasValue ? fx.TrySaveState(budget.Value - elapsed.Elapsed) : fx.SaveState();
                if (state != null && ReferenceEquals(ti[fx.Slot.SlotIndex], fx) && fx.Slot.PluginUid == fx.Entry.Uid)
                    fx.Slot.StateData = state;
            }
        }

        public void RemoveTrack(int trackNo) {
            lock (_lock) {
                if (_tracks.TryGetValue(trackNo, out var ti)) {
                    ti.Dispose(); _tracks.Remove(trackNo);
                }
            }
        }
        /// <summary>Safe-dispose all effects queued for removal on all tracks.
        /// 仅在所有渲染/导出消费段退出（RenderGate.InFlight == 0）且输出未播放、
        /// 且非录制式导出中（录制期间主输出是 Dummy，OutputActive 恒 false，
        /// 但录制 WasapiOut 回调仍在消费 VST 链）时执行；
        /// 否则跳过并记日志，延迟到下一安全点（B1 竞态修复——禁止在音频线程仍可
        /// 触碰旧 handle 时 vst_unload）。</summary>
        public bool TryFlushAllPendingDispose() {
            if (RenderGate.InFlight != 0 || PlaybackManager.Inst.OutputActive || PlaybackManager.Inst.IsRecording) {
                return false;
            }
            lock (_lock) {
                foreach (var kv in _tracks) kv.Value.FlushPendingDispose();
            }
            return true;
        }

        /// <summary>
        /// Unload all tracks' slots（延迟销毁，无 UI 线程原生 Dispose）。
        /// 工程切换时替代 ClearAll——原生 Dispose 收敛到安全点（Flush）。
        /// </summary>
        public void UnloadAllTracks() {
            lock (_lock) {
                foreach (var kv in _tracks) kv.Value.UnloadAll();
            }
        }

        public void ClearAll() {
            lock (_lock) {
                foreach (var kv in _tracks) kv.Value.Dispose();
                _tracks.Clear();
            }
        }
        public static List<VstPluginSlot> CreateDefaultSlots(int count = 3) {
            var s = new List<VstPluginSlot>();
            for (int i = 0; i < count; i++) s.Add(new VstPluginSlot(i));
            return s;
        }
    }

    public class VstPluginInfo {
        public string PluginUid { get; set; } = "";
        public string PluginName { get; set; } = "";
        public string PluginPath { get; set; } = "";
        public VstPluginType PluginType { get; set; }
        public string Vendor { get; set; } = "";
        public List<string> Subs { get; set; } = new();
        public bool IsEffect { get; set; } = true;
        public override string ToString() => $"[{PluginType}] {PluginName}";
    }
}

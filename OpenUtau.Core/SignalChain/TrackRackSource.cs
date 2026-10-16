using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using OpenUtau.Core.SignalChain.Effects;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Vst;

namespace OpenUtau.Core.SignalChain {
    /// <summary>Graphs are prepared on the editing thread and atomically adopted at a block boundary.</summary>
    public sealed class TrackRackSource : ISignalSource {
        static readonly ConditionalWeakTable<UTrack, List<WeakReference<TrackRackSource>>> sources = new();
        readonly ISignalSource input;
        readonly UTrack track;
        volatile ISignalSource graph;
        string topology = "";

        public TrackRackSource(ISignalSource input, UTrack track) {
            this.input = input; this.track = track; graph = input;
            var list = sources.GetOrCreateValue(track);
            lock (list) {
                list.RemoveAll(w => !w.TryGetTarget(out _));
                list.Add(new WeakReference<TrackRackSource>(this));
                Prepare();
            }
        }

        public static void Refresh(UTrack track) {
            if (!sources.TryGetValue(track, out var list)) return;
            lock (list) {
                for (int i = list.Count - 1; i >= 0; i--)
                    if (list[i].TryGetTarget(out var source)) source.Prepare(); else list.RemoveAt(i);
            }
        }

        void Prepare() {
            var entries = TrackEffectRack.Read(track);
            string signature = (track.EffectRack == null ? "legacy:" : "rack:") +
                string.Join("|", entries.Select(e => $"{e.Id}:{e.Kind}:{e.VstSlot}"));
            if (signature == topology) return;
            ISignalSource next = input;
            if (track.EffectRack == null) next = MixFxSource.WrapLive(next, track);
            foreach (var entry in entries) {
                if (entry.Kind == TrackEffectKind.Vst) {
                    next = EffectChain.Build(next, new IEffect[] { new LiveSlot(track, entry.VstSlot) });
                } else if (track.EffectRack != null) {
                    string id = entry.Id;
                    next = MixFxSource.WrapLive(next, () => TrackEffectRack.Find(track, id)?.Settings);
                }
            }
            topology = signature;
            graph = next;
        }

        public bool IsReady(int position, int count) => input.IsReady(position, count);
        public int Mix(int position, float[] buffer, int index, int count) {
            var current = graph;
            return current.Mix(position, buffer, index, count);
        }

        sealed class LiveSlot : IEffect {
            readonly UTrack track;
            readonly int index;
            public LiveSlot(UTrack track, int index) { this.track = track; this.index = index; }
            VstEffect? Current {
                get {
                    if (index < 0 || index >= track.VstSlots.Count) return null;
                    var slot = track.VstSlots[index];
                    if (!slot.IsLoaded || slot.Bypassed) return null;
                    var fx = VstPluginManager.Inst.GetEffect(track.TrackNo, index);
                    return fx?.IsLoaded == true && fx.Entry?.Uid == slot.PluginUid ? fx : null;
                }
            }
            public bool IsBypassed => Current == null;
            public int LatencySamples => Current?.LatencySamples ?? 0;
            public void Process(float[] buffer, int offset, int count) => Current?.Process(buffer, offset, count);
            public void Reset() { }
        }

        public static ISignalSource Snapshot(ISignalSource input, IReadOnlyList<UTrackEffect> entries,
            IReadOnlyDictionary<int, IEffect> vst, bool builtIn = true, bool external = true) {
            ISignalSource result = input;
            foreach (var entry in entries) {
                if (entry.Kind == TrackEffectKind.Vst) {
                    if (external && vst.TryGetValue(entry.VstSlot, out var effect))
                        result = EffectChain.Build(result, new[] { effect });
                } else if (builtIn) result = MixFxSource.WrapWith(result, entry.Settings);
            }
            return result;
        }
    }
}

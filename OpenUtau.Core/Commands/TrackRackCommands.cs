using System;
using System.Linq;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Vst;

namespace OpenUtau.Core {
    public static class TrackRackCommands {
        public static UCommand Set(UTrack track, System.Collections.Generic.List<UTrackEffect>? next) {
            var old = track.EffectRack;
            return new TrackMixCommand(new[] { track }, TrackMixProperty.Effects,
                () => track.EffectRack = next, () => track.EffectRack = old, "Set MixFx rack");
        }

        public static UCommand Add(UTrack track, TrackEffectKind kind) {
            if (kind == TrackEffectKind.Vst) throw new ArgumentException("Use the VST picker.");
            var next = TrackEffectRack.Read(track).Select(e => e.Clone()).ToList();
            next.Add(new UTrackEffect { Kind = kind, Settings = TrackEffectRack.Isolate(new UMixFx { Enabled = true }, kind) });
            return Set(track, next);
        }

        public static UCommand Settings(UTrack track, string id, UMixFx settings) {
            var next = TrackEffectRack.Read(track).Select(e => e.Clone()).ToList();
            var entry = next.FirstOrDefault(e => e.Id == id);
            if (entry == null || entry.Kind == TrackEffectKind.Vst) throw new InvalidOperationException("Effect no longer exists.");
            entry.Settings = TrackEffectRack.Isolate(settings, entry.Kind);
            return Set(track, next);
        }

        public static UCommand Power(UTrack track, string id, bool enabled) {
            var entry = TrackEffectRack.Read(track).First(e => e.Id == id);
            if (entry.Kind == TrackEffectKind.Vst) {
                var slot = track.VstSlots[entry.VstSlot];
                bool old = slot.Bypassed;
                void Apply(bool bypass) {
                    slot.Bypassed = bypass;
                    if (!bypass && VstPluginManager.Inst.GetEffect(track.TrackNo, entry.VstSlot) == null)
                        TrackMixCommands.LoadAndNotify(track, slot);
                }
                return new TrackMixCommand(new[] { track }, TrackMixProperty.Effects,
                    () => Apply(!enabled), () => Apply(old), "Power VST MixFx rack");
            }
            var fx = entry.Settings?.Clone() ?? new UMixFx();
            fx.Enabled = enabled;
            TrackMixCommands.SetModuleEnabled(fx, (MixFxModule)entry.Kind, enabled);
            return Settings(track, id, fx);
        }

        public static UCommand Move(UTrack track, string id, int delta) {
            var next = TrackEffectRack.Read(track).Select(e => e.Clone()).ToList();
            int at = next.FindIndex(e => e.Id == id);
            if (at < 0) throw new InvalidOperationException("Effect no longer exists.");
            int to = Math.Clamp(at + delta, 0, next.Count - 1);
            var entry = next[at]; next.RemoveAt(at); next.Insert(to, entry);
            return Set(track, next);
        }

        public static UCommand Remove(UTrack track, string id) {
            var old = track.EffectRack;
            var entries = TrackEffectRack.Read(track);
            var entry = entries.First(e => e.Id == id);
            var next = entries.Where(e => e.Id != id).Select(e => e.Clone()).ToList();
            if (entry.Kind != TrackEffectKind.Vst) return Set(track, next);
            var slot = track.VstSlots[entry.VstSlot];
            string uid = slot.PluginUid;
            bool bypass = slot.Bypassed;
            byte[]? state = null;
            return new TrackMixCommand(new[] { track }, TrackMixProperty.Effects, () => {
                VstPluginManager.Inst.UnloadEffect(track.TrackNo, entry.VstSlot);
                state = slot.StateData?.ToArray();
                slot.Clear();
                track.EffectRack = next;
            }, () => {
                slot.PluginUid = uid; slot.StateData = state?.ToArray(); slot.Bypassed = bypass;
                track.EffectRack = old;
                TrackMixCommands.LoadAndNotify(track, slot);
            }, "Remove MixFx rack VST");
        }

        public static UCommand AssignVst(UTrack track, int index, string uid) {
            var old = track.EffectRack;
            if (index < 0 || index > track.VstSlots.Count) throw new ArgumentOutOfRangeException(nameof(index));
            var slot = index < track.VstSlots.Count ? track.VstSlots[index] : new VstPluginSlot(index);
            string oldUid = slot.PluginUid;
            bool oldBypass = slot.Bypassed;
            byte[]? oldState = slot.StateData?.ToArray(), newState = null;
            System.Collections.Generic.List<UTrackEffect>? next = null;
            return new TrackMixCommand(new[] { track }, TrackMixProperty.Effects, () => {
                if (track.VstSlots.Count == index) track.VstSlots.Add(slot);
                VstPluginManager.Inst.UnloadEffect(track.TrackNo, index);
                oldState = slot.StateData?.ToArray();
                slot.PluginUid = uid; slot.StateData = newState?.ToArray(); slot.Bypassed = false;
                next ??= TrackEffectRack.Read(track).Select(e => e.Clone()).ToList();
                track.EffectRack = next;
                TrackMixCommands.LoadAndNotify(track, slot);
            }, () => {
                VstPluginManager.Inst.UnloadEffect(track.TrackNo, index);
                newState = slot.StateData?.ToArray();
                slot.PluginUid = oldUid; slot.StateData = oldState?.ToArray(); slot.Bypassed = oldBypass;
                track.EffectRack = old;
                if (slot.IsLoaded) TrackMixCommands.LoadAndNotify(track, slot);
            }, "Set VST MixFx rack");
        }
    }
}

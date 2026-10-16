using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenUtau.Core.Ustx {
    public enum TrackEffectKind { Eq, Compressor, Reverb, Vst }

    /// <summary>A stable rack instance. VST slots retain their original storage indices.</summary>
    public sealed class UTrackEffect {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public TrackEffectKind Kind { get; set; }
        public int VstSlot { get; set; } = -1;
        public UMixFx? Settings { get; set; }
        public UTrackEffect Clone() => new() { Id = Id, Kind = Kind, VstSlot = VstSlot, Settings = Settings?.Clone() };
    }

    public static class TrackEffectRack {
        // Reading a legacy project is deliberately side-effect free.
        public static IReadOnlyList<UTrackEffect> Read(UTrack track) {
            var result = track.EffectRack?.ToList() ?? new List<UTrackEffect>();
            if (track.EffectRack == null && track.MixFx != null) {
                foreach (var kind in new[] { TrackEffectKind.Eq, TrackEffectKind.Compressor, TrackEffectKind.Reverb })
                    result.Add(new UTrackEffect { Id = "legacy-" + kind, Kind = kind, Settings = Isolate(track.MixFx, kind) });
            }
            foreach (var slot in track.VstSlots.Where(s => s.IsLoaded).OrderBy(s => s.SlotIndex)) {
                if (!result.Any(e => e.Kind == TrackEffectKind.Vst && e.VstSlot == slot.SlotIndex))
                    result.Add(new UTrackEffect { Id = "vst-" + slot.SlotIndex, Kind = TrackEffectKind.Vst, VstSlot = slot.SlotIndex });
            }
            return result.Where(e => e.Kind != TrackEffectKind.Vst ||
                (e.VstSlot >= 0 && e.VstSlot < track.VstSlots.Count && track.VstSlots[e.VstSlot].IsLoaded)).ToArray();
        }

        public static UMixFx Isolate(UMixFx fx, TrackEffectKind kind) {
            var copy = fx.Clone();
            copy.EqEnabled = kind == TrackEffectKind.Eq && fx.EqEnabled;
            copy.CompEnabled = kind == TrackEffectKind.Compressor && fx.CompEnabled;
            copy.ReverbEnabled = kind == TrackEffectKind.Reverb && fx.ReverbEnabled;
            return copy;
        }

        public static UTrackEffect? Find(UTrack track, string id) {
            var rack = track.EffectRack;
            if (rack != null) {
                for (int i = 0; i < rack.Count; i++) if (rack[i].Id == id) return rack[i];
                return null;
            }
            foreach (var entry in Read(track)) if (entry.Id == id) return entry;
            return null;
        }

        public static bool IsPowered(UTrack track, UTrackEffect effect) => effect.Kind == TrackEffectKind.Vst
            ? !track.VstSlots[effect.VstSlot].Bypassed
            : effect.Settings?.Enabled == true && (effect.Kind switch {
                TrackEffectKind.Eq => effect.Settings.EqEnabled,
                TrackEffectKind.Compressor => effect.Settings.CompEnabled,
                TrackEffectKind.Reverb => effect.Settings.ReverbEnabled,
                _ => false,
            });
    }
}

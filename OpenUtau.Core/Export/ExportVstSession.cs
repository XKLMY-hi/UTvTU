using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Vst;

namespace OpenUtau.Core.Export {
    internal sealed record ExportVstSnapshot(int TrackIndex, int SlotIndex, string Uid, byte[]? State, VstExportParameter[] Parameters);

    // These instances never enter the playback registry. A job owns and retires every handle.
    internal sealed class ExportVstSession : IDisposable {
        readonly List<VstEffect> owned = new();
        internal IReadOnlyDictionary<int, VstEffect[]> Effects { get; }

        internal static ExportVstSnapshot[] Capture(ExportDeliveryPlan plan) {
            if (!plan.Audio.VstEffects) return Array.Empty<ExportVstSnapshot>();
            var snapshots = new List<ExportVstSnapshot>();
            foreach (var target in plan.Targets) {
                foreach (var slot in target.Track.VstSlots?.Where(s => s.IsLoaded && !s.Bypassed).OrderBy(s => s.SlotIndex) ?? Enumerable.Empty<VstPluginSlot>()) {
                    byte[]? state = slot.StateData?.ToArray();
                    var parameters = Array.Empty<VstExportParameter>();
                    var live = VstPluginManager.Inst.GetEffect(target.Index, slot.SlotIndex);
                    if (live?.IsLoaded == true && live.Slot.PluginUid == slot.PluginUid) {
                        var captured = live.CaptureExportState();
                        state = captured.State.ToArray();
                        parameters = captured.Parameters.ToArray();
                    }
                    snapshots.Add(new ExportVstSnapshot(target.Index, slot.SlotIndex, slot.PluginUid, state, parameters));
                }
            }
            return snapshots.ToArray();
        }

        internal ExportVstSession(ExportVstSnapshot[] snapshots, ExportAudioOptions options, TimeAxis clock,
            int resolution, IVstBridge bridge, CancellationToken token) {
            var tracks = new Dictionary<int, List<VstEffect>>();
            try {
                foreach (var snapshot in snapshots.Where(s => options.TrackIndices == null || options.TrackIndices.Contains(s.TrackIndex))) {
                    token.ThrowIfCancellationRequested();
                    var slot = new VstPluginSlot(snapshot.SlotIndex) { PluginUid = snapshot.Uid };
                    var effect = new VstEffect(slot, bridge);
                    owned.Add(effect);
                    effect.Load();
                    if (snapshot.State?.Length > 0 && !effect.RestoreState(snapshot.State))
                        throw new InvalidOperationException($"Could not restore '{effect.DisplayName}' state for export.");
                    effect.SetupForExport(AudioSettings.SampleRate, AudioSettings.BlockSize,
                        options.ProcessingMode == ExportProcessingMode.Offline, snapshot.Parameters, position => {
                            double tick = clock.MsPosToNonExactTickPos(position * 1000d / AudioSettings.SampleRate);
                            var signature = clock.TimeSignatureAtTick((int)Math.Floor(tick));
                            return new VstExportContext(position, tick / resolution, clock.GetBpmAtTick((int)Math.Floor(tick)), signature.beatPerBar, signature.beatUnit);
                        });
                    if (!tracks.TryGetValue(snapshot.TrackIndex, out var effects)) tracks.Add(snapshot.TrackIndex, effects = new());
                    effects.Add(effect);
                }
                Effects = tracks.ToDictionary(t => t.Key, t => t.Value.ToArray());
            } catch { Dispose(); throw; }
        }

        public void Dispose() {
            for (int i = owned.Count - 1; i >= 0; i--) {
                try { owned[i].Dispose(); }
                catch (Exception ex) { Serilog.Log.Warning(ex, "Could not dispose export VST instance"); }
            }
            owned.Clear();
        }
    }
}

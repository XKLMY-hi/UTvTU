using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.SignalChain.Effects;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Vst;
using Xunit;

namespace OpenUtau.Test.Core.SignalChain;

[Collection("VstShared")]
public class UnifiedEffectRackTests {
    sealed class Tone : ISignalSource {
        public bool IsReady(int position, int count) => true;
        public int Mix(int position, float[] buffer, int index, int count) {
            for (int i = 0; i < count; i++) buffer[index + i] += .2f * (float)Math.Sin((position + i) / AudioSettings.Channels * 2 * Math.PI * 4000 / AudioSettings.SampleRate);
            return position + count;
        }
    }
    sealed class Offset : IEffect {
        readonly float amount;
        public Offset(float amount) => this.amount = amount;
        public bool IsBypassed => false;
        public void Process(float[] buffer, int offset, int count) { for (int i = offset; i < offset + count; i++) buffer[i] += amount; }
        public void Reset() { }
    }
    static float[] Render(ISignalSource source, int position = 0) {
        var samples = new float[4096]; source.Mix(position, samples, 0, samples.Length); return samples;
    }
    static UTrack Legacy() => new() { MixFx = new UMixFx { Enabled = true, EqHighDb = 5, CompRatio = 3, ReverbWet = .4 },
        VstSlots = new() { new(0) { PluginUid = "missing:test", Bypassed = true, StateData = new byte[] { 1, 2 } } } };

    [Fact]
    public void LegacyReadDoesNotMutateAndKeepsDisabledStagesAndUnknownPluginState() {
        var track = Legacy(); track.MixFx.CompEnabled = false;
        var entries = TrackEffectRack.Read(track);
        Assert.Null(track.EffectRack);
        Assert.Equal(new[] { TrackEffectKind.Eq, TrackEffectKind.Compressor, TrackEffectKind.Reverb, TrackEffectKind.Vst }, entries.Select(e => e.Kind));
        Assert.False(TrackEffectRack.IsPowered(track, entries[1]));
        Assert.False(TrackEffectRack.IsPowered(track, entries[3]));
        Assert.Equal(new byte[] { 1, 2 }, track.VstSlots[0].StateData);
    }

    [Fact]
    public void LegacyMigrationProducesTheSameSamplesIncludingReverb() {
        var track = Legacy();
        var old = MixFxSource.WrapWith(new Tone(), track.MixFx);
        var next = TrackRackSource.Snapshot(new Tone(), TrackEffectRack.Read(track), new Dictionary<int, IEffect>());
        Assert.Equal(Render(old), Render(next));
    }

    [Fact]
    public void GlobalLegacyBypassRemainsDryAfterMigration() {
        var track = Legacy(); track.MixFx.Enabled = false;
        Assert.Equal(Render(new Tone()), Render(TrackRackSource.Snapshot(new Tone(), TrackEffectRack.Read(track), new Dictionary<int, IEffect>())));
    }

    [Fact]
    public void DuplicateBuiltInsHaveIndependentIdentityParametersAndUndo() {
        var track = new UTrack();
        TrackRackCommands.Add(track, TrackEffectKind.Eq).Execute();
        TrackRackCommands.Add(track, TrackEffectKind.Eq).Execute();
        var first = track.EffectRack![0]; var second = track.EffectRack[1];
        Assert.NotEqual(first.Id, second.Id);
        var settings = first.Settings!.Clone(); settings.EqLowDb = 9;
        var edit = TrackRackCommands.Settings(track, first.Id, settings); edit.Execute();
        Assert.Equal(9, track.EffectRack[0].Settings!.EqLowDb);
        Assert.Equal(0, track.EffectRack[1].Settings!.EqLowDb);
        edit.Unexecute(); Assert.Equal(0, track.EffectRack[0].Settings!.EqLowDb);
    }

    [Fact]
    public void MoveAcrossVstBoundaryChangesRackOrderWithoutChangingSlotOrState() {
        var track = Legacy();
        var oldSlot = track.VstSlots[0]; var state = oldSlot.StateData;
        string id = TrackEffectRack.Read(track)[0].Id;
        var move = TrackRackCommands.Move(track, id, 3); move.Execute();
        Assert.Equal(id, track.EffectRack![3].Id);
        Assert.Same(oldSlot, track.VstSlots[0]); Assert.Same(state, oldSlot.StateData);
        move.Unexecute(); Assert.Null(track.EffectRack);
        move.Execute(); Assert.Equal(id, track.EffectRack![3].Id);
    }

    [Fact]
    public void RemovingBuiltInAndUndoRestoresTheExactLegacyModel() {
        var track = Legacy(); var original = track.MixFx;
        var remove = TrackRackCommands.Remove(track, TrackEffectRack.Read(track)[1].Id);
        remove.Execute(); Assert.Equal(3, TrackEffectRack.Read(track).Count);
        remove.Unexecute(); Assert.Null(track.EffectRack); Assert.Same(original, track.MixFx);
    }

    [Fact]
    public void RackYamlRoundTripKeepsOrderIdsAndDuplicateSettings() {
        var track = Legacy(); TrackRackCommands.Add(track, TrackEffectKind.Compressor).Execute();
        TrackRackCommands.Move(track, track.EffectRack![3].Id, -2).Execute();
        string yaml = Yaml.DefaultSerializer.Serialize(track);
        var loaded = Yaml.DefaultDeserializer.Deserialize<UTrack>(yaml);
        Assert.Equal(track.EffectRack.Select(e => e.Id), loaded.EffectRack!.Select(e => e.Id));
        Assert.Equal(track.EffectRack.Select(e => e.Kind), loaded.EffectRack.Select(e => e.Kind));
        Assert.Equal(track.EffectRack[4].Settings!.CompRatio, loaded.EffectRack[4].Settings!.CompRatio);
        Assert.Equal(track.VstSlots[0].StateData, loaded.VstSlots[0].StateData);
    }

    [Fact]
    public void SnapshotRespectsInterleavingAndIndependentExportSwitches() {
        var track = new UTrack(); TrackRackCommands.Add(track, TrackEffectKind.Compressor).Execute();
        var comp = track.EffectRack![0]; comp.Settings!.CompMakeupDb = 6;
        var vst = new UTrackEffect { Kind = TrackEffectKind.Vst, VstSlot = 2 };
        var map = new Dictionary<int, IEffect> { [2] = new Offset(.3f) };
        var before = Render(TrackRackSource.Snapshot(new Tone(), new[] { vst, comp }, map));
        var after = Render(TrackRackSource.Snapshot(new Tone(), new[] { comp, vst }, map));
        Assert.True(before.Zip(after).Max(p => Math.Abs(p.First - p.Second)) > .01);
        Assert.Equal(Render(new Tone()), Render(TrackRackSource.Snapshot(new Tone(), new[] { vst, comp }, map, false, false)));
        Assert.Equal(Render(MixFxSource.WrapWith(new Tone(), comp.Settings)), Render(TrackRackSource.Snapshot(new Tone(), new[] { vst, comp }, map, true, false)));
    }

    [Fact]
    public void LiveRackAdoptsAddedRemovedAndEditedEffectsWithoutRestartOrSteadyAllocations() {
        var track = new UTrack(); var live = new TrackRackSource(new Tone(), track);
        var dry = Render(live);
        TrackRackCommands.Add(track, TrackEffectKind.Compressor).Execute();
        string id = track.EffectRack![0].Id;
        var settings = track.EffectRack[0].Settings!.Clone(); settings.CompMakeupDb = 12;
        TrackRackCommands.Settings(track, id, settings).Execute();
        var wet = Render(live, 4096);
        Assert.True(wet.Zip(dry).Max(p => Math.Abs(p.First - p.Second)) > .05);
        var buffer = new float[4096]; int position = 8192;
        for (int i = 0; i < 4; i++) position = live.Mix(position, buffer, 0, buffer.Length);
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 4; i++) position = live.Mix(position, buffer, 0, buffer.Length);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - start);
        TrackRackCommands.Remove(track, id).Execute();
        Assert.Equal(Render(new Tone(), position), Render(live, position));
    }

    [Fact]
    public void MakingAnAddVstCommandDoesNotAllocateASlotInTheProject() {
        var track = new UTrack(); _ = TrackRackCommands.AssignVst(track, 0, "missing:test");
        Assert.Empty(track.VstSlots); Assert.Null(track.EffectRack);
    }

    [Fact]
    public void LiveVstMovesWithoutReloadAndBypassStopsItsProcessing() {
        VstTestSetup.RunExclusive(() => {
            const string uid = "test:unified-rack-live";
            VstTestSetup.Register(VstTestSetup.MakeEntry(uid));
            var manager = VstPluginManager.Inst; var previous = manager.Bridge;
            int loads = 0, calls = 0;
            manager.Bridge = new FakeVstBridge { LoadCallback = () => loads++, ProcessCallback = (_, _) => calls++, EditorCallback = () => true };
            var track = new UTrack { TrackNo = VstTestSetup.NextTrackNo(), VstSlots = new() { new(0) { PluginUid = uid } } };
            try {
                var instance = manager.LoadEffect(track.TrackNo, track.VstSlots[0]);
                Assert.NotNull(instance);
                TrackRackCommands.Add(track, TrackEffectKind.Eq).Execute();
                var source = new TrackRackSource(new Tone(), track); Render(source);
                int processed = calls; Assert.True(processed > 0);
                string id = track.EffectRack![0].Id;
                var move = TrackRackCommands.Move(track, id, 1); move.Execute();
                Assert.Same(instance, manager.GetEffect(track.TrackNo, 0)); Assert.Equal(1, loads);
                var bypass = TrackRackCommands.Power(track, id, false); bypass.Execute(); Render(source, 4096);
                Assert.Equal(processed, calls);
                bypass.Unexecute(); Render(source, 8192); Assert.True(calls > processed);
                move.Unexecute(); Assert.Same(instance, manager.GetEffect(track.TrackNo, 0)); Assert.Equal(1, loads);
                Assert.True(instance!.OpenNativeEditorAsync().GetAwaiter().GetResult());
            } finally { manager.RemoveTrack(track.TrackNo); manager.Bridge = previous; }
        });
    }
}

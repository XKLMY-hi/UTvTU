using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core {
    public enum TrackMixProperty { Volume, Pan, Mute, Solo, Effects }

    /// <summary>A reversible live mix edit. Views observe it without writing the model again.</summary>
    public sealed class TrackMixCommand : UCommand {
        public readonly IReadOnlyList<UTrack> Tracks;
        public readonly TrackMixProperty Property;
        readonly Action apply;
        readonly Action revert;
        readonly string description;

        public TrackMixCommand(IEnumerable<UTrack> tracks, TrackMixProperty property,
            Action apply, Action revert, string description) {
            Tracks = tracks.ToArray();
            Property = property;
            this.apply = apply;
            this.revert = revert;
            this.description = description;
        }

        public override bool AffectsSynthesis => false;
        public override void Execute() => apply();
        public override void Unexecute() => revert();
        public override bool CanMerge(IList<UCommand> commands) =>
            Property is TrackMixProperty.Volume or TrackMixProperty.Pan or TrackMixProperty.Effects &&
            commands.All(c => c is TrackMixCommand mix && mix.Property == Property &&
                mix.Tracks.SequenceEqual(Tracks));
        public override UCommand Merge(IList<UCommand> commands) =>
            new TrackMixCommand(Tracks, Property, apply, ((TrackMixCommand)commands[0]).revert, description);
        public override string ToString() => description;
    }

    public sealed class TrackMixChangedNotification : UNotification {
        public readonly IReadOnlyList<UTrack> Tracks;
        public TrackMixChangedNotification(UProject project, IReadOnlyList<UTrack> tracks) {
            this.project = project;
            Tracks = tracks;
        }
    }

    public sealed class CommandHistoryChangedNotification : UNotification { }
}

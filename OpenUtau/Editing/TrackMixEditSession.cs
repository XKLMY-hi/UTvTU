using System;
using System.Linq;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;

namespace OpenUtau.App.Editing {
    /// <summary>Owns mix edits for one track in one document; stale views cannot edit a new document.</summary>
    public sealed class TrackMixEditSession : IDisposable {
        readonly DocManager doc;
        readonly UProject? project;
        readonly UTrack track;
        bool gesture;
        bool disposed;
        UCommandGroup? ownedGroup;

        public TrackMixEditSession(UTrack track) {
            doc = DocManager.Inst;
            this.track = track;
            project = doc.Project.tracks.Contains(track) ? doc.Project : null;
        }

        public bool IsCurrent => !disposed && (project == null ||
            (ReferenceEquals(project, doc.Project) && project.tracks.Contains(track)));

        public void BeginGesture() {
            if (disposed) return;
            EndGesture();
            gesture = true;
        }

        public void EndGesture() {
            gesture = false;
            if (ownedGroup != null && ReferenceEquals(ownedGroup, doc.ActiveUndoGroup) && IsCurrent) {
                doc.EndUndoGroup();
            }
            ownedGroup = null;
        }

        public bool SetVolume(double db) {
            if (!double.IsFinite(db)) return false;
            db = Math.Clamp(db, -24, 12);
            return track.Volume != db && Execute(() => TrackMixCommands.Volume(track, db));
        }

        public bool SetPan(double percent) {
            if (!double.IsFinite(percent)) return false;
            double pan = Math.Clamp(percent, -100, 100) / 100;
            return track.Pan != pan && Execute(() => TrackMixCommands.Pan(track, pan));
        }

        public bool SetMute(bool mute) => track.Mute != mute && Execute(() => TrackMixCommands.Mute(track, mute));
        public bool SetSolo(bool solo, bool additionally = false) =>
            track.Solo != solo && Execute(() => TrackMixCommands.Solo(track, solo, additionally));
        public bool SetEffects(UMixFx? effects) =>
            !ReferenceEquals(track.MixFx, effects) && Execute(() => TrackMixCommands.SetMixFx(track, effects));

        public bool SetMutes(Func<UTrack, bool> value) {
            var tracks = project?.tracks.ToArray() ?? new[] { track };
            return tracks.Any(t => t.Mute != value(t)) &&
                Execute(() => TrackMixCommands.MuteTracks(tracks, value));
        }

        public bool UnsoloAll() {
            var tracks = project?.tracks.ToArray() ?? new[] { track };
            return tracks.Any(t => t.Solo) && Execute(() => TrackMixCommands.UnsoloTracks(tracks));
        }

        bool Execute(Func<UCommand> makeCommand) {
            if (!IsCurrent) return false;
            // Standalone controls / preset previews have no document history to modify.
            if (project == null) {
                makeCommand().Execute();
                return true;
            }
            if (ownedGroup == null || !ReferenceEquals(ownedGroup, doc.ActiveUndoGroup)) {
                doc.StartUndoGroup("command.track.setting");
                ownedGroup = doc.ActiveUndoGroup;
            }
            try {
                doc.ExecuteCmd(makeCommand());
            } finally {
                if (!gesture) EndGesture();
            }
            return true;
        }

        public void Dispose() {
            EndGesture();
            disposed = true;
        }
    }
}

using System.Linq;
using OpenUtau.App.Commands;
using OpenUtau.App.Editing;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;

namespace OpenUtau.App.ViewModels {
    public partial class MainWindowViewModel : IWorkspaceEditingCommands {
        void IWorkspaceEditingCommands.SelectAllParts() => TracksViewModel.SelectAllParts();
        void IWorkspaceEditingCommands.CutParts() => TracksViewModel.CutParts();
        void IWorkspaceEditingCommands.CopyParts() => TracksViewModel.CopyParts();
        void IWorkspaceEditingCommands.PasteParts() => TracksViewModel.PasteParts();
        void IWorkspaceEditingCommands.DeleteSelectedParts() => TracksViewModel.DeleteSelectedParts();
        void IWorkspaceEditingCommands.SwitchTo(AppSurface surface) => ViewSwitcher.SwitchTo(surface);
        void IWorkspaceEditingCommands.MovePlayPosToStart() => PlaybackViewModel.MovePlayPos(0);

        void IWorkspaceEditingCommands.MovePlayPosToEnd() {
            if (TracksViewModel.Parts.Count > 0) {
                PlaybackViewModel.MovePlayPos(TracksViewModel.Parts.Max(part => part.End));
            }
        }

        UTrack? SelectedPartTrack() {
            if (disposed) return null;
            var project = DocManager.Inst.Project;
            var part = TracksViewModel.SelectedParts.FirstOrDefault();
            return part != null && project.parts.Contains(part) &&
                part.trackNo >= 0 && part.trackNo < project.tracks.Count
                ? project.tracks[part.trackNo] : null;
        }

        void IWorkspaceEditingCommands.SoloSelectedPart() {
            if (SelectedPartTrack() is not { } track) return;
            using var edits = new TrackMixEditSession(track);
            edits.SetSolo(!track.Solo);
        }

        void IWorkspaceEditingCommands.MuteSelectedPart() {
            if (SelectedPartTrack() is not { } track) return;
            using var edits = new TrackMixEditSession(track);
            edits.SetMute(!track.Mute);
        }
    }
}

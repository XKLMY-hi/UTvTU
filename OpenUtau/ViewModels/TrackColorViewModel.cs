using System.Collections.ObjectModel;
using System.Linq;
using OpenUtau.App.Editing;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI.Fody.Helpers;

namespace OpenUtau.App.ViewModels {
    public class TrackColorViewModel : ViewModelBase {
        public ObservableCollection<TrackColor> TrackColors { get; } = new ObservableCollection<TrackColor>(ThemeManager.TrackColors);
        [Reactive] public TrackColor SelectedColor { get; set; }
        private UTrack track;
        readonly UProject project;

        public TrackColorViewModel(UTrack track) {
            SelectedColor = TrackColors.FirstOrDefault(c => c.Name == track.TrackColor) ?? TrackColors.First(c => c.Name == "Blue");
            this.track = track;
            project = DocManager.Inst.Project;
        }

        public void Finish() {
            if (ReferenceEquals(project, DocManager.Inst.Project) && project.tracks.Contains(track) &&
                SelectedColor.Name != track.TrackColor) {
                using var edits = new DocumentEditGesture(project, "command.track.setting");
                edits.Execute(new ChangeTrackColorCommand(project, track, SelectedColor.Name));
            }
        }
    }
}

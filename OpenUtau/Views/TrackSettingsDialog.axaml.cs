using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.App.Controls;

namespace OpenUtau.App.Views {
    public partial class TrackSettingsDialog : WorkspaceToolView {

        TrackSettingsViewModel viewModel;

        public TrackSettingsDialog() : this(new UTrack(DocManager.Inst.Project)) { }

        public TrackSettingsDialog(UTrack track) {
            InitializeComponent();
            DataContext = viewModel = new TrackSettingsViewModel(track);
        }

        public void OnOkClicked(object sender, RoutedEventArgs e) {
            viewModel.Finish();
            Close();
        }
    }
}

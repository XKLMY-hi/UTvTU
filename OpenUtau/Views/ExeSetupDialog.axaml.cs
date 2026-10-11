using Avalonia.Controls;
using Avalonia.Interactivity;
using Classic;
using OpenUtau.App.ViewModels;
using OpenUtau.App.Controls;

namespace OpenUtau.App.Views {
    public partial class ExeSetupDialog : WorkspaceToolView {
        public ExeSetupDialog() {
            InitializeComponent();
            Width = 600;
        }

        public void InstallAsResampler(object sender, RoutedEventArgs arg) {
            var viewModel = DataContext as ExeSetupViewModel;
            if (viewModel == null) {
                return;
            }
            ExeInstaller.Install(viewModel.filePath, ExeType.resampler);
            Close();
        }

        public void InstallAsWavtool(object sender, RoutedEventArgs arg) {
            var viewModel = DataContext as ExeSetupViewModel;
            if (viewModel == null) {
                return;
            }
            ExeInstaller.Install(viewModel.filePath, ExeType.wavtool);
            Close();
        }
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;
using OpenUtau.App.Controls;

namespace OpenUtau.App.Views {
    public partial class PhoneticAssistant : WorkspaceToolView {
        PhoneticAssistantViewModel viewModel;
        public PhoneticAssistant() {
            InitializeComponent();
            Width = 400;
            DataContext = viewModel = new PhoneticAssistantViewModel();
        }

        public void OnCopy(object sender, RoutedEventArgs e) {
            var data = new Avalonia.Input.DataTransfer();
            data.Add(Avalonia.Input.DataTransferItem.CreateText(viewModel.Phonemes));
            _ = WindowHost.Clipboard?.SetDataAsync(data);
        }
    }
}

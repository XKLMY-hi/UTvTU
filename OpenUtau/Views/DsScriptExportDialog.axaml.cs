using Avalonia.Controls;
using Avalonia.Input;
using OpenUtau.App.Controls;
using Avalonia.Interactivity;

namespace OpenUtau.App.Views {
    public partial class DsScriptExportDialog : WorkspaceToolView {
        public bool Confirmed { get; private set; }

        public DsScriptExportDialog() {
            InitializeComponent();
            Width = 380;
        }

        void OnOkClicked(object? sender, RoutedEventArgs e) {
            Confirmed = true;
            Close();
        }

        void OnCancelClicked(object? sender, RoutedEventArgs e) {
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            if (e.Key == Key.Escape) {
                e.Handled = true;
                Close();
            } else if (e.Key == Key.Return) {
                e.Handled = true;
                Confirmed = true;
                Close();
            } else {
                base.OnKeyDown(e);
            }
        }
    }
}

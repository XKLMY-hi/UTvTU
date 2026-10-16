using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using OpenUtau.App.Controls;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Analysis;

namespace OpenUtau.App.Views {
    public partial class TranscribeDialog : WorkspaceToolView {
        public bool Confirmed { get; private set; }

        public TranscribeDialog() {
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

        async void OnInstallModel(object? sender, RoutedEventArgs e) {
            if (sender is Avalonia.Controls.Button { Tag: string id } && WindowHost is MainWindow main) {
                Close();
                await main.OfferRequiredPackages(new[] { id });
            }
        }

        void OnRmvpeRowPressed(object? sender, PointerPressedEventArgs e) {
            e.Handled = true;
            _ = HandleRmvpeRowClick();
        }

        async System.Threading.Tasks.Task HandleRmvpeRowClick() {
            var vm = DataContext as TranscribeViewModel;
            if (vm == null) {
                return;
            }
            if (!vm.RmvpeAvailable) {
                if (RmvpeCheck != null) {
                    RmvpeCheck.IsChecked = false;
                }
                if (WindowHost is MainWindow main) {
                    Close();
                    await main.OfferRequiredPackages(new[] { "rmvpe" });
                }
                return;
            }
            var current = vm.PredictPitd;
            vm.PredictPitd = !current;
            if (RmvpeCheck != null) {
                RmvpeCheck.IsChecked = !current;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            if (e.Key == Key.Escape) {
                e.Handled = true;
                Close();
            } else if (e.Key == Key.Return) {
                var vm = DataContext as TranscribeViewModel;
                if (vm?.CanRun == true) {
                    e.Handled = true;
                    Confirmed = true;
                    Close();
                }
            } else {
                base.OnKeyDown(e);
            }
        }
    }
}

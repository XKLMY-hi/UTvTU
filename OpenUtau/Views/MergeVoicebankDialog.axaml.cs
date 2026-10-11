using Serilog;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;
using OpenUtau.App.Controls;

namespace OpenUtau.App.Views {
    public partial class MergeVoicebankDialog : WorkspaceToolView {
        public MergeVoicebankDialog() {
            InitializeComponent();
            Width = 600;
        }

        void OnEditedFolder(object sender, DataGridCellEditEndedEventArgs e)
        {
            var viewModel = DataContext as MergeVoicebankViewModel;
            if (viewModel == null)
            {
                return;
            }
            viewModel.CheckFolderNames();
        }

        void OnEditedSubbank(object sender, DataGridCellEditEndedEventArgs e)
        {
            var viewModel = DataContext as MergeVoicebankViewModel;
            if (viewModel == null)
            {
                return;
            }
            viewModel.CheckSubbankNames();
        }

        void OnEditedVoiceColor(object sender, DataGridCellEditEndedEventArgs e)
        {
            var viewModel = DataContext as MergeVoicebankViewModel;
            if (viewModel == null)
            {
                return;
            }
            viewModel.CheckVoiceColorNames();
        }

        void MergeClicked(object sender, RoutedEventArgs arg) {
            var viewModel = DataContext as MergeVoicebankViewModel;
            if (viewModel == null) {
                return;
            }
            var scheduler = TaskScheduler.FromCurrentSynchronizationContext();
            var task = viewModel.Merge();
            task.ContinueWith((task) => {
                if (task.IsFaulted) {
                    Log.Error(task.Exception, "Failed to merge singer");
                    if (WindowHost is Window window && window.IsVisible) {
                        MessageBox.ShowError(window, task.Exception);
                    }
                }
            }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, scheduler);
            Close();
        }
    }
}

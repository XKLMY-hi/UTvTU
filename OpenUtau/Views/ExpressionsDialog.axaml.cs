using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;
using OpenUtau.Core.Ustx;
using OpenUtau.App.Controls;
using Avalonia;
using OpenUtau.Core;

namespace OpenUtau.App.Views {
    public partial class ExpressionsDialog : WorkspaceToolView, ICmdSubscriber {
        bool applying;
        public ExpressionsDialog() {
            InitializeComponent();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            DocManager.Inst.AddSubscriber(this);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            DocManager.Inst.RemoveSubscriber(this);
            base.OnDetachedFromVisualTree(e);
        }

        public void OnNext(UCommand cmd, bool isUndo) {
            var model = DataContext as ExpressionsViewModel;
            if (cmd is LoadProjectNotification ||
                (model?.Track != null && !DocManager.Inst.Project.tracks.Contains(model.Track))) {
                Close();
            } else if (cmd is ConfigureExpressionsCommand && !applying) {
                // Graph import and undo may add descriptors. Keep the form current so
                // its next Apply cannot delete expressions imported through the graph tab.
                DataContext = new ExpressionsViewModel(model?.Track);
            }
        }

        private void ApplyButtonClicked(object sender, RoutedEventArgs _) {
            applying = true;
            try {
                (DataContext as ExpressionsViewModel)?.Apply();
            } catch (Exception e) {
                MessageBox.ShowError(WindowHost, e);
            } finally {
                applying = false;
            }
        }

        private void AddButtonClicked(object sender, RoutedEventArgs _) {
            var button = (Button)sender;
            var vm = DataContext as ExpressionsViewModel;
            if (vm != null) {
                vm.Add();
                if (vm.IsTrackOverride) {
                    button.ContextMenu?.Open();
                }
            }
        }
    }
}

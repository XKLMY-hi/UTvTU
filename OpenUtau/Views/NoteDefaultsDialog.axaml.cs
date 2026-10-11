using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;
using OpenUtau.Core.Util;
using OpenUtau.App.Controls;

namespace OpenUtau.App.Views {
    public partial class NoteDefaultsDialog : WorkspaceToolView {
        internal readonly NoteDefaultsViewModel ViewModel;
        public NoteDefaultsDialog() {
            InitializeComponent();
            Width = 600;
            DataContext = ViewModel = new NoteDefaultsViewModel();
        }

        void OnSavePortamentoPreset(object sender, RoutedEventArgs e) {
            var dialog = new TypeInDialog() {
                Title = ThemeManager.GetString("notedefaults.preset.namenew"),
                onFinish = name => ViewModel.SavePortamentoPreset(name),
            };
            dialog.ShowDialog(this);
        }

        void OnRemovePortamentoPreset(object sender, RoutedEventArgs e) {
            ViewModel.RemoveAppliedPortamentoPreset();
        }

        void OnSaveVibratoPreset(object sender, RoutedEventArgs e) {
            var dialog = new TypeInDialog() {
                Title = ThemeManager.GetString("notedefaults.preset.namenew"),
                onFinish = name => ViewModel.SaveVibratoPreset(name),
            };
            dialog.ShowDialog(this);
        }

        void OnRemoveVibratoPreset(object sender, RoutedEventArgs e) {
            ViewModel.RemoveAppliedVibratoPreset();
        }

        void OnReset(object sender, RoutedEventArgs e) {
            NotePresets.Reset();
            ViewModel.ResetSettings();
        }
    }
}

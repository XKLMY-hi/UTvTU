using System.Threading.Tasks;
using Avalonia.Interactivity;
using OpenUtau.App.Commands;

namespace OpenUtau.App.Views {
    public partial class MainWindow : IWorkspaceWindowCommands {
        void IWorkspaceWindowCommands.NewProject() => NewProject();
        void IWorkspaceWindowCommands.Open() => Open();
        Task IWorkspaceWindowCommands.SaveAs() => SaveAs();
        void IWorkspaceWindowCommands.Render() => OnMenuRender(this, new RoutedEventArgs());
        void IWorkspaceWindowCommands.ToggleMixer() => OnMenuMixer(this, new RoutedEventArgs());
        void IWorkspaceWindowCommands.ToggleMixerWindow() => ToggleMixerWindow();
        void IWorkspaceWindowCommands.ToggleFullScreen() => OnMenuFullScreen(this, new RoutedEventArgs());
        void IWorkspaceWindowCommands.ShowShortcutOverview() => ShortcutOverviewWindow.Open(this);
        void IWorkspaceWindowCommands.PlayOrPause() => PlayOrPause();
        void IWorkspaceWindowCommands.CloseOverlay() => CloseOverlay();
        void IWorkspaceWindowCommands.QuitApplication() => QuitApplication();
    }
}

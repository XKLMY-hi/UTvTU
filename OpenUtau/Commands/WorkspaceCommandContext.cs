using System.Threading.Tasks;
using OpenUtau.App.ViewModels;

namespace OpenUtau.App.Commands {
    /// <summary>Document and navigation operations; no window or routed events are required.</summary>
    public interface IWorkspaceEditingCommands {
        bool CanUndo { get; }
        bool CanRedo { get; }
        void Undo();
        void Redo();
        void SelectAllParts();
        void CutParts();
        void CopyParts();
        void PasteParts();
        void DeleteSelectedParts();
        void SoloSelectedPart();
        void MuteSelectedPart();
        void SwitchTo(AppSurface surface);
        void MovePlayPosToStart();
        void MovePlayPosToEnd();
    }

    /// <summary>Operations requiring a file picker, window host or current playback focus.</summary>
    public interface IWorkspaceWindowCommands {
        void NewProject();
        void Open();
        Task Save();
        Task SaveAs();
        void Render();
        void ToggleMixer();
        void ToggleMixerWindow();
        void ToggleFullScreen();
        void ShowShortcutOverview();
        void PlayOrPause();
        void CloseOverlay();
        void QuitApplication();
    }

    public sealed record WorkspaceCommandContext(
        IWorkspaceEditingCommands Editing, IWorkspaceWindowCommands Window) {
        public bool TryExecute(CommandDefinition command) {
            if (command.CanExecute?.Invoke(this) == false) return false;
            command.Execute(this);
            return true;
        }
    }
}

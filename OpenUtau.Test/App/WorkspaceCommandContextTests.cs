using System.Collections.Generic;
using System.Threading.Tasks;
using OpenUtau.App.Commands;
using OpenUtau.App.ViewModels;
using Xunit;

namespace OpenUtau.Test.App {
    public class WorkspaceCommandContextTests {
        // These adapters exercise the production registry without creating a window or audio engine.
        sealed class Operations : IWorkspaceEditingCommands, IWorkspaceWindowCommands {
            public readonly List<string> Calls = new();
            public bool CanUndo { get; set; } = true;
            public bool CanRedo { get; set; } = true;
            public void Undo() => Calls.Add("undo");
            public void Redo() => Calls.Add("redo");
            public void SelectAllParts() => Calls.Add("selectall");
            public void CutParts() => Calls.Add("cut");
            public void CopyParts() => Calls.Add("copy");
            public void PasteParts() => Calls.Add("paste");
            public void DeleteSelectedParts() => Calls.Add("delete");
            public void SoloSelectedPart() => Calls.Add("solo");
            public void MuteSelectedPart() => Calls.Add("mute");
            public void SwitchTo(AppSurface surface) => Calls.Add(surface.ToString());
            public void MovePlayPosToStart() => Calls.Add("home");
            public void MovePlayPosToEnd() => Calls.Add("end");
            public void NewProject() => Calls.Add("new");
            public void Open() => Calls.Add("open");
            public Task Save() { Calls.Add("save"); return Task.CompletedTask; }
            public Task SaveAs() { Calls.Add("saveas"); return Task.CompletedTask; }
            public void Render() => Calls.Add("render");
            public void ToggleMixer() => Calls.Add("mixer");
            public void ToggleMixerWindow() => Calls.Add("detach");
            public void ToggleFullScreen() => Calls.Add("fullscreen");
            public void ShowShortcutOverview() => Calls.Add("shortcuts");
            public void PlayOrPause() => Calls.Add("play");
            public void CloseOverlay() => Calls.Add("overlay");
            public void QuitApplication() => Calls.Add("quit");
        }

        [Theory]
        [InlineData("file.new", "new")]
        [InlineData("file.open", "open")]
        [InlineData("file.save", "save")]
        [InlineData("file.saveas", "saveas")]
        [InlineData("file.render", "render")]
        [InlineData("edit.undo", "undo")]
        [InlineData("edit.redo", "redo")]
        [InlineData("edit.redo.alt", "redo")]
        [InlineData("edit.selectallparts", "selectall")]
        [InlineData("edit.cutparts", "cut")]
        [InlineData("edit.copyparts", "copy")]
        [InlineData("edit.pasteparts", "paste")]
        [InlineData("edit.deleteparts", "delete")]
        [InlineData("track.solopart", "solo")]
        [InlineData("track.mutepart", "mute")]
        [InlineData("tools.mixer", "mixer")]
        [InlineData("tools.mixerattach", "detach")]
        [InlineData("tools.fullscreen", "fullscreen")]
        [InlineData("tools.shortcutoverview", "shortcuts")]
        [InlineData("view.workspace", "Workspace")]
        [InlineData("view.pianoroll", "PianoRoll")]
        [InlineData("view.mixer", "Mixer")]
        [InlineData("playback.playpause", "play")]
        [InlineData("playback.gohome", "home")]
        [InlineData("playback.goend", "end")]
        [InlineData("window.escape", "overlay")]
        [InlineData("window.quit", "quit")]
        public void RegistryDispatchesToTheExplicitCapability(string id, string operation) {
            var editing = new Operations();
            var window = new Operations();
            var context = new WorkspaceCommandContext(editing, window);
            Assert.True(context.TryExecute(CommandRegistry.Find(id)!));
            var documentCommand = id.StartsWith("edit.") || id.StartsWith("track.") ||
                id.StartsWith("view.") || id is "playback.gohome" or "playback.goend";
            Assert.Equal(new[] { operation }, documentCommand ? editing.Calls : window.Calls);
            Assert.Empty(documentCommand ? window.Calls : editing.Calls);
        }

        [Theory]
        [InlineData("edit.undo")]
        [InlineData("edit.redo")]
        [InlineData("edit.redo.alt")]
        public void DisabledHistoryIsNotExecuted_AndAvailabilityIsReadAtDispatch(string id) {
            var editing = new Operations { CanUndo = false, CanRedo = false };
            var window = new Operations();
            var context = new WorkspaceCommandContext(editing, window);
            var command = CommandRegistry.Find(id)!;
            Assert.False(context.TryExecute(command));
            Assert.Empty(editing.Calls);
            editing.CanUndo = editing.CanRedo = true;
            Assert.True(context.TryExecute(command));
            Assert.Single(editing.Calls);
            Assert.Empty(window.Calls);
        }
    }
}

using System;
using Avalonia.Headless.XUnit;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtau.Test.TestSupport;
using ReactiveUI;
using Xunit;

namespace OpenUtau.Test.App {
    [Collection("WorkspaceSession")]
    public sealed class NotePresetRangeTests {
        [AvaloniaFact]
        public void VibratoPreset_PreservesNegativeVolumeLinkAndUndo() {
            using var dispatcher = DocManagerTestSetup.EnterScopedDispatcher(nullChannel: true, installScheduler: false);
            var previous = DocManager.Inst.Project;
            var project = new UProject();
            var part = new UVoicePart { Duration = 1920 };
            var note = project.CreateNote(60, 0, 240);
            part.notes.Add(note);
            project.parts.Add(part);
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(project));
            using var parent = new NotePropertiesViewModel { Part = part };
            try {
                var selection = new NoteSelectionViewModel();
                selection.Add(note);
                MessageBus.Current.SendMessage(new NotesSelectionEvent(selection));
                float before = note.vibrato.volLink;
                parent.BeginDiscreteInput(nameof(parent.ApplyVibratoPreset));
                parent.ApplyVibratoPreset = new NotePresets.VibratoPreset("Negative volume link", 60, 160, 40, 20, 20, 5, 5, -50);
                parent.EndDiscreteInputs();
                Assert.Equal(-50f, note.vibrato.volLink);
                Assert.Equal(-50f, parent.VibratoVolLink);
                Assert.Null(DocManager.Inst.ActiveUndoGroup);
                DocManager.Inst.Undo();
                Assert.Equal(before, note.vibrato.volLink);
                Assert.False(DocManager.Inst.GetUndoState(out _));
            } finally {
                parent.Dispose();
                if (DocManager.Inst.ActiveUndoGroup != null) DocManager.Inst.EndUndoGroup();
                DocManager.Inst.ExecuteCmd(new LoadProjectNotification(previous));
            }
        }
    }
}

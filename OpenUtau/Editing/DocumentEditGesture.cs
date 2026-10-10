using System;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;

namespace OpenUtau.App.Editing {
    /// <summary>A gesture owns one document and only its own undo group. A save
    /// can seal the group while input continues; subsequent input starts a new one.</summary>
    internal sealed class DocumentEditGesture : IDisposable {
        readonly DocManager doc = DocManager.Inst;
        readonly UProject project;
        readonly string? nameKey;
        readonly bool deferValidate;
        UCommandGroup? ownedGroup;
        bool finished;

        public DocumentEditGesture(UProject project, string? nameKey, bool deferValidate = false) {
            this.project = project;
            this.nameKey = nameKey;
            this.deferValidate = deferValidate;
        }

        public bool IsCurrent => !finished && ReferenceEquals(project, doc.Project);

        public bool Execute(UCommand command) {
            if (!IsCurrent) return false;
            if (command is PartCommand part && (!ReferenceEquals(part.project, project) ||
                !project.parts.Contains(part.part))) return false;
            if (ownedGroup == null || !ReferenceEquals(ownedGroup, doc.ActiveUndoGroup)) {
                // Modal actions and another control may own the active transaction.
                // Do not close it or silently append this gesture's commands to it.
                if (doc.ActiveUndoGroup != null) return false;
                doc.StartUndoGroup(nameKey, deferValidate);
                ownedGroup = doc.ActiveUndoGroup;
            }
            doc.ExecuteCmd(command);
            return true;
        }

        public void Dispose() {
            if (finished) return;
            finished = true;
            if (ReferenceEquals(project, doc.Project) && ownedGroup != null &&
                ReferenceEquals(ownedGroup, doc.ActiveUndoGroup)) doc.EndUndoGroup();
            ownedGroup = null;
        }
    }
}

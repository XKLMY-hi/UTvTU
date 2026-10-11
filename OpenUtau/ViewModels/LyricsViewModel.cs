using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using OpenUtau.App.Editing;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtau.App.ViewModels {
    /// <summary>Each preview is a sealed gesture; cancellation restores only lyrics still owned by this session.</summary>
    class LyricsViewModel : ViewModelBase, ICmdSubscriber, IDisposable {
        [Reactive] public string? Text { get; set; } = string.Empty;
        [Reactive] public int CurrentCount { get; set; }
        [Reactive] public int TotalCount { get; set; }
        [Reactive] public bool LivePreview { get; set; } = Preferences.Default.LyricLivePreview;
        [Reactive] public bool ApplySelection { get; set; } = Preferences.Default.LyricApplySelectionOnly;
        readonly CompositeDisposable subscriptions = new();
        readonly Dictionary<UNote, string> original = new(), preview = new();
        readonly HashSet<UNote> externallyEdited = new();
        UProject? project;
        UVoicePart? part;
        UNote[] notes = Array.Empty<UNote>(), selection = Array.Empty<UNote>();
        bool applying, disposed;
        bool Current => !disposed && project != null && part != null && ReferenceEquals(project, DocManager.Inst.Project)
            && project.parts.Contains(part);

        public LyricsViewModel() {
            subscriptions.Add(this.WhenAnyValue(x => x.LivePreview, x => x.Text).Subscribe(_ => Preview(LivePreview)));
            subscriptions.Add(this.WhenAnyValue(x => x.ApplySelection).Subscribe(_ => { UpdateTotalCount(); Preview(LivePreview); }));
        }
        public void Start(UVoicePart part, UNote[] notes, UNote[] selection) {
            this.project = DocManager.Inst.Project;
            this.part = part;
            this.notes = notes.ToArray(); this.selection = selection.ToArray();
            foreach (var note in notes) original[note] = note.lyric;
            if (selection.Length == 0) ApplySelection = false;
            UpdateTotalCount();
            Text = SplitLyrics.Join((ApplySelection ? this.selection : this.notes).Select(n => original[n]).ToArray());
            DocManager.Inst.AddSubscriber(this);
        }
        void UpdateTotalCount() => TotalCount = (ApplySelection ? selection : notes).Length;
        void Preview(bool update) {
            var lyrics = SplitLyrics.Split(Text);
            CurrentCount = lyrics.Count;
            if (!Current || applying) return;
            var target = ApplySelection ? selection : notes;
            var desired = new Dictionary<UNote, string>();
            if (update) for (int i = 0; i < lyrics.Count && i < target.Length; i++) desired[target[i]] = lyrics[i];
            applying = true;
            try {
                using var edits = new DocumentEditGesture(project!, "command.note.lyric");
                if (!edits.CanExecute) return;
                foreach (var note in notes) {
                    if (!part!.notes.Contains(note) || externallyEdited.Contains(note)) continue;
                    if (preview.TryGetValue(note, out var last) && note.lyric != last) { externallyEdited.Add(note); preview.Remove(note); continue; }
                    string value = desired.TryGetValue(note, out var lyric) ? lyric : original[note];
                    if (value == note.lyric || edits.Execute(new ChangeNoteLyricCommand(part, note, value))) {
                        if (value == original[note]) preview.Remove(note); else preview[note] = value;
                    }
                }
            } finally { applying = false; }
        }
        public void Reset() => Text = SplitLyrics.Join((ApplySelection ? selection : notes).Select(n => original[n]).ToArray());
        public void Cancel() { if (disposed) return; Preview(false); FinishSession(); }
        public void Finish() { if (disposed) return; Preview(true); FinishSession(); }
        void FinishSession() {
            Preferences.Default.LyricLivePreview = LivePreview;
            Preferences.Default.LyricApplySelectionOnly = ApplySelection;
            Dispose();
            Preferences.Save();
        }
        public void OnNext(UCommand cmd, bool isUndo) {
            if (applying || disposed) return;
            if (cmd is ChangeNoteLyricCommand lyric && ReferenceEquals(lyric.Part, part))
                foreach (var note in lyric.Notes) { externallyEdited.Add(note); preview.Remove(note); }
        }
        public void Dispose() {
            if (disposed) return;
            disposed = true;
            DocManager.Inst.RemoveSubscriber(this);
            subscriptions.Dispose();
        }
    }
}

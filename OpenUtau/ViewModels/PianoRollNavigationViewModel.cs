using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;

namespace OpenUtau.App.ViewModels {
    /// <summary>Projects the voice editor entry from the current document, independent of a window.</summary>
    public sealed class PianoRollNavigationViewModel : ViewModelBase, ICmdSubscriber, IDisposable {
        readonly DocManager doc = DocManager.Inst;
        readonly CompositeDisposable subscriptions = new();
        UVoicePart? selected;
        UVoicePart? current;
        UVoicePart? target;
        bool disposed;

        public UVoicePart? TargetPart => target;
        public bool CanOpen => target != null;
        public string Hint => ThemeManager.GetString(CanOpen ? "view.pianoroll.openpart" : "view.pianoroll.selectpart");

        public PianoRollNavigationViewModel() {
            MessageBus.Current.Listen<PartsSelectionEvent>().Subscribe(e => {
                selected = e.selectedParts.OfType<UVoicePart>().FirstOrDefault(p => doc.Project.parts.Contains(p));
                Refresh();
            }).DisposeWith(subscriptions);
            MessageBus.Current.Listen<ThemeChangedEvent>().Subscribe(_ => this.RaisePropertyChanged(nameof(Hint)))
                .DisposeWith(subscriptions);
            doc.AddSubscriber(this);
        }

        void Refresh() {
            target = disposed ? null :
                selected != null && doc.Project.parts.Contains(selected) ? selected :
                current != null && doc.Project.parts.Contains(current) ? current : null;
            this.RaisePropertyChanged(nameof(TargetPart));
            this.RaisePropertyChanged(nameof(CanOpen));
            this.RaisePropertyChanged(nameof(Hint));
        }

        public void OnNext(UCommand cmd, bool isUndo) {
            if (disposed) return;
            if (cmd is LoadProjectNotification) {
                selected = current = null;
                Refresh();
            } else if (cmd is LoadPartNotification load && ReferenceEquals(load.project, doc.Project)) {
                current = load.part as UVoicePart;
                Refresh();
            } else if (cmd is PartCommand or TrackCommand) {
                Refresh();
            }
        }

        public void Dispose() {
            if (disposed) return;
            disposed = true;
            subscriptions.Dispose();
            doc.RemoveSubscriber(this);
            selected = current = null;
            Refresh();
        }
    }
}

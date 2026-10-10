using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using OpenUtau.App.Controls;
using OpenUtau.App.Editing;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;

namespace OpenUtau.App.Views {
    class PartEditState {
        public virtual MouseButton MouseButton => MouseButton.Left;
        public readonly Control control;
        protected readonly TracksViewModel tracksVm;
        public Point startPoint;
        protected virtual string? commandNameKey => null;
        DocumentEditGesture? gesture;
        protected IPointer? capturedPointer;
        protected bool CanEdit => gesture?.CanExecute == true;

        public PartEditState(Control control, MainWindowViewModel vm) {
            this.control = control;
            tracksVm = vm?.TracksViewModel!;
        }
        protected PartEditState(Control control, TracksViewModel tracksVm) {
            this.control = control;
            this.tracksVm = tracksVm;
        }
        public virtual void Begin(IPointer pointer, Point point) {
            gesture = new DocumentEditGesture(DocManager.Inst.Project, commandNameKey);
            capturedPointer = pointer;
            pointer.Capture(control);
            startPoint = point;
        }
        public virtual void End(IPointer pointer, Point point) {
            Cancel();
        }
        public virtual void Cancel() {
            var pointer = capturedPointer;
            capturedPointer = null;
            gesture?.Dispose();
            gesture = null;
            if (pointer?.Captured == control) pointer.Capture(null);
        }
        protected void ExecuteCmd(UCommand command) => gesture?.Execute(command);
        public virtual void Update(IPointer pointer, Point point) { }
        public static void Swap<T>(ref T a, ref T b) {
            T temp = a;
            a = b;
            b = temp;
        }
    }

    class PartSelectionEditState : PartEditState {
        private int startTick;
        private int startTrack;
        public readonly Rectangle selectionBox;

        public PartSelectionEditState(Control control, MainWindowViewModel vm, Rectangle selectionBox) : base(control, vm) {
            this.selectionBox = selectionBox;
        }
        public override void Begin(IPointer pointer, Point point) {
            capturedPointer = pointer;
            pointer.Capture(control);
            startPoint = point;
            selectionBox.IsVisible = true;
            var tracksVm = this.tracksVm;
            startTick = tracksVm.PointToTick(point);
            startTrack = tracksVm.PointToTrackNo(point);
        }
        public override void End(IPointer pointer, Point point) {
            var tracksVm = this.tracksVm;
            tracksVm.CommitTempSelectParts();
            Cancel();
        }
        public override void Cancel() {
            selectionBox.IsVisible = false;
            tracksVm.CancelTempSelectParts();
            base.Cancel();
        }
        public override void Update(IPointer pointer, Point point) {
            var tracksVm = this.tracksVm;
            int tick = tracksVm.PointToTick(point);
            int track = tracksVm.PointToTrackNo(point);

            int minTick = Math.Min(tick, startTick);
            int maxTick = Math.Max(tick, startTick);
            tracksVm.TickToLineTick(minTick, out int x0, out int _);
            tracksVm.TickToLineTick(maxTick, out int _, out int x1);

            int y0 = Math.Min(track, startTrack);
            int y1 = Math.Max(track, startTrack) + 1;

            var leftTop = tracksVm.TickTrackToPoint(x0, y0);
            var Size = tracksVm.TickTrackToSize(x1 - x0, y1 - y0);
            Canvas.SetLeft(selectionBox, leftTop.X);
            Canvas.SetTop(selectionBox, leftTop.Y);
            selectionBox.Width = Size.Width + 1;
            selectionBox.Height = Size.Height;
            tracksVm.TempSelectParts(x0, x1, y0, y1);
        }
    }

    class PartMoveEditState : PartEditState {
        public readonly UPart part;
        public readonly bool isVoice;
        private double xOffset;
        HashSet<UPart> selection = new();
        protected override string? commandNameKey => "command.part.move";

        public PartMoveEditState(Control control, MainWindowViewModel vm, UPart part) : this(control, vm.TracksViewModel, part) { }
        internal PartMoveEditState(Control control, TracksViewModel tracksVm, UPart part) : base(control, tracksVm) {
            this.part = part;
            isVoice = part is UVoicePart;
            if (!tracksVm.SelectedParts.Contains(part)) {
                tracksVm.DeselectParts();
                tracksVm.SelectPart(part);
            }
        }
        public override void Begin(IPointer pointer, Point point) {
            base.Begin(pointer, point);
            selection = tracksVm.SelectedParts.ToHashSet();
            xOffset = point.X - tracksVm.TickTrackToPoint(part.position, 0).X;
        }
        public override void Update(IPointer pointer, Point point) {
            if (!CanEdit) return;
            var delta = point - startPoint;
            if (Math.Abs(delta.X) + Math.Abs(delta.Y) < 4) {
                return;
            }
            var project = DocManager.Inst.Project;
            var tracksVm = this.tracksVm;
            if (!project.parts.Contains(part) || !selection.SetEquals(tracksVm.SelectedParts) ||
                selection.Any(p => !project.parts.Contains(p))) return;

            int deltaTrack = tracksVm.PointToTrackNo(point) - part.trackNo;
            int minDeltaTrack;
            int maxDeltaTrack;
            if (selection.Count > 0) {
                minDeltaTrack = -selection.Select(p => p.trackNo).Min();
                maxDeltaTrack = project.tracks.Count - 1 - selection.Select(p => p.trackNo).Max();
            } else {
                minDeltaTrack = -part.trackNo;
                maxDeltaTrack = project.tracks.Count - 1 - part.trackNo;
            }
            deltaTrack = Math.Clamp(deltaTrack, minDeltaTrack, maxDeltaTrack);

            int newPos = 0;
            if (!isVoice) {
                newPos = tracksVm.PointToTick(point - new Point(xOffset, 0));
            } else {
                tracksVm.PointToLineTick(point - new Point(xOffset, 0), out int left, out int right);
                newPos = left;
            }
            int deltaTick = newPos - part.position;
            int minDeltaTick;
            if (selection.Count > 0) {
                minDeltaTick = -selection.Select(p => p.position).Min();
            } else {
                minDeltaTick = -part.position;
            }
            deltaTick = Math.Max(deltaTick, minDeltaTick);

            if (deltaTrack == 0 && deltaTick == 0) {
                return;
            }
            if (selection.Count == 0) {
                ExecuteCmd(new MovePartCommand(
                    project, part, part.position + deltaTick, part.trackNo + deltaTrack));
                return;
            }
            foreach (var part in selection) {
                if (!selection.SetEquals(tracksVm.SelectedParts)) break;
                ExecuteCmd(new MovePartCommand(
                    project, part, part.position + deltaTick, part.trackNo + deltaTrack));
            }
        }
    }

    class PartResizeEditState : PartEditState {
        public readonly UPart part;
        public readonly bool fromStart;
        private List<UPart> selectedParts;
        protected override string? commandNameKey => "command.part.edit";

        public PartResizeEditState(Control control, MainWindowViewModel vm, UPart part, bool fromStart = false) : base(control, vm) {
            this.part = part;
            var tracksVm = this.tracksVm;
            if (!tracksVm.SelectedParts.Contains(part)) {
                tracksVm.DeselectParts();
            }
            if (part is UVoicePart) {
                selectedParts = tracksVm.SelectedParts.Where(part => part is UVoicePart).ToList();
            } else {
                selectedParts = tracksVm.SelectedParts.Where(part => part is UWavePart).ToList();
            }
            if (selectedParts.Count == 0) {
                selectedParts.Add(part);
            }
            this.fromStart = fromStart;
        }
        public override void Update(IPointer pointer, Point point) {
            if (!CanEdit) return;
            var project = DocManager.Inst.Project;
            var tracksVm = this.tracksVm;
            if (!project.parts.Contains(part) || selectedParts.Any(p => !project.parts.Contains(p))) return;

            if (part is UVoicePart) {
                tracksVm.PointToLineTick(point, out int left, out int right);
                int deltaDuration = this.fromStart
                    ? part.position - left
                    : right - part.End;
                if (deltaDuration < 0) {
                    int maxDurReduction = selectedParts.Min(p => fromStart ? p.GetMaxPosiTick(project) - p.position : p.Duration - p.GetMinDurTick(project));
                    deltaDuration = Math.Max(deltaDuration, -maxDurReduction);
                }
                if (deltaDuration == 0) {
                    return;
                }
                foreach (UVoicePart voicePart in selectedParts.Where(part => part is UVoicePart)) {
                    ExecuteCmd(new ResizeVoicePartCommand(project, voicePart, deltaDuration, fromStart));
                }
            } else if (part is UWavePart) {
                int newPos = tracksVm.PointToTick(point);
                int deltaDuration = this.fromStart
                    ? part.position - newPos
                    : newPos - part.End;
                var selectedWaveParts = selectedParts.Select(part => (UWavePart)part);
                if (deltaDuration < 0) {
                    int maxDurReduction = selectedWaveParts.Min(p => fromStart ? p.GetMaxPosiTick(project) - p.position : p.Duration - p.GetMinDurTick(project));
                    deltaDuration = Math.Max(deltaDuration, -maxDurReduction);
                } else if (deltaDuration > 0) {
                    int maxDurExtension = selectedWaveParts.Min(p => fromStart ? Math.Min(p.skip, p.position) : p.trim);
                    deltaDuration = Math.Min(deltaDuration, maxDurExtension);
                }
                if (deltaDuration == 0) {
                    return;
                }
                foreach (UWavePart wavePart in selectedWaveParts) {
                    ExecuteCmd(new ResizeWavePartCommand(project, wavePart, deltaDuration, fromStart));
                }
            }
        }
    }

    class PartFadeInState : PartEditState {
        public readonly UWavePart part;
        protected override string? commandNameKey => "command.part.fadein";

        public PartFadeInState(Control control, MainWindowViewModel vm, UWavePart part) : base(control, vm) {
            this.part = part;
        }
        public override void Update(IPointer pointer, Point point) {
            if (!CanEdit) return;
            var project = DocManager.Inst.Project;
            var tracksVm = this.tracksVm;
            int pointerTick = tracksVm.PointToTick(point);
            int fadeTick = Math.Max(0, pointerTick - part.position);
            fadeTick = Math.Min(fadeTick, part.Duration - part.fadeout - 120);
            ExecuteCmd(new PartFadeInCommand(project, part, fadeTick));
        }
    }

    class PartFadeOutState : PartEditState {
        public readonly UWavePart part;
        protected override string? commandNameKey => "command.part.fadeout";

        public PartFadeOutState(Control control, MainWindowViewModel vm, UWavePart part) : base(control, vm) {
            this.part = part;
        }
        public override void Update(IPointer pointer, Point point) {
            if (!CanEdit) return;
            var project = DocManager.Inst.Project;
            var tracksVm = this.tracksVm;
            int pointerTick = tracksVm.PointToTick(point);
            int fadeTick = Math.Max(0, part.End - pointerTick);
            fadeTick = Math.Min(fadeTick, part.Duration - part.fadein - 120);
            ExecuteCmd(new PartFadeOutCommand(project, part, fadeTick));
        }
    }

    class PartEraseEditState : PartEditState {
        public override MouseButton MouseButton => MouseButton.Right;
        protected override string? commandNameKey => "command.part.delete";

        public PartEraseEditState(Control control, MainWindowViewModel vm) : base(control, vm) { }
        public override void Update(IPointer pointer, Point point) {
            if (!CanEdit) return;
            var project = DocManager.Inst.Project;
            var control = base.control.InputHitTest(point);
            if (control is PartControl partControl) {
                ExecuteCmd(new RemovePartCommand(project, partControl.part));
            }
        }
    }

    class PartPanningState : PartEditState {
        public override MouseButton MouseButton => MouseButton.Middle;
        public PartPanningState(Control control, MainWindowViewModel vm) : base(control, vm) { }
        public override void Begin(IPointer pointer, Point point) {
            capturedPointer = pointer;
            pointer.Capture(control);
            startPoint = point;
        }
        public override void End(IPointer pointer, Point point) {
            Cancel();
        }
        public override void Update(IPointer pointer, Point point) {
            var tracksVm = this.tracksVm;
            double deltaX = (point.X - startPoint.X) / tracksVm.TickWidth;
            double deltaY = (point.Y - startPoint.Y) / tracksVm.TrackHeight;
            startPoint = point;
            tracksVm.TickOffset = Math.Max(0, tracksVm.TickOffset - deltaX);
            tracksVm.TrackOffset = Math.Max(0, tracksVm.TrackOffset - deltaY);
        }
    }
}

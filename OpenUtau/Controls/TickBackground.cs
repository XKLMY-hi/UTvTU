using System;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using OpenUtau.App.ViewModels;
using ReactiveUI;

namespace OpenUtau.App.Controls {
    class TickBackground : TemplatedControl {
        // Opt-in: the piano editor keeps its original ruler, grid and note geometry.
        public static readonly StyledProperty<bool> IsArrangementProperty =
            AvaloniaProperty.Register<TickBackground, bool>(nameof(IsArrangement));
        public bool IsArrangement { get => GetValue(IsArrangementProperty); set => SetValue(IsArrangementProperty, value); }

        private static readonly IDashStyle DashStyle = new ImmutableDashStyle(new double[] { 2, 4 }, 0);

        public static readonly DirectProperty<TickBackground, int> ResolutionProperty =
            AvaloniaProperty.RegisterDirect<TickBackground, int>(
                nameof(Resolution),
                o => o.Resolution,
                (o, v) => o.Resolution = v);
        public static readonly DirectProperty<TickBackground, double> TickWidthProperty =
            AvaloniaProperty.RegisterDirect<TickBackground, double>(
                nameof(TickWidth),
                o => o.TickWidth,
                (o, v) => o.TickWidth = v);
        public static readonly DirectProperty<TickBackground, double> TickOffsetProperty =
            AvaloniaProperty.RegisterDirect<TickBackground, double>(
                nameof(TickOffset),
                o => o.TickOffset,
                (o, v) => o.TickOffset = v);
        public static readonly DirectProperty<TickBackground, int> TickOriginProperty =
            AvaloniaProperty.RegisterDirect<TickBackground, int>(
                nameof(TickOrigin),
                o => o.TickOrigin,
                (o, v) => o.TickOrigin = v);
        public static readonly DirectProperty<TickBackground, int> SnapDivProperty =
            AvaloniaProperty.RegisterDirect<TickBackground, int>(
                nameof(SnapDiv),
                o => o.SnapDiv,
                (o, v) => o.SnapDiv = v);
        public static readonly DirectProperty<TickBackground, ObservableCollection<int>?> SnapTicksProperty =
            AvaloniaProperty.RegisterDirect<TickBackground, ObservableCollection<int>?>(
                nameof(SnapTicks),
                o => o.SnapTicks,
                (o, v) => o.SnapTicks = v);
        public static readonly DirectProperty<TickBackground, bool> ShowBarProperty =
            AvaloniaProperty.RegisterDirect<TickBackground, bool>(
                nameof(ShowBar),
                o => o.ShowBar,
                (o, v) => o.ShowBar = v);

        public int Resolution {
            get => _resolution;
            private set => SetAndRaise(ResolutionProperty, ref _resolution, value);
        }
        // Tick width in pixel.
        public double TickWidth {
            get => _tickWidth;
            private set => SetAndRaise(TickWidthProperty, ref _tickWidth, value);
        }
        public double TickOffset {
            get => _tickOffset;
            private set => SetAndRaise(TickOffsetProperty, ref _tickOffset, value);
        }
        public int TickOrigin {
            get => _tickOrigin;
            private set => SetAndRaise(TickOriginProperty, ref _tickOrigin, value);
        }
        public int SnapDiv {
            get => _snapDiv;
            set => SetAndRaise(SnapDivProperty, ref _snapDiv, value);
        }
        public ObservableCollection<int>? SnapTicks {
            get => _snapTicks;
            set => SetAndRaise(SnapTicksProperty, ref _snapTicks, value);
        }
        public bool ShowBar {
            get => _showBar;
            set => SetAndRaise(ShowBarProperty, ref _showBar, value);
        }

        private int _resolution = 480;
        private double _tickWidth;
        private double _tickOffset;
        private int _tickOrigin;
        private int _snapDiv;
        private ObservableCollection<int>? _snapTicks;
        private bool _showBar = true;

        private Pen penBar;
        private Pen penBeatUnit;
        private Pen penDanshed;

        public TickBackground() {
            penBar = new Pen(Foreground, 1);
            penBeatUnit = new Pen(Background, 1);
            penDanshed = new Pen(Background, 1) {
                DashStyle = DashStyle,
            };
            MessageBus.Current.Listen<ThemeChangedEvent>()
                .Subscribe(e => InvalidateVisual());
            MessageBus.Current.Listen<TimeAxisChangedEvent>()
                .Subscribe(e => InvalidateVisual());
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == ForegroundProperty) {
                penBar = new Pen(Foreground, 1);
            }
            if (change.Property == BackgroundProperty) {
                penBeatUnit = new Pen(Background, 1);
                penDanshed = new Pen(Background, 1) {
                    DashStyle = DashStyle,
                };
            }
            if (change.Property == ResolutionProperty ||
                change.Property == TickOriginProperty ||
                change.Property == TickWidthProperty ||
                change.Property == TickOffsetProperty ||
                change.Property == SnapDivProperty ||
                change.Property == ShowBarProperty ||
                change.Property == IsArrangementProperty) {
                InvalidateVisual();
            }
        }

        public override void Render(DrawingContext context) {
            if (TickWidth <= 0) {
                return;
            }
            if (IsArrangement) {
                context.DrawRectangle(ArrangementVisuals.Brush("md3.surface-container", ThemeManager.BackgroundBrush), null,
                    new Rect(0, 0, Bounds.Width, 34));
                var edge = new Pen(ArrangementVisuals.Brush("md3.outline-variant", Foreground), 1);
                context.DrawLine(edge, new Point(0, 33.5), new Point(Bounds.Width, 33.5));
            }
            var project = Core.DocManager.Inst.Project;
            int snapUnit = project.resolution * 4 / SnapDiv;
            while (snapUnit * TickWidth < ViewConstants.MinTicklineWidth) {
                snapUnit *= 2; // Avoid drawing too dense.
            }
            double minLineTick = ViewConstants.MinTicklineWidth / TickWidth;
            double pixelOffset = (TickOffset + TickOrigin) * TickWidth;
            double leftTick = TickOffset + TickOrigin;
            double rightTick = TickOffset + TickOrigin + Bounds.Width / TickWidth;

            project.timeAxis.TickPosToBarBeat(TickOrigin, out int bar, out int beat, out int remainingTicks);
            if (bar > 0) {
                bar--;
            }
            int barTick = project.timeAxis.BarBeatToTickPos(bar, 0);
            SnapTicks?.Clear();
            while (barTick <= rightTick) {
                SnapTicks?.Add(barTick);
                double x = Math.Round(barTick * TickWidth - pixelOffset) + 0.5;
                double y = -0.5;
                double rulerBarWidth = IsArrangement
                    ? (project.timeAxis.BarBeatToTickPos(bar + 1, 0) - barTick) * TickWidth : 0;
                int labelStride = IsArrangement ? Math.Max(1, (int)Math.Ceiling(32 / Math.Max(1, rulerBarWidth))) : 1;
                if (ShowBar && bar % labelStride == 0) {
                    var textLayout = TextLayoutCache.Get((bar + 1).ToString(),
                        IsArrangement ? ArrangementVisuals.Brush("md3.on-surface", ThemeManager.BarNumberBrush) : ThemeManager.BarNumberBrush,
                        IsArrangement ? 11 : 10, IsArrangement);
                    using (var state = context.PushTransform(Matrix.CreateTranslation(x + (IsArrangement ? 6 : 3), IsArrangement ? 17 : 10))) {
                        textLayout.Draw(context, new Point());
                    }
                }
                if (IsArrangement) {
                    using (context.PushOpacity(.65)) {
                        context.DrawLine(penBar, new Point(x, 50), new Point(x, Bounds.Height + .5));
                    }
                    context.DrawLine(penBar, new Point(x, 27), new Point(x, 34));
                } else {
                    context.DrawLine(penBar, new Point(x, y), new Point(x, Bounds.Height + 0.5f));
                }
                // Lines between bars.
                var timeSig = project.timeAxis.TimeSignatureAtBar(bar);
                int nextBarTick = project.timeAxis.BarBeatToTickPos(bar + 1, 0);
                int ticksPerBeat = project.resolution * 4 * timeSig.beatPerBar / timeSig.beatUnit;
                int ticksPerLine = snapUnit;
                if (ticksPerBeat < snapUnit) {
                    ticksPerLine = ticksPerBeat;
                } else if (ticksPerBeat % snapUnit != 0) {
                    if (ticksPerBeat > minLineTick) {
                        ticksPerLine = ticksPerBeat;
                    } else {
                        ticksPerLine = nextBarTick - barTick;
                    }
                }
                if (nextBarTick > leftTick) {
                    for (int tick = barTick + ticksPerLine; tick < nextBarTick; tick += ticksPerLine) {
                        SnapTicks?.Add(tick);
                        project.timeAxis.TickPosToBarBeat(tick, out int snapBar, out int snapBeat, out int snapRemainingTicks);
                        var pen = snapRemainingTicks != 0 ? penDanshed : penBeatUnit;
                        x = Math.Round(tick * TickWidth - pixelOffset) + 0.5;
                        y = ShowBar ? 24 : 0;
                        if (IsArrangement) {
                            using (context.PushOpacity(snapRemainingTicks != 0 ? .28 : .65)) {
                                // Solid subdivisions are quiet; beat lines remain easy to follow.
                                context.DrawLine(penBeatUnit, new Point(x, 50), new Point(x, Bounds.Height + .5));
                            }
                            if (snapRemainingTicks == 0) {
                                context.DrawLine(penBeatUnit, new Point(x, 30), new Point(x, 34));
                            }
                        } else {
                            context.DrawLine(pen, new Point(x, y), new Point(x, Bounds.Height + 0.5f));
                        }
                    }
                }
                barTick = nextBarTick;
                bar++;
            }
            SnapTicks?.Add(barTick);

            if (ShowBar && IsArrangement) {
                DrawArrangementMarkers(context, pixelOffset);
            } else if (ShowBar) {
                foreach (var tempo in project.tempos) {
                    double x = Math.Round(tempo.position * TickWidth - pixelOffset) + 0.5;
                    context.DrawLine(penDanshed, new Point(x, 0), new Point(x, 24));
                    var textLayout = TextLayoutCache.Get(tempo.bpm.ToString("#0.00"), ThemeManager.BarNumberBrush, 10);
                    using (var state = context.PushTransform(Matrix.CreateTranslation(x + 3, 0))) {
                        textLayout.Draw(context, new Point());
                    }
                }

                foreach (var timeSig in project.timeSignatures) {
                    int tick = project.timeAxis.BarBeatToTickPos(timeSig.barPosition, 0);
                    var barTextLayout = TextLayoutCache.Get((timeSig.barPosition + 1).ToString(), ThemeManager.BarNumberBrush, 10);
                    double x = Math.Round(tick * TickWidth - pixelOffset) + 0.5 + barTextLayout.Width + 4;
                    var textLayout = TextLayoutCache.Get($"{timeSig.beatPerBar}/{timeSig.beatUnit}", ThemeManager.BarNumberBrush, 10);
                    using (var state = context.PushTransform(Matrix.CreateTranslation(x + 3, 10))) {
                        textLayout.Draw(context, new Point());
                    }
                }
            }
        }
        void DrawArrangementMarkers(DrawingContext context, double pixelOffset) {
            var project = Core.DocManager.Inst.Project;
            var ink = ArrangementVisuals.Brush("md3.on-surface-variant", ThemeManager.BarNumberBrush);
            var primary = ArrangementVisuals.Brush("md3.primary", ThemeManager.AccentBrush1);
            foreach (var tempo in project.tempos) {
                double x = Math.Round(tempo.position * TickWidth - pixelOffset) + .5;
                // The complete value remains available even at the left scroll boundary.
                var label = TextLayoutCache.Get(tempo.bpm.ToString("#0.##"), primary, 10, true);
                if (x + label.Width + 10 < 0 || x > Bounds.Width) continue;
                context.DrawEllipse(primary, null, new Point(x + 4, 8), 1.5, 1.5);
                label.Draw(context, new Point(x + 9, 1));
            }
            foreach (var signature in project.timeSignatures) {
                int tick = project.timeAxis.BarBeatToTickPos(signature.barPosition, 0);
                double x = Math.Round(tick * TickWidth - pixelOffset) + .5;
                var bar = TextLayoutCache.Get((signature.barPosition + 1).ToString(), ink, 11, true);
                var label = TextLayoutCache.Get($"{signature.beatPerBar}/{signature.beatUnit}", ink, 10);
                // Do not paint over the next bar number at distant zoom levels.
                double barWidth = (project.timeAxis.BarBeatToTickPos(signature.barPosition + 1, 0) - tick) * TickWidth;
                if (barWidth > bar.Width + label.Width + 22) label.Draw(context, new Point(x + bar.Width + 15, 18));
            }
        }
    }
}

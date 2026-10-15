using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using NWaves.Signals;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using Serilog;

namespace OpenUtau.App.Controls {
    class PartControl : Control, IDisposable, IProgress<int> {
        public static readonly DirectProperty<PartControl, double> TickWidthProperty =
            AvaloniaProperty.RegisterDirect<PartControl, double>(
                nameof(TickWidth),
                o => o.TickWidth,
                (o, v) => o.TickWidth = v);
        public static readonly DirectProperty<PartControl, double> TrackHeightProperty =
            AvaloniaProperty.RegisterDirect<PartControl, double>(
                nameof(TrackHeight),
                o => o.TrackHeight,
                (o, v) => o.TrackHeight = v);
        public static readonly DirectProperty<PartControl, double> ViewWidthProperty =
            AvaloniaProperty.RegisterDirect<PartControl, double>(
                nameof(ViewWidth),
                o => o.ViewWidth,
                (o, v) => o.ViewWidth = v);
        public static readonly DirectProperty<PartControl, double> TickOffsetProperty =
            AvaloniaProperty.RegisterDirect<PartControl, double>(
                nameof(TickOffset),
                o => o.TickOffset,
                (o, v) => o.TickOffset = v);
        public static readonly DirectProperty<PartControl, Point> OffsetProperty =
            AvaloniaProperty.RegisterDirect<PartControl, Point>(
                nameof(Offset),
                o => o.Offset,
                (o, v) => o.Offset = v);
        public static readonly DirectProperty<PartControl, string> TextProperty =
            AvaloniaProperty.RegisterDirect<PartControl, string>(
                nameof(Text),
                o => o.Text,
                (o, v) => o.Text = v);
        public static readonly DirectProperty<PartControl, bool> SelectedProperty =
            AvaloniaProperty.RegisterDirect<PartControl, bool>(
                nameof(Selected),
                o => o.Selected,
                (o, v) => o.Selected = v);
        public static readonly DirectProperty<PartControl, double> FadeInProperty =
            AvaloniaProperty.RegisterDirect<PartControl, double>(
                nameof(FadeIn),
                o => o.FadeIn,
                (o, v) => o.FadeIn = v);
        public static readonly DirectProperty<PartControl, double> FadeOutProperty =
            AvaloniaProperty.RegisterDirect<PartControl, double>(
                nameof(FadeOut),
                o => o.FadeOut,
                (o, v) => o.FadeOut = v);
        public static readonly DirectProperty<PartControl, double> PianoRollViewTickOffsetProperty =
            AvaloniaProperty.RegisterDirect<PartControl, double>(
                nameof(PianoRollViewTickOffset),
                o => o.PianoRollViewTickOffset,
                (o, v) => o.PianoRollViewTickOffset = v);
        public static readonly DirectProperty<PartControl, double> PianoRollViewViewportTicksProperty =
            AvaloniaProperty.RegisterDirect<PartControl, double>(
                nameof(PianoRollViewViewportTicks),
                o => o.PianoRollViewViewportTicks,
                (o, v) => o.PianoRollViewViewportTicks = v);

        // Tick width in pixel.
        public double TickWidth {
            get => tickWidth;
            set => SetAndRaise(TickWidthProperty, ref tickWidth, value);
        }
        public double TrackHeight {
            get => trackHeight;
            set => SetAndRaise(TrackHeightProperty, ref trackHeight, value);
        }
        public double ViewWidth {
            get { return viewWidth; }
            set { SetAndRaise(ViewWidthProperty, ref viewWidth, value); }
        }
        public double TickOffset {
            get { return tickOffset; }
            set { SetAndRaise(TickOffsetProperty, ref tickOffset, value); }
        }
        public Point Offset {
            get { return offset; }
            set { SetAndRaise(OffsetProperty, ref offset, value); }
        }
        public string Text {
            get { return text; }
            set { SetAndRaise(TextProperty, ref text, value); }
        }
        public bool Selected {
            get { return selected; }
            set { SetAndRaise(SelectedProperty, ref selected, value); }
        }
        public double FadeIn {
            get { return fadeIn * TickWidth; }
            set { SetAndRaise(FadeInProperty, ref fadeIn, value); }
        }
        public double FadeOut {
            get { return Width - (fadeOut * TickWidth); }
            set { SetAndRaise(FadeOutProperty, ref fadeOut, value); }
        }
        public double PianoRollViewTickOffset {
            get => pianoRollViewTickOffset;
            set => SetAndRaise(PianoRollViewTickOffsetProperty, ref pianoRollViewTickOffset, value);
        }
        public double PianoRollViewViewportTicks {
            get => pianoRollViewViewportTicks;
            set => SetAndRaise(PianoRollViewViewportTicksProperty, ref pianoRollViewViewportTicks, value);
        }

        private double tickWidth;
        private double trackHeight;
        private double viewWidth;
        private double tickOffset;
        private Point offset;
        private string text = string.Empty;
        private bool selected;
        private double fadeIn;
        private double fadeOut;
        private double pianoRollViewTickOffset;
        private double pianoRollViewViewportTicks;
        private Geometry pointGeometry;

        public readonly UPart part;
        private readonly PartsCanvas partsCanvas;
        // 卷帘视口指示条（上游 9caec1a6；把手尺寸常量与命中区一起定义在下方）
        private const double GripDot = 2;
        private const double GripGap = 3;
        private bool draggingViewport;
        private double viewportDragStartX;
        private double viewportDragStartOffset;
        private List<IDisposable> unbinds = new List<IDisposable>();
        private WriteableBitmap? bitmap;
        private int[] bitmapData;

        public PartControl(UPart part, PartsCanvas canvas) {
            this.part = part;
            partsCanvas = canvas;
            bitmapData = new int[0];
            pointGeometry = new EllipseGeometry(new Rect(0, 0, 6, 6));

            unbinds.Add(this.Bind(TickWidthProperty, canvas.GetObservable(PartsCanvas.TickWidthProperty)));
            unbinds.Add(this.Bind(TrackHeightProperty, canvas.GetObservable(PartsCanvas.TrackHeightProperty)));
            unbinds.Add(this.Bind(WidthProperty, canvas.GetObservable(PartsCanvas.TickWidthProperty).Select(tickWidth => tickWidth * part.Duration)));
            unbinds.Add(this.Bind(HeightProperty, canvas.GetObservable(PartsCanvas.TrackHeightProperty)));
            unbinds.Add(this.Bind(OffsetProperty, canvas.WhenAnyValue(x => x.TickOffset, x => x.TrackOffset,
                (tick, track) => new Point(-tick * TickWidth, -track * TrackHeight))));
            unbinds.Add(this.Bind(ViewWidthProperty, canvas.WhenAnyValue(x => x.Bounds).Select(bounds => bounds.Width)));
            unbinds.Add(this.Bind(TickOffsetProperty, canvas.WhenAnyValue(x => x.TickOffset).Select(tickOffset => tickOffset)));
            unbinds.Add(this.Bind(PianoRollViewTickOffsetProperty, canvas.GetObservable(PartsCanvas.PianoRollViewTickOffsetProperty)));
            unbinds.Add(this.Bind(PianoRollViewViewportTicksProperty, canvas.GetObservable(PartsCanvas.PianoRollViewViewportTicksProperty)));

            SetPosition();
            Refersh();

            if (part is UWavePart wavePart) {
                var scheduler = TaskScheduler.FromCurrentSynchronizationContext();
                wavePart.Peaks.ContinueWith((task) => {
                    if (task.IsFaulted) {
                        Log.Error(task.Exception, "Failed to build peaks");
                    } else {
                        InvalidateVisual();
                    }
                }, CancellationToken.None, TaskContinuationOptions.None, scheduler);
            }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == OffsetProperty ||
                change.Property == TrackHeightProperty ||
                change.Property == TickWidthProperty) {
                SetPosition();
            }
            // 卷帘视口（TickOffset / ViewportTicks）变化时**本控件不重绘**：这两个属性是
            // 每个部件都绑定到 PartsCanvas 的，原先各自 InvalidateVisual ⇒ 卷帘每滚一帧
            // 编排区所有部件都重绘（波形部件还会重建波形）。实际只有"打开的那个部件"
            // 画卷帘视口高亮，而 PartsCanvas 已经自己失效了它（InvalidatePartViewport()）。
            // 上游 `96473fa5` 同口径。实测（PartRedrawScopeTests）：滚动一帧的重绘数 3 → 1。
            if (change.Property == SelectedProperty ||
                change.Property == TextProperty || 
                change.Property == FadeInProperty ||
                change.Property == FadeOutProperty) {
                InvalidateVisual();
            }
        }

        public void SetPosition() {
            Canvas.SetLeft(this, Offset.X + part.position * tickWidth);
            Canvas.SetTop(this, Offset.Y + part.trackNo * trackHeight);
        }

        public void SetSize() {
            Width = TickWidth * part.Duration;
            Height = trackHeight;
        }

        public void Refersh() {
            Text = part.DisplayName;
            if (part is UWavePart wavePart) {
                FadeIn = wavePart.fadein;
                FadeOut = wavePart.fadeout;
            }
        }

        /// <summary>
        /// 测试/诊断用：本控件的实际绘制次数（性能类证据靠它，不靠像素）。
        /// 编排区一次滚动帧里"有几个部件被重绘"就是靠比对各部件的这个计数得出的。
        /// </summary>
        internal int RenderCount { get; private set; }

        public override void Render(DrawingContext context) {
            RenderCount++;
            if (Width <= 2 || Height <= 4) return;
            var tracks = Core.DocManager.Inst.Project.tracks;
            var track = part.trackNo >= 0 && part.trackNo < tracks.Count ? tracks[part.trackNo] : null;
            var colors = ArrangementVisuals.For(track, Selected);
            var clip = new Rect(1, 2, Width - 2, Height - 4);
            double titleHeight = Height >= 60 ? 22 : 16;
            double contentTop = clip.Top + titleHeight + 4;
            double contentHeight = Math.Max(1, clip.Bottom - contentTop - 5);
            var outline = new Pen(colors.Edge, 2);
            double radius = Math.Min(8, Math.Min(clip.Width, clip.Height) / 2);
            using var clipState = context.PushClip(new RoundedRect(clip, radius));
            context.DrawRectangle(colors.Body, null, clip);

            // Title and preview share one tonal surface; their reserved spaces stay separate.
            double visibleLeft = Math.Max(9, -Canvas.GetLeft(this) + 9);
            if (clip.Right > visibleLeft + 8) {
                using (context.PushClip(new Rect(visibleLeft, clip.Top, Math.Max(0, clip.Right - visibleLeft - 5), titleHeight))) {
                    DrawKindIcon(context, colors.Ink, visibleLeft, clip.Top + titleHeight / 2);
                    var label = TextLayoutCache.Get(Text, colors.Ink, Height >= 60 ? 12 : 10, Selected);
                    label.Draw(context, new Point(visibleLeft + 16, clip.Top + (titleHeight - label.Height) / 2));
                }
            }
            if (part is UVoicePart voicePart) {
                if (voicePart.notes.Count > 0) {
                    int maxTone = voicePart.notes.Max(note => note.tone);
                    int minTone = voicePart.notes.Min(note => note.tone);
                    int range = Math.Max(24, maxTone - minTone + 6);
                    double centerTone = (maxTone + minTone) / 2.0;
                    var notePen = new Pen(colors.Detail, Math.Clamp(contentHeight / 14, 2, 3), lineCap: PenLineCap.Round);
                    using (context.PushClip(new Rect(clip.X + 3, contentTop, Math.Max(0, clip.Width - 6), contentHeight))) {
                        foreach (var note in voicePart.notes) {
                            double y = contentTop + contentHeight * (.5 + (centerTone - note.tone) / range);
                            context.DrawLine(notePen, new Point(note.position * tickWidth, y), new Point(note.End * tickWidth, y));
                        }
                    }
                }
                // Highlight：卷帘视口指示条 + 中间的可拖拽把手（上游 9caec1a6）
                if (PianoRollViewportRect() is Rect vpRect) {
                    using (context.PushOpacity(.08)) {
                        context.DrawRectangle(colors.Ink, null, new RoundedRect(vpRect, new CornerRadius(3)));
                    }
                    context.DrawRectangle(null, new Pen(colors.Detail, 1.5), new RoundedRect(vpRect, new CornerRadius(3)));
                    if (GripRect(vpRect) is Rect grip) {
                        // 3×3 点阵把手
                        for (int column = 0; column < 3; ++column) {
                            for (int row = 0; row < 3; ++row) {
                                var center = new Point(
                                    grip.X + GripDot / 2 + column * (GripDot + GripGap),
                                    grip.Y + GripDot / 2 + row * (GripDot + GripGap));
                                context.DrawEllipse(colors.Ink, null, center, GripDot / 2, GripDot / 2);
                            }
                        }
                    }
                }
            } else if (part is UWavePart wavePart) {
                // A quiet zero line keeps silent sections legible without changing the waveform samples.
                using (context.PushOpacity(.18)) {
                    context.DrawLine(new Pen(colors.Detail, 1), new Point(clip.Left, contentTop + contentHeight / 2),
                        new Point(clip.Right, contentTop + contentHeight / 2));
                }
                // Waveform
                try {
                    DrawWaveform(wavePart, GetBitmap(ViewWidth), colors.Detail.Color);
                    if (bitmap != null) {
                        // Peaks are rasterized in viewport coordinates; translate only the image.
                        var srcRect = new Rect(0, 0, bitmap.PixelSize.Width, TrackHeight);
                        var dstRect = new Rect(-Canvas.GetLeft(this), contentTop, bitmap.PixelSize.Width, contentHeight);
                        using (context.PushClip(new Rect(clip.X, contentTop, clip.Width, contentHeight))) {
                            context.DrawImage(bitmap, srcRect, dstRect);
                        }
                    }
                } catch (Exception e) {
                    Log.Error(e, "failed to draw bitmap");
                }
                // Fade
                var brush = colors.Ink;
                var pen = outline;
                using (var state = context.PushTransform(Matrix.CreateTranslation(FadeIn, 0))) {
                    context.DrawGeometry(brush, pen, pointGeometry);
                }
                if (wavePart.fadein > 0) {
                    context.DrawLine(new Pen(colors.Detail), new Point(2, Height - 2), new Point(FadeIn + 1, 2));
                }
                using (var state = context.PushTransform(Matrix.CreateTranslation(FadeOut - 6, 0))) {
                    context.DrawGeometry(brush, pen, pointGeometry);
                }
                if (wavePart.fadeout > 0) {
                    context.DrawLine(new Pen(colors.Detail), new Point(Width - 1, Height - 2), new Point(FadeOut, 2));
                }
            }
            if (Selected && clip.Width > 2 && clip.Height > 2) context.DrawRectangle(null, outline, clip.Deflate(1), Math.Max(0, radius - 1), Math.Max(0, radius - 1));
        }

        void DrawKindIcon(DrawingContext context, IBrush brush, double x, double y) {
            var pen = new Pen(brush, 1.2, lineCap: PenLineCap.Round);
            if (part is UWavePart) {
                for (int i = 0; i < 5; i++) {
                    double h = i == 2 ? 4 : i % 2 == 0 ? 1.5 : 3;
                    context.DrawLine(pen, new Point(x + i * 2, y - h), new Point(x + i * 2, y + h));
                }
            } else {
                context.DrawEllipse(brush, null, new Point(x + 2, y + 3), 2, 1.5);
                context.DrawLine(pen, new Point(x + 4, y + 3), new Point(x + 4, y - 4));
                context.DrawLine(pen, new Point(x + 4, y - 4), new Point(x + 9, y - 5));
                context.DrawLine(pen, new Point(x + 9, y - 5), new Point(x + 9, y + 1));
                context.DrawEllipse(brush, null, new Point(x + 7, y + 1), 2, 1.5);
            }
        }

        /// <summary>
        /// 卷帘的可见区间落在本部件内的那一段（仅当卷帘正打开本部件时）。
        /// </summary>
        private Rect? PianoRollViewportRect() {
            if (part is not UVoicePart || part != partsCanvas.PianoRollOpenPart || pianoRollViewViewportTicks <= 0) {
                return null;
            }
            const double inset = 1;
            double innerWidth = Math.Max(0, Width - 2 * inset);
            double innerHeight = Math.Max(0, Height - 2 * inset);
            double vpLeft = Math.Max(0, pianoRollViewTickOffset * tickWidth);
            double vpRight = Math.Min(innerWidth, (pianoRollViewTickOffset + pianoRollViewViewportTicks) * tickWidth);
            if (vpRight <= vpLeft + 1) {
                return null;
            }
            return new Rect(inset + vpLeft, inset, vpRight - vpLeft, innerHeight);
        }

        /// <summary>指示条正中的拖拽把手（放不下就不给）。</summary>
        private static Rect? GripRect(Rect viewport) {
            const double width = 3 * GripDot + 2 * GripGap;
            const double height = 3 * GripDot + 2 * GripGap;
            if (viewport.Width < width + 6 || viewport.Height < height + 6) {
                return null;
            }
            return new Rect(
                Math.Round(viewport.Center.X - width / 2),
                Math.Round(viewport.Center.Y - height / 2),
                width, height);
        }

        /// <summary>点（本控件坐标）是否落在卷帘视口指示条的拖拽把手上。</summary>
        public bool HitPianoRollViewportHandle(Point point) {
            return PianoRollViewportRect() is Rect vpRect
                && GripRect(vpRect) is Rect grip
                && grip.Inflate(new Thickness(6, 8)).Contains(point);
        }

        // ── 拖拽指示条滚动卷帘（上游 9caec1a6）────────────────────────────
        // 上游把拖拽入口放在 MainWindow 的部件指针状态机里（布局线文件）⇒ 这里改为
        // **本控件自带指针处理**：命中把手即捕获指针，拖出的位移经 MessageBus 交给卷帘
        // （PianoRoll 侧钳制后写入 NotesViewModel.TickOffset）。只改视口，不入撤销栈。
        protected override void OnPointerMoved(PointerEventArgs e) {
            base.OnPointerMoved(e);
            var point = e.GetPosition(this);
            if (draggingViewport) {
                double deltaTicks = tickWidth > 0 ? (point.X - viewportDragStartX) / tickWidth : 0;
                MessageBus.Current.SendMessage(new PianoRollViewportScrollEvent(viewportDragStartOffset + deltaTicks));
                e.Handled = true;
                return;
            }
            Cursor = HitPianoRollViewportHandle(point) ? HandCursors.Grab : null;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e) {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) {
                return;
            }
            if (!HitPianoRollViewportHandle(e.GetPosition(this))) {
                return;
            }
            draggingViewport = true;
            viewportDragStartX = e.GetPosition(this).X;
            viewportDragStartOffset = partsCanvas.PianoRollViewTickOffset;
            Cursor = HandCursors.Grabbing;
            e.Pointer.Capture(this);
            e.Handled = true;   // 别让编排区的画布指针处理把这次按下当成"点部件"
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e) {
            base.OnPointerReleased(e);
            if (!draggingViewport) {
                return;
            }
            draggingViewport = false;
            Cursor = null;
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) {
            base.OnPointerCaptureLost(e);
            draggingViewport = false;
            Cursor = null;
        }

        private WriteableBitmap GetBitmap(double width) {
            int w = 128 * (int)(width / 128 + 1);
            if (bitmap == null || bitmap.Size.Width < w) {
                bitmap?.Dispose();
                var size = new PixelSize(w, (int)ViewConstants.TrackHeightMax);
                Log.Information($"created bitmap {size}");
                bitmap = new WriteableBitmap(
                    size, new Vector(96, 96),
                    Avalonia.Platform.PixelFormat.Rgba8888,
                    Avalonia.Platform.AlphaFormat.Unpremul);
                bitmapData = new int[size.Width * size.Height];
            }
            return bitmap;
        }

        private void DrawWaveform(UWavePart wavePart, WriteableBitmap bitmap, Color color) {
            if (wavePart.Peaks == null ||
                !wavePart.Peaks.IsCompletedSuccessfully ||
                wavePart.Peaks.Result == null) {
                return;
            }
            var wholePeaks = wavePart.Peaks.Result;
            int skipCount = (int)(wavePart.peaksSampleRate * wavePart.GetSkipMs(Core.DocManager.Inst.Project) / 1000);
            if (skipCount >= wholePeaks[0].Length) return;

            double height = TrackHeight;
            double monoChnlAmp = (height - 4.0) / 2;
            double stereoChnlAmp = (height - 6.0) / 4;

            var timeAxis = Core.DocManager.Inst.Project.timeAxis;
            DiscreteSignal[] peaks = new DiscreteSignal[wholePeaks.Length];
            for (int i = 0; i < wholePeaks.Length; i++) {
                var newSamples = wholePeaks[i].Samples.Skip(skipCount);
                peaks[i] = new DiscreteSignal(wavePart.peaksSampleRate, newSamples);
            }
            int x = 0;
            if (TickOffset <= wavePart.position) {
                // Part starts in or to the right of view.
                x = (int)(TickWidth * (wavePart.position - TickOffset));
            }
            int posTick = (int)(TickOffset + x / TickWidth);
            double posMs = timeAxis.TickPosToMsPos(posTick);
            double offsetMs = timeAxis.TickPosToMsPos(wavePart.position);
            int sampleIndex = (int)(wavePart.peaksSampleRate * (posMs - offsetMs) * 0.001);
            sampleIndex = Math.Clamp(sampleIndex, 0, peaks[0].Length);
            using (var frameBuffer = bitmap.Lock()) {
                Array.Clear(bitmapData, 0, bitmapData.Length);
                while (x < frameBuffer.Size.Width) {
                    if (posTick >= wavePart.position + wavePart.Duration) {
                        break;
                    }
                    int nextPosTick = (int)(TickOffset + (x + 1) / TickWidth);
                    double nexPosMs = timeAxis.TickPosToMsPos(nextPosTick);
                    int nextSampleIndex = (int)(wavePart.peaksSampleRate * (nexPosMs - offsetMs) * 0.001);
                    nextSampleIndex = Math.Clamp(nextSampleIndex, 0, peaks[0].Length);
                    if (nextSampleIndex > sampleIndex) {
                        for (int i = 0; i < peaks.Length; ++i) {
                            var segment = new ArraySegment<float>(peaks[i].Samples, sampleIndex, nextSampleIndex - sampleIndex);
                            float min = segment.Min();
                            float max = segment.Max();
                            double ySpan = peaks.Length == 1 ? monoChnlAmp : stereoChnlAmp;
                            double yOffset = i == 1 ? monoChnlAmp : 0;
                            DrawPeak(bitmapData, frameBuffer.Size.Width, x,
                                (int)(ySpan * (1 + -min) + yOffset) + 2,
                                (int)(ySpan * (1 + -max) + yOffset) + 2, color);
                        }
                    }
                    x++;
                    posTick = nextPosTick;
                    posMs = nexPosMs;
                    sampleIndex = nextSampleIndex;
                }
                Marshal.Copy(bitmapData, 0, frameBuffer.Address, bitmapData.Length);
            }
        }

        private void DrawPeak(int[] data, int width, int x, int y1, int y2, Color color) {
            int rgba = unchecked((int)(0xFF000000u | (uint)(color.B << 16) | (uint)(color.G << 8) | color.R));
            if (y1 > y2) {
                int temp = y2;
                y2 = y1;
                y1 = temp;
            }
            for (var y = y1; y <= y2; ++y) {
                data[x + width * y] = rgba;
            }
        }

        public void Report(int value) {
        }

        public void Dispose() {
            bitmap?.Dispose();
            unbinds.ForEach(u => u.Dispose());
            unbinds.Clear();
        }
    }
}

using System;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using ReactiveUI;

namespace OpenUtau.App.Controls {
    class TrackBackground : TemplatedControl {
        public static readonly StyledProperty<bool> IsArrangementProperty =
            AvaloniaProperty.Register<TrackBackground, bool>(nameof(IsArrangement));
        public bool IsArrangement { get => GetValue(IsArrangementProperty); set => SetValue(IsArrangementProperty, value); }
        public static readonly DirectProperty<TrackBackground, double> TrackHeightProperty =
            AvaloniaProperty.RegisterDirect<TrackBackground, double>(
                nameof(TrackHeight),
                o => o.TrackHeight,
                (o, v) => o.TrackHeight = v);
        public static readonly DirectProperty<TrackBackground, double> TrackOffsetProperty =
            AvaloniaProperty.RegisterDirect<TrackBackground, double>(
                nameof(TrackOffset),
                o => o.TrackOffset,
                (o, v) => o.TrackOffset = v);
        public static readonly DirectProperty<TrackBackground, bool> IsPianoRollProperty =
            AvaloniaProperty.RegisterDirect<TrackBackground, bool>(
                nameof(IsPianoRoll),
                o => o.IsPianoRoll,
                (o, v) => o.IsPianoRoll = v);
        public static readonly DirectProperty<TrackBackground, bool> IsKeyboardProperty =
            AvaloniaProperty.RegisterDirect<TrackBackground, bool>(
                nameof(IsKeyboard),
                o => o.IsKeyboard,
                (o, v) => o.IsKeyboard = v);
        public static readonly DirectProperty<TrackBackground, int> KeyProperty =
            AvaloniaProperty.RegisterDirect<TrackBackground, int>(
                nameof(Key),
                o => o.Key,
                (o, v) => o.Key = v);

        public double TrackHeight {
            get => _trackHeight;
            private set => SetAndRaise(TrackHeightProperty, ref _trackHeight, value);
        }
        public double TrackOffset {
            get => _trackOffset;
            private set => SetAndRaise(TrackOffsetProperty, ref _trackOffset, value);
        }
        public bool IsPianoRoll {
            get => _isPianoRoll;
            set => SetAndRaise(IsPianoRollProperty, ref _isPianoRoll, value);
        }
        public bool IsKeyboard {
            get => _isKeyboard;
            set => SetAndRaise(IsKeyboardProperty, ref _isKeyboard, value);
        }
        public int Key {
            get => _key;
            set => SetAndRaise(KeyProperty, ref _key, value);
        }

        private double _trackHeight;
        private double _trackOffset;
        private bool _isPianoRoll;
        private bool _isKeyboard;
        private int _key;

        public TrackBackground() {
            MessageBus.Current.Listen<ThemeChangedEvent>()
                .Subscribe(e => InvalidateVisual());
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == TrackHeightProperty ||
                change.Property == TrackOffsetProperty ||
                change.Property == ForegroundProperty ||
                change.Property == KeyProperty ||
                change.Property == IsKeyboardProperty ||
                change.Property == IsPianoRollProperty ||
                change.Property == IsArrangementProperty) {
                InvalidateVisual();
            }
        }

        int mod(int a, int b){
            return (a % b + b) % b;
        }

        public override void Render(DrawingContext context) {
            if (TrackHeight == 0) {
                return;
            }
            if (IsKeyboard) {
                RenderKeyboard(context);
                return;
            }
            if (IsArrangement) {
                RenderArrangement(context);
                return;
            }
            int track = (int)TrackOffset;
            double top = TrackHeight * (track - TrackOffset);
            while (top < Bounds.Height) {
                bool isAltTrack = IsAltTrack(track) ^ ThemeManager.IsDarkMode;
                context.DrawRectangle(isAltTrack ? Foreground : Background, null,
                    new Rect(0, (int)top, Bounds.Width, TrackHeight));
                track++;
                top += TrackHeight;
            }
        }

        void RenderArrangement(DrawingContext context) {
            var surface = ArrangementVisuals.Brush("md3.surface", ThemeManager.BackgroundBrush);
            var alternate = ArrangementVisuals.Brush("md3.surface-container-low", ThemeManager.BackgroundBrush);
            var edge = new Pen(ArrangementVisuals.Brush("md3.outline-variant", ThemeManager.NeutralAccentBrush), .75);
            int track = (int)TrackOffset;
            double top = TrackHeight * (track - TrackOffset);
            using (context.PushClip(new Rect(Bounds.Size))) {
                while (top < Bounds.Height) {
                    context.DrawRectangle(track % 2 == 0 ? surface : alternate, null, new Rect(0, top, Bounds.Width, TrackHeight));
                    using (context.PushOpacity(.55)) {
                        context.DrawLine(edge, new Point(0, Math.Round(top) + .5), new Point(Bounds.Width, Math.Round(top) + .5));
                    }
                    track++;
                    top += TrackHeight;
                }
            }
        }

        // Piano materials stay light/dark in both themes; accent is reserved for a held key.
        static readonly IBrush darkWhite = new SolidColorBrush(Color.Parse("#CDC8D1"));
        static readonly IBrush lightWhite = new SolidColorBrush(Color.Parse("#F5F1F7"));
        static readonly IBrush darkBlack = new SolidColorBrush(Color.Parse("#27242D"));
        static readonly IBrush lightBlack = new SolidColorBrush(Color.Parse("#343039"));
        static readonly IBrush whiteInk = new SolidColorBrush(Color.Parse("#48414F"));
        static readonly IBrush blackInk = new SolidColorBrush(Color.Parse("#E9E2ED"));
        static readonly IPen darkKeyEdge = new Pen(new SolidColorBrush(Color.Parse("#948B9E")), 0.75);
        static readonly IPen lightKeyEdge = new Pen(new SolidColorBrush(Color.Parse("#B7AEBF")), 0.75);
        int? pressedTone;
        internal int? PressedTone {
            get => pressedTone;
            set { if (pressedTone != value) { pressedTone = value; InvalidateVisual(); } }
        }

        internal Rect KeyboardKeyBounds(int tone) => PianoKeyboardGeometry.KeyBounds(tone, Bounds.Width, TrackHeight, TrackOffset);
        internal int KeyboardToneAt(Point point) => PianoKeyboardGeometry.ToneAt(point, Bounds.Width, TrackHeight, TrackOffset);

        void RenderKeyboard(DrawingContext context) {
            if (Bounds.Width <= 0 || TrackHeight <= 0) return;
            var white = ThemeManager.IsDarkMode ? darkWhite : lightWhite;
            var black = ThemeManager.IsDarkMode ? darkBlack : lightBlack;
            var edge = ThemeManager.IsDarkMode ? darkKeyEdge : lightKeyEdge;
            int highest = Math.Clamp((int)Math.Ceiling(ViewConstants.MaxTone - TrackOffset + 2), 0, ViewConstants.MaxTone - 1);
            int lowest = Math.Clamp((int)Math.Floor(ViewConstants.MaxTone - TrackOffset - Bounds.Height / TrackHeight - 3), 0, ViewConstants.MaxTone - 1);
            using (context.PushClip(new Rect(Bounds.Size))) {
                context.DrawRectangle(white, null, new Rect(Bounds.Size));
                // White keys meet between their pitch centers; black keys overlay those boundaries.
                for (int tone = highest; tone >= lowest; tone--) {
                    if (MusicMath.IsBlackKey(tone)) continue;
                    var rect = KeyboardKeyBounds(tone);
                    context.DrawRectangle(pressedTone == tone ? ThemeManager.AccentBrush2 : white, edge, rect);
                    DrawKeyLabel(context, tone, whiteInk);
                }
                for (int tone = highest; tone >= lowest; tone--) {
                    if (!MusicMath.IsBlackKey(tone)) continue;
                    var rect = KeyboardKeyBounds(tone);
                    context.DrawRectangle(pressedTone == tone ? ThemeManager.AccentBrush2 : black, null, rect, 3, 3);
                    DrawKeyLabel(context, tone, blackInk);
                }
                context.DrawLine(edge, new Point(Bounds.Width - .5, 0), new Point(Bounds.Width - .5, Bounds.Height));
            }
        }

        void DrawKeyLabel(DrawingContext context, int tone, IBrush ink) {
            if (TrackHeight < 12) return;
            double y = (ViewConstants.MaxTone - 1 - tone - TrackOffset + .5) * TrackHeight;
            bool black = MusicMath.IsBlackKey(tone);
            // Octaves provide orientation without turning the keyboard into a dense note list.
            if (!black && (MusicMath.IsCenterKey(tone) || pressedTone == tone)) {
                var label = TextLayoutCache.Get(MusicMath.GetToneName(tone), ink, 11, true);
                using (context.PushTransform(Matrix.CreateTranslation(Bounds.Width - 5 - label.Width, y - label.Height / 2))) {
                    label.Draw(context, new Point());
                }
            }
            string[]? degrees = Preferences.Default.DegreeStyle switch {
                1 => MusicMath.Solfeges,
                2 => MusicMath.NumberedNotations,
                _ => null,
            };
            if (degrees != null && TrackHeight >= 18) {
                var degree = TextLayoutCache.Get(degrees[mod(tone - Key, 12)], ink, 10);
                using (context.PushTransform(Matrix.CreateTranslation(4, y - degree.Height / 2))) {
                    degree.Draw(context, new Point());
                }
            }
        }

        private bool IsAltTrack(int track) {
            if (!IsPianoRoll) {
                return track % 2 == 1;
            }
            int tone = ViewConstants.MaxTone - 1 - track;
            if (tone < 0) {
                return false;
            }
            return MusicMath.IsBlackKey(tone);
        }

    }
}

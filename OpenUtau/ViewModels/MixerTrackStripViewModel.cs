using System.Linq;
using System.Windows.Input;
using OpenUtau.App.Editing;
using Avalonia.Media;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtau.App.ViewModels {
    public class MixerTrackStripViewModel : ViewModelBase {
        private readonly UTrack _track;
        public TrackMixEditSession Edits { get; }
        public int TrackNo => _track.TrackNo;
        public string TrackName => _track.TrackName;
        public IBrush TrackColor => ThemeManager.GetTrackColor(_track.TrackColor).AccentColor;
        [Reactive] public double Volume { get; set; }
        [Reactive] public double Pan { get; set; }
        [Reactive] public bool Mute { get; set; }
        [Reactive] public bool Solo { get; set; }
        public ICommand ToggleMuteCmd { get; }
        public ICommand ToggleSoloCmd { get; }

        public MixerTrackStripViewModel(UTrack track) {
            _track = track;
            Edits = new TrackMixEditSession(track);
            Volume = track.Volume;
            Pan = track.Pan * 100.0;
            Mute = track.Mute;
            Solo = track.Solo;
            ToggleMuteCmd = ReactiveCommand.Create(() => {
                Edits.SetMute(!_track.Mute);
                Refresh();
            });
            ToggleSoloCmd = ReactiveCommand.Create(() => {
                Edits.SetSolo(!_track.Solo);
                Refresh();
            });
        }

        public bool ApplyVolume(double db) {
            bool changed = Edits.SetVolume(db);
            Refresh();
            return changed;
        }

        public bool ApplyPan(double value) {
            bool changed = Edits.SetPan(value);
            Refresh();
            return changed;
        }

        public void Refresh() {
            Volume = _track.Volume;
            Pan = _track.Pan * 100.0;
            Mute = _track.Mute;
            Solo = _track.Solo;
            this.RaisePropertyChanged(nameof(TrackName));
            this.RaisePropertyChanged(nameof(TrackColor));
            this.RaisePropertyChanged(nameof(PanText));
            this.RaisePropertyChanged(nameof(EqLowDb));
            this.RaisePropertyChanged(nameof(EqMidFreq));
            this.RaisePropertyChanged(nameof(EqMidDb));
            this.RaisePropertyChanged(nameof(EqHighDb));
            this.RaisePropertyChanged(nameof(EqActive));
        }

        public bool IsSilent => Mute || Volume <= -24;

        // ── 通道条显示值（只读投影，不改模型）────────────────────

        /// <summary>声像文本：设计稿 65-68 的 `C` / `L12` / `R15`。</summary>
        public string PanText => FormatPan(Pan);

        /// <summary>-100..100 → `C` / `L##` / `R##`（|值|&lt;0.5 视为居中）。</summary>
        public static string FormatPan(double pan) {
            int v = (int)System.Math.Round(System.Math.Clamp(pan, -100, 100));
            if (v == 0) {
                return "C";
            }
            return v < 0 ? $"L{-v}" : $"R{v}";
        }

        // EQ 曲线屏取值：没有 MixFx 时按平坦响应（0dB）画，绝不显示虚构的曲线。
        UMixFx? FirstEq => _track.EffectRack == null ? _track.MixFx : _track.EffectRack.FirstOrDefault(e => e.Kind == TrackEffectKind.Eq)?.Settings;
        public double EqLowDb => FirstEq?.EqLowDb ?? 0;
        public double EqMidFreq => FirstEq?.EqMidFreq ?? 1000;
        public double EqMidDb => FirstEq?.EqMidDb ?? 0;
        public double EqHighDb => FirstEq?.EqHighDb ?? 0;

        /// <summary>EQ 是否真的在链上生效（整机架开 + EQ 模块开）——决定曲线屏是否降透明度。</summary>
        public bool EqActive => FirstEq?.Enabled == true && FirstEq.EqEnabled;
    }
}

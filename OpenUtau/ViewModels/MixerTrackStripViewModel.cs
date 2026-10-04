using System.Windows.Input;
using Avalonia.Media;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtau.App.ViewModels {
    public class MixerTrackStripViewModel : ViewModelBase {
        private readonly UTrack _track;
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
            Volume = track.Volume;
            Pan = track.Pan * 100.0;
            Mute = track.Mute;
            Solo = track.Solo;
            ToggleMuteCmd = ReactiveCommand.Create(() => {
                Mute = !Mute;
                _track.Mute = Mute; _track.Muted = Mute;
                var vn = new VolumeChangeNotification(TrackNo, Mute ? -24 : Volume);
                DocManager.Inst.StartUndoGroup();
                DocManager.Inst.ExecuteCmd(TrackMixCommands.Mute(_track, Mute));
                DocManager.Inst.EndUndoGroup();
                DocManager.Inst.ExecuteCmd(vn);
                MessageBus.Current.SendMessage(vn);
                MessageBus.Current.SendMessage(new TracksMuteEvent(TrackNo, false));
            });
            ToggleSoloCmd = ReactiveCommand.Create(() => {
                Solo = !Solo;
                _track.Solo = Solo;
                DocManager.Inst.StartUndoGroup();
                DocManager.Inst.ExecuteCmd(TrackMixCommands.Solo(_track, Solo));
                DocManager.Inst.EndUndoGroup();
                MessageBus.Current.SendMessage(new TracksSoloEvent(TrackNo, Solo, false));
            });
        }

        public void ApplyVolume(double db) {
            db = System.Math.Clamp(db, -24, 12);
            Volume = db;
            _track.Volume = db;
        }

        public void ApplyPan(double value) {
            var clamped = System.Math.Clamp(value, -100, 100);
            Pan = clamped;
            _track.Pan = clamped / 100.0;
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
        public double EqLowDb => _track.MixFx?.EqLowDb ?? 0;
        public double EqMidFreq => _track.MixFx?.EqMidFreq ?? 1000;
        public double EqMidDb => _track.MixFx?.EqMidDb ?? 0;
        public double EqHighDb => _track.MixFx?.EqHighDb ?? 0;

        /// <summary>EQ 是否真的在链上生效（整机架开 + EQ 模块开）——决定曲线屏是否降透明度。</summary>
        public bool EqActive => _track.MixFx?.Enabled == true && _track.MixFx.EqEnabled;
    }
}

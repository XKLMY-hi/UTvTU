using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using Avalonia;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtau.App.ViewModels {
    public class MixerViewModel : ViewModelBase, ICmdSubscriber {
        public ObservableCollection<UTrack> Tracks { get; } = new();

        [Reactive] public double MasterVolume { get; set; } = 0;
        [Reactive] public bool HasProject { get; set; }

        public ReactiveCommand<UTrack, Unit> OpenMixFxCommand { get; }

        public MixerViewModel() {
            OpenMixFxCommand = ReactiveCommand.Create<UTrack>(track => {
                if (track == null) return;
                if (Application.Current?.ApplicationLifetime is
                    Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop) {
                    // 与轨道头同一个入口：每轨单窗、非模态；已开着则聚焦（Open 内部处理）。
                    Views.MixFxDialog.Open(desktop.MainWindow, track);
                }
            });

            if (DocManager.Inst.Project?.tracks != null) {
                RefreshTracks();
            }
            DocManager.Inst.AddSubscriber(this);
        }

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is LoadProjectNotification || cmd is WillRemoveTrackNotification || cmd is TrackCommand) {
                // TrackCommand 覆盖新建/删除/移动/重命名轨道（含撤销重做）
                RefreshTracks();
            }
        }

        /// <summary>新建轨道（混音台工具行入口，可撤销）。</summary>
        public void AddTrack() {
            var project = DocManager.Inst.Project;
            if (project == null) {
                return;
            }
            DocManager.Inst.StartUndoGroup("command.track.add");
            DocManager.Inst.ExecuteCmd(new AddTrackCommand(project, new UTrack(project) { TrackNo = project.tracks.Count }));
            DocManager.Inst.EndUndoGroup();
        }

        public void RefreshTracks() {
            Tracks.Clear();
            var project = DocManager.Inst.Project;
            if (project?.tracks == null) {
                HasProject = false;
                return;
            }
            foreach (var t in project.tracks) {
                Tracks.Add(t);
            }
            HasProject = Tracks.Count > 0;
        }

        public static string FormatVolume(double db) {
            if (db <= -24) return "-∞ dB";
            return $"{db:+0.0;-0.0} dB";
        }
    }
}

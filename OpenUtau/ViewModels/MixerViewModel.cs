using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using Avalonia;
using Avalonia.Threading;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtau.App.ViewModels {
    public class MixerViewModel : ViewModelBase, ICmdSubscriber {
        public ObservableCollection<UTrack> Tracks { get; } = new();

        [Reactive] public double MasterVolume { get; set; } = 0;
        [Reactive] public bool HasProject { get; set; }

        /// <summary>
        /// 当前选中轨道（冻结接口 §1.1-2）：混音台右侧效果链面板消费该属性，
        /// 链面板不反向写 MixerViewModel。通道条点击/键选经 <see cref="SelectTrack"/> 更新。
        /// </summary>
        [Reactive] public UTrack? SelectedTrack { get; set; }

        /// <summary>选中轨道；null = 无选中。同一对象不重复通知。</summary>
        public void SelectTrack(UTrack? track) {
            if (ReferenceEquals(SelectedTrack, track)) {
                return;
            }
            SelectedTrack = track;
        }

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

        readonly UiThreadAffinity affinity = new UiThreadAffinity();

        /// <summary>刷新通道条（UI 线程亲和，见 <see cref="UiThreadAffinity"/>）。</summary>
        public void RefreshTracks() {
            // 跨线程编组（与链面板同法）：Tracks 变更会让 MixerControl 重建通道条（动视觉树），
            // 而命令通知在测试宿主等场景可能落在非 UI 线程 ⇒ 不编组会抛 Dispatcher.VerifyAccess。
            // 生产路径由 DocManager 的主线程守卫兜住，这里只是把契约显式化。
            // 判据用 `UiThreadAffinity`（锚定**订阅时所属线程**）而非 `CheckAccess()`：
            // 后者在 headless 测试宿主下会误判为 true（我们已因此踩过两次）；
            // 投递目标直指 Core，避免入队后再次进入本方法自我循环。
            affinity.Post(RefreshTracksCore);
        }

        void RefreshTracksCore() {
            Tracks.Clear();
            var project = DocManager.Inst.Project;
            if (project?.tracks == null) {
                HasProject = false;
                SelectTrack(null);
                return;
            }
            foreach (var t in project.tracks) {
                Tracks.Add(t);
            }
            HasProject = Tracks.Count > 0;
            // 轨道删除/重建后，选中项必须仍在列表里；空选中时默认选第一条（链面板有内容可显示）。
            if (SelectedTrack == null || !Tracks.Contains(SelectedTrack)) {
                SelectTrack(Tracks.FirstOrDefault());
            }
        }

        public static string FormatVolume(double db) {
            if (db <= -24) return "-∞ dB";
            return $"{db:+0.0;-0.0} dB";
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;

namespace OpenUtau.App.Views {
    /// <summary>
    /// 一条轨道的实时效果机架（Track Polish / 三面板）。
    ///
    /// **非模态**：窗口不阻塞主窗，所以可以边播边转旋钮，每次编辑都实时可听。
    /// 每轨最多一个窗口（<see cref="Open"/> 会聚焦已开的那个）。
    ///
    /// 提交语义：确定 = 保留；取消 / ESC = 恢复到窗口打开时的设置快照；
    /// 直接点标题栏关闭 = 保留（等同确定，只是不写库偏好）。
    /// </summary>
    public partial class MixFxDialog : WindowEx, ICmdSubscriber {
        static readonly Dictionary<UTrack, MixFxDialog> open = new Dictionary<UTrack, MixFxDialog>();

        readonly MixFxViewModel viewModel;
        readonly UTrack? track;
        // 取消 / ESC 走过 Revert：关窗时不要把它当成"保留改动"再标记一次工程已修改。
        bool reverted;
        // 确定走过 Apply（已经标记过工程已修改），关窗时不再重复标记。
        bool applied;

        public MixFxDialog() : this(null) { }

        public MixFxDialog(UTrack? track) {
            InitializeComponent();
            this.track = track;
            DataContext = viewModel = new MixFxViewModel(track);
            viewModel.AskForName = PromptForNameAsync;
            // 轨道头上的 fx 指示灯跟随总电源开关。
            viewModel.WhenAnyValue(x => x.Enabled).Subscribe(_ => NotifyTrackHeader());
            if (track != null) {
                DocManager.Inst.AddSubscriber(this);
            }
            // ESC = 取消（恢复到打开时的设置）。用路由事件而非重写 OnKeyDown：
            // 弹层自己处理掉的按键不会冒泡到这里。
            AddHandler(KeyDownEvent, (_, e) => {
                if (e.Key == Key.Escape) {
                    Cancel();
                    e.Handled = true;
                }
            });
            Closed += OnDialogClosed;
        }

        /// <summary>打开 <paramref name="track"/> 的机架；已开着就把它抬到最前。</summary>
        public static void Open(Window? owner, UTrack track) {
            if (open.TryGetValue(track, out var existing)) {
                existing.Activate();
                return;
            }
            var dialog = new MixFxDialog(track);
            open[track] = dialog;
            if (owner != null) {
                dialog.Show(owner);
            } else {
                dialog.Show();
            }
        }

        protected void OnDialogClosed(object? sender, EventArgs e) {
            if (track == null) {
                return;
            }
            DocManager.Inst.RemoveSubscriber(this);
            if (open.TryGetValue(track, out var dialog) && dialog == this) {
                open.Remove(track);
            }
            if (!reverted && !applied) {
                // 直接关窗 = 保留改动（确定路径已经在 Apply 里标记过，这里只在"脏"时补一次）。
                viewModel.MarkModifiedIfDirty();
            }
            NotifyTrackHeader();
        }

        /// <summary>
        /// 换工程或轨道被删（含撤销掉它的新增）时关闭窗口，并**保留**改动——
        /// 撤销"删除轨道"后轨道回来时还是它最后一次听到的样子。重命名则跟随。
        /// </summary>
        public void OnNext(UCommand cmd, bool isUndo) {
            if (track == null || !(cmd is TrackCommand || cmd is LoadProjectNotification)) {
                return;
            }
            Dispatcher.UIThread.Post(() => {
                if (!open.TryGetValue(track, out var dialog) || dialog != this) {
                    return;
                }
                if (cmd is LoadProjectNotification || DocManager.Inst.Project?.tracks?.Contains(track) != true) {
                    // 不是取消：改动照常保留（OnDialogClosed 里补标记）。
                    Close();
                } else {
                    viewModel.TrackName = track.TrackName;
                }
            });
        }

        void NotifyTrackHeader() {
            if (track != null) {
                MessageBus.Current.SendMessage(new MixFxChangedNotification(track.TrackNo));
            }
        }

        Task<string?> PromptForNameAsync() {
            var tcs = new TaskCompletionSource<string?>();
            var dialog = new TypeInDialog();
            dialog.Title = ThemeManager.GetString("mixfx.library.save");
            dialog.SetText(string.Empty);
            string? captured = null;
            dialog.onFinish = name => {
                if (!string.IsNullOrWhiteSpace(name)) captured = name;
            };
            dialog.Closed += (_, __) => tcs.TrySetResult(captured);
            dialog.ShowDialog(this);
            return tcs.Task;
        }

        /// <summary>确定：保留当前设置，并写回"导出时套用"偏好。</summary>
        void OnOkClicked(object sender, RoutedEventArgs e) {
            applied = true;
            viewModel.Apply();
            NotifyTrackHeader();
            Close();
        }

        /// <summary>取消：恢复到窗口打开时的设置。ESC 走同一条路。</summary>
        void OnCancelClicked(object sender, RoutedEventArgs e) => Cancel();

        void Cancel() {
            reverted = true;
            viewModel.Revert();
            NotifyTrackHeader();
            Close();
        }
    }
}

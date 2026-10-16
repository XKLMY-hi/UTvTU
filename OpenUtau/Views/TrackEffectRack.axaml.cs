using System;
using System.Collections.Generic;
using Avalonia.Controls;
using OpenUtau.Core;
using Avalonia.Input;
using OpenUtau.App.Controls;
using OpenUtau.Core.Ustx;

namespace OpenUtau.App.Views {
    /// <summary>One shared, non-modal workspace rack per track. Closing retains live edits.</summary>
    public partial class TrackEffectRack : WorkspaceToolView, ICmdSubscriber {
        static readonly Dictionary<UTrack, TrackEffectRack> open = new();
        readonly UTrack? track;
        readonly UiThreadAffinity affinity = new();
        public static void Open(Window? owner, UTrack track) {
            if (open.TryGetValue(track, out var existing) && !existing.IsClosed) { existing.Activate(); return; }
            var rack = new TrackEffectRack(track);
            open[track] = rack;
            if (owner != null) rack.Show(owner); else rack.Show();
        }
        public void OnNext(UCommand cmd, bool isUndo) {
            if (track == null || cmd is not (TrackCommand or LoadProjectNotification)) return;
            affinity.Post(() => {
                if (IsClosed) return;
                if (cmd is LoadProjectNotification || !DocManager.Inst.Project.tracks.Contains(track)) Close();
                else Title = $"{ThemeManager.GetString("effects.title")} · {track.TrackName}";
            });
        }
        protected override void OnClosed(EventArgs e) {
            DocManager.Inst.RemoveSubscriber(this);
            if (track != null && open.TryGetValue(track, out var current) && ReferenceEquals(current, this)) open.Remove(track);
            if (Content is FxChainPanel panel) panel.Shutdown();
            base.OnClosed(e);
        }
        public TrackEffectRack() : this(new UTrack()) { }

        public TrackEffectRack(UTrack track) {
            InitializeComponent();
            this.track = track;
            DocManager.Inst.AddSubscriber(this);
            // 内容就是 FxChainPanel（不使用 x:Name 字段：SukiWindow 派生窗口的字段填充
            // 在部分加载路径上不可靠 —— 直接取 Content，零查找依赖）
            if (Content is FxChainPanel panel) {
                panel.Track = track;
            }
            Title = $"{ThemeManager.GetString("effects.title")} · {track.TrackName}";
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape) {
                Close();
            }
        }
    }
}

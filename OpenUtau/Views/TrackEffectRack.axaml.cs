using System;
using Avalonia.Controls;
using Avalonia.Input;
using OpenUtau.App.Controls;
using OpenUtau.Core.Ustx;

namespace OpenUtau.App.Views {
    /// <summary>
    /// 单轨效果链的分离窗（**并入后的兜底宿主**，原 VST 槽机架已退役）。
    ///
    /// 迁移说明（对照表见交付报告）：
    /// · 原窗口的 `fx => track.MixFx ??= new()`（开窗即改工程）与"开窗即写 3 个默认槽"
    ///   两个 bug 一并消除 —— 新面板**只读呈现**，只有用户动作才走命令写模型；
    /// · VST 能力（增删槽 / 选插件 / 旁通 / 打开原生 GUI / 槽位 UID 与加载状态 / 异步加载通知）
    ///   全部由 <see cref="FxChainPanel"/> + <see cref="ViewModels.FxChainViewModel"/> 承接；
    /// · 内置三件套的预设库 / 总电源 / 导出套用仍在 `MixFxDialog`（轨道头 fx 入口，决策 R6）。
    ///
    /// 保留本窗口的两个理由：① 混音台通道条的 FX 入口直接引用本类型与构造函数
    /// （`Controls/MixerTrackStrip.axaml.cs`，W1 范围，保留 = 跨线零编译风险）；
    /// ② 不切到混音台视图时也能打开某条轨的链。内容与右侧面板是**同一个控件**，无第二套实现。
    /// </summary>
    public partial class TrackEffectRack : WindowEx {
        public TrackEffectRack() : this(new UTrack()) { }

        public TrackEffectRack(UTrack track) {
            InitializeComponent();
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

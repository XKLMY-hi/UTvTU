using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenUtau.Core;

namespace OpenUtau.App.Controls;

/// <summary>
/// 混音台主输出条（设计规格 Mixer.txt:691-736）。音量经
/// <see cref="PlaybackManager.ApplyMasterVolume"/> 实时作用于 masterMix（MasterAdapter.Scale）。
///
/// **Bus Info 四行的数据来源（规划 R5：不许编数）**：
/// · 综合响度 —— Core 没有 LUFS 计，显示占位「—」（ToolTip 说明原因）；
/// · 真峰值 —— MasterAdapter 的**样本峰值**（非过采样真峰），如实标 dBFS；
/// · 限制器 —— 播放/导出链路都没有限制器实现，显示状态文本「关」；
/// · 抖动 —— 导出固定 16-bit PCM 且不做抖动（ExportSession → CreateWaveFile16），显示「16-bit」。
///
/// 双表说明：Core 只提供**全声道单一峰值**（MasterAdapter.ReadAndResetPeakDb），
/// 没有 L/R 分流 ⇒ 设计稿的双表由同一个真实值驱动（视觉忠实 + 数据诚实，不做假的左右差异）。
/// </summary>
public partial class MasterStrip : UserControl {
    private readonly MixerMeter meter = new MixerMeter();
    private bool isDragging;
    private double masterDb = 0;

    public MasterStrip() {
        InitializeComponent();
        FaderBox.SizeChanged += (s, e) => UpdateFaderPosition();
        FaderBox.AddHandler(PointerReleasedEvent, OnFaderReleased,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        FaderBox.AddHandler(PointerCaptureLostEvent, OnFaderCaptureLost,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        InitBusInfo();
        UpdateMuteBtn();
        UpdateFaderPosition();
    }

    /// <summary>
    /// Bus Info：静态三项（响度占位 / 限制器状态 / 抖动状态）+ 每项来源说明。
    /// 真峰值行由 <see cref="UpdateLevel"/> 实时写。
    /// </summary>
    private void InitBusInfo() {
        IntegratedValue.Text = "—";
        ToolTip.SetTip(IntegratedValue, ThemeManager.GetString("mixer.bus.tip.integrated"));
        LimiterValue.Text = ThemeManager.GetString("button.off");
        ToolTip.SetTip(LimiterValue, ThemeManager.GetString("mixer.bus.tip.limiter"));
        DitherValue.Text = "16-bit";
        ToolTip.SetTip(DitherValue, ThemeManager.GetString("mixer.bus.tip.dither"));
        ToolTip.SetTip(TruePeakValue, ThemeManager.GetString("mixer.bus.tip.truepeak"));
        ToolTip.SetTip(MasterPeakLabel, ThemeManager.GetString("mixer.bus.tip.truepeak"));
    }

    /// <summary>
    /// 主输出电平（TrackLevels.ReadMasterAndReset 数据源 = MasterAdapter 采样峰，33ms 轮询）。
    /// 双表同值（见类型注释）。
    /// </summary>
    public void UpdateLevel(float rawPeakDb) {
        meter.Push(rawPeakDb);
        meter.Apply(MeterFillL, MixerMetrics.FaderHeight);
        meter.Apply(MeterFillR, MixerMetrics.FaderHeight);
        MasterPeakLabel.Text = FormatDb(meter.PeakDb);
        TruePeakValue.Text = $"{FormatDbNumber(meter.PeakDb)} dBFS";
    }

    /// <summary>-60dB 及以下显示 -∞（与推子读数同口径）。</summary>
    private static string FormatDb(double db) => $"{FormatDbNumber(db)} dB";

    private static string FormatDbNumber(double db) =>
        db <= MixerMeter.MinDb ? "-∞" : $"{db:+0.0;-0.0}";

    // ── 主输出静音（W1b：设计稿缺此元素，属有意偏离——见文件头）──

    /// <summary>静音态 → 键外观（error / on-error），与通道条 M 键同一套语义。</summary>
    private void UpdateMuteBtn() {
        MuteBtn.Classes.Set("muteOn", PlaybackManager.Inst.MasterMuted);
    }

    /// <summary>
    /// 与既有路径一致：直接切 <see cref="PlaybackManager.SetMasterMuted"/>（即时作用于
    /// masterMix.Scale = 0），不新造通知/命令机制；静音时推子柄落到底（沿用旧行为）。
    /// </summary>
    private void OnMuteClick(object? sender, RoutedEventArgs e) {
        PlaybackManager.Inst.SetMasterMuted(!PlaybackManager.Inst.MasterMuted);
        UpdateMuteBtn();
        UpdateFaderPosition();
    }

    // ── 推子 ───────────────────────────────────────────

    private void UpdateFaderPosition() {
        if (FaderBox.Bounds.Height <= 0) return;
        double db = PlaybackManager.Inst.MasterMuted ? MixerMetrics.FaderMinDb : masterDb;
        Canvas.SetTop(FaderHandle,
            MixerMetrics.FaderTop(db, FaderBox.Bounds.Height, MixerMetrics.MasterHandleHeight));
    }

    private void ApplyMasterVolume(double db) {
        db = Math.Clamp(db, MixerMetrics.FaderMinDb, MixerMetrics.FaderMaxDb);
        masterDb = db;
        PlaybackManager.Inst.ApplyMasterVolume(db);
        DocManager.Inst.ExecuteCmd(new MasterVolumeChangeNotification(db));
        UpdateFaderPosition();
    }

    // ── 鼠标 ───────────────────────────────────────────

    private void OnFaderPressed(object? sender, PointerPressedEventArgs e) {
        isDragging = true;
        e.Pointer.Capture(FaderBox);
        ApplyMasterVolume(MixerMetrics.FaderTopToDb(e.GetPosition(FaderBox).Y,
            FaderBox.Bounds.Height, MixerMetrics.MasterHandleHeight));
        e.Handled = true;
    }

    private void OnFaderMoved(object? sender, PointerEventArgs e) {
        if (!isDragging) return;
        ApplyMasterVolume(MixerMetrics.FaderTopToDb(e.GetPosition(FaderBox).Y,
            FaderBox.Bounds.Height, MixerMetrics.MasterHandleHeight));
        e.Handled = true;
    }

    private void OnFaderReleased(object? sender, PointerEventArgs e) {
        isDragging = false;
        e.Pointer.Capture(null);
    }

    private void OnFaderCaptureLost(object? sender, PointerCaptureLostEventArgs e) {
        isDragging = false;
    }
}

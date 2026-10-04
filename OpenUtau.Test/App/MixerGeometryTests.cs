using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Test.TestSupport;
using ReactiveUI;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// 混音台几何契约（设计规格 `.opencode/design/spec/Mixer.txt`）。
    ///
    /// 这里断言的是**属性 / 几何 / 契约**，不是像素：本仓 headless 走 `UseHeadlessDrawing`
    /// 桩绘制，`RenderTargetBitmap` 是空白位面。XAML 里的每个规格字面量都在这里被逐条锁住，
    /// 同时 `MixerMetrics` 里的常数也被要求等于同一批设计稿数值 —— 三处（设计稿 / 常数 / XAML）
    /// 任何一处漂移都会让本文件变红。
    /// </summary>
    [Collection("Theme")]
    public class MixerGeometryTests {
        // 设计稿原文数值（Mixer.txt 行号写在断言前）
        static readonly double[] TickTops = { 0, 62, 125, 187, 249, 311, 374, 436, 498 };
        static readonly double[] TickWidths = { 8, 5, 8, 5, 8, 5, 8, 5, 8 };

        const double StripWidth = 96;          // 43
        const double StripPadding = 8;         // 43
        const double StripRadius = 12;         // 43
        const double StripGap = 6;             // 43
        const double AccentHeight = 3;         // 44
        const double NameFontSize = 11;        // 45
        const double NameLineHeight = 12;      // 45
        const double EqWidth = 80;             // 47
        const double EqHeight = 76;            // 47
        const double EqRadius = 8;             // 47
        const double EqZeroY = 38;             // 48
        const double PanRowHeight = 14;        // 65
        const double PanLabelSize = 8;         // 66
        const double PanValueSize = 9;         // 68
        const double ButtonHeight = 20;        // 71/74
        const double ButtonRadius = 4;         // 71/74
        const double ButtonFontSize = 10;      // 72/75
        const double ButtonGap = 4;            // 70
        const double FaderWidth = 80;          // 81
        const double FaderHeight = 500;        // 81
        const double MeterWidth = 8;           // 82
        const double MeterLeft = 4;            // 82
        const double TrackWidth = 4;           // 84
        const double TrackLeft = 34;           // 84
        const double HandleWidth = 24;         // 85
        const double HandleHeight = 14;        // 85
        const double HandleLeft = 24;          // 85
        const double TickLeft = 52;            // 86-94
        const double DbFontSize = 9;           // 95
        const double MasterWidth = 160;        // 691
        const double MasterNameSize = 12;      // 693
        const double MasterSubtitleSize = 8;   // 695
        const double BusPadding = 10;          // 697
        const double BusGap = 8;               // 697
        const double BusRadius = 8;            // 697
        const double BusRowHeight = 14;        // 698/703/708/713
        const double BusLabelSize = 8;         // 699/704/709/714
        const double BusValueSize = 9;         // 701/706/711/716
        const double BusFaderWidth = 144;      // 719
        const double BusMeterWidth = 10;       // 720/722
        const double BusFaderTrackLeft = 74;   // 724
        const double BusHandleHeight = 16;     // 725
        const double BusHandleLeft = 64;       // 725
        const double BusTickLeft = 96;         // 726-734
        const double BusPeakSize = 13;         // 735

        static Border[] Ticks(Canvas fader) => fader.Children.OfType<Border>()
            .Where(b => b.Name != null && b.Name.StartsWith("FaderTick"))
            .ToArray();

        // ── 规格常数（代码侧唯一事实来源）────────────────────

        [Fact]
        public void SpecConstants_EqualDesignValues() {
            Assert.Equal(StripWidth, MixerMetrics.StripWidth);
            Assert.Equal(StripPadding, MixerMetrics.StripPadding);
            Assert.Equal(StripRadius, MixerMetrics.StripCornerRadius);
            Assert.Equal(StripGap, MixerMetrics.StripGap);
            Assert.Equal(AccentHeight, MixerMetrics.AccentHeight);
            Assert.Equal(NameFontSize, MixerMetrics.NameFontSize);
            Assert.Equal(NameLineHeight, MixerMetrics.NameLineHeight);
            Assert.Equal(EqWidth, MixerMetrics.EqWidth);
            Assert.Equal(EqHeight, MixerMetrics.EqHeight);
            Assert.Equal(EqRadius, MixerMetrics.EqCornerRadius);
            Assert.Equal(EqZeroY, MixerMetrics.EqZeroLineY);
            Assert.Equal(PanRowHeight, MixerMetrics.PanRowHeight);
            Assert.Equal(PanLabelSize, MixerMetrics.PanLabelFontSize);
            Assert.Equal(PanValueSize, MixerMetrics.PanValueFontSize);
            Assert.Equal(ButtonHeight, MixerMetrics.ButtonHeight);
            Assert.Equal(ButtonRadius, MixerMetrics.ButtonCornerRadius);
            Assert.Equal(ButtonFontSize, MixerMetrics.ButtonFontSize);
            Assert.Equal(ButtonGap, MixerMetrics.ButtonGap);
            Assert.Equal(FaderWidth, MixerMetrics.FaderWidth);
            Assert.Equal(FaderHeight, MixerMetrics.FaderHeight);
            Assert.Equal(MeterWidth, MixerMetrics.MeterWidth);
            Assert.Equal(MeterLeft, MixerMetrics.MeterLeft);
            Assert.Equal(TrackWidth, MixerMetrics.FaderTrackWidth);
            Assert.Equal(TrackLeft, MixerMetrics.FaderTrackLeft);
            Assert.Equal(HandleWidth, MixerMetrics.FaderHandleWidth);
            Assert.Equal(HandleHeight, MixerMetrics.FaderHandleHeight);
            Assert.Equal(HandleLeft, MixerMetrics.FaderHandleLeft);
            Assert.Equal(TickLeft, MixerMetrics.TickLeft);
            Assert.Equal(TickTops, MixerMetrics.TickTops);
            Assert.Equal(TickWidths, MixerMetrics.TickWidths);
            Assert.Equal(DbFontSize, MixerMetrics.DbFontSize);
            Assert.Equal(MasterWidth, MixerMetrics.MasterWidth);
            Assert.Equal(MasterNameSize, MixerMetrics.MasterNameFontSize);
            Assert.Equal(MasterSubtitleSize, MixerMetrics.MasterSubtitleFontSize);
            Assert.Equal(BusPadding, MixerMetrics.BusInfoPadding);
            Assert.Equal(BusGap, MixerMetrics.BusInfoGap);
            Assert.Equal(BusRadius, MixerMetrics.BusInfoCornerRadius);
            Assert.Equal(BusRowHeight, MixerMetrics.BusRowHeight);
            Assert.Equal(BusFaderWidth, MixerMetrics.MasterFaderWidth);
            Assert.Equal(BusMeterWidth, MixerMetrics.MasterMeterWidth);
            Assert.Equal(BusFaderTrackLeft, MixerMetrics.MasterFaderTrackLeft);
            Assert.Equal(BusHandleHeight, MixerMetrics.MasterHandleHeight);
            Assert.Equal(BusHandleLeft, MixerMetrics.MasterHandleLeft);
            Assert.Equal(BusTickLeft, MixerMetrics.MasterTickLeft);
            Assert.Equal(BusPeakSize, MixerMetrics.MasterPeakFontSize);
        }

        // ── 通道条（Mixer.txt:43-96）─────────────────────────

        [AvaloniaFact]
        public void ChannelStrip_MatchesSpec() {
            var strip = new MixerTrackStrip(new UTrack { TrackNo = 0 });

            // 43：宽 96 / 内边距 8 / 圆角 12 / 列间距 6
            Assert.Equal(StripWidth, strip.Width);
            Assert.Equal(new Thickness(StripPadding), strip.StripCard.Padding);
            Assert.Equal(new CornerRadius(StripRadius), strip.StripCard.CornerRadius);
            Assert.Equal(StripGap, strip.RootGrid.RowSpacing);

            // 44：强调条满宽 3px 圆角 999
            Assert.Equal(AccentHeight, strip.AccentBar.Height);
            Assert.Equal(new CornerRadius(999), strip.AccentBar.CornerRadius);
            Assert.Equal(HorizontalAlignment.Stretch, strip.AccentBar.HorizontalAlignment);

            // 45：名称 11 / 行高 12 / semibold
            Assert.Equal(NameFontSize, strip.TrackNameLabel.FontSize);
            Assert.Equal(NameLineHeight, strip.TrackNameLabel.LineHeight);
            Assert.Equal(FontWeight.SemiBold, strip.TrackNameLabel.FontWeight);

            // 47-49：EQ 屏 80×76 圆角 8；零线 80×1 在 top 38；曲线满幅
            Assert.Equal(EqWidth, strip.EqDisplay.Width);
            Assert.Equal(EqHeight, strip.EqDisplay.Height);
            Assert.Equal(new CornerRadius(EqRadius), strip.EqDisplay.CornerRadius);
            Assert.Equal(EqWidth, strip.EqZeroLine.Width);
            Assert.Equal(1, strip.EqZeroLine.Height);
            Assert.Equal(EqZeroY, Canvas.GetTop(strip.EqZeroLine));
            Assert.Equal(EqWidth, strip.EqCurve.Width);
            Assert.Equal(EqHeight, strip.EqCurve.Height);

            // 65-68：声像行（视觉 14 / 命中区 22：14px 的行拖不动）
            Assert.Equal(PanRowHeight, strip.PanRow.Height);
            Assert.Equal(MixerMetrics.PanHitHeight, strip.PanHitArea.Height);
            Assert.True(strip.PanHitArea.Height > PanRowHeight);
            Assert.Equal(PanLabelSize, strip.PanLabel.FontSize);
            Assert.Equal(PanValueSize, strip.PanValueLabel.FontSize);
            Assert.Equal(HorizontalAlignment.Right, strip.PanValueLabel.HorizontalAlignment);

            // 70-76：M/S 高 20 / 圆角 4 / font 10 bold / 间距 4（裁定 R3：没有 R 键）
            foreach (var btn in new[] { strip.MuteBtn, strip.SoloBtn }) {
                Assert.Equal(ButtonHeight, btn.Height);
                Assert.Equal(new CornerRadius(ButtonRadius), btn.CornerRadius);
                Assert.Equal(ButtonFontSize, btn.FontSize);
                Assert.Equal(FontWeight.Bold, btn.FontWeight);
            }
            Assert.Equal(ButtonGap, strip.MsRow.ColumnSpacing);
            Assert.Equal(2, strip.MsRow.Children.Count);
            Assert.Null(strip.FindControl<Button>("ArmBtn"));

            // 81-85：推子区 80×500；表 8 宽 left 4 从底部长；轨 4 宽 left 34；柄 24×14 left 24
            Assert.Equal(FaderWidth, strip.FaderBox.Width);
            Assert.Equal(FaderHeight, strip.FaderBox.Height);
            Assert.Equal(MeterWidth, strip.MeterBar.Width);
            Assert.Equal(FaderHeight, strip.MeterBar.Height);
            Assert.Equal(MeterLeft, Canvas.GetLeft(strip.MeterBar));
            Assert.Equal(new CornerRadius(999), strip.MeterBar.CornerRadius);
            Assert.Equal(VerticalAlignment.Bottom, strip.MeterFill.VerticalAlignment);
            Assert.Equal(MeterWidth, strip.MeterFill.Width);
            Assert.Equal(new CornerRadius(999), strip.MeterFill.CornerRadius);
            Assert.Equal(TrackWidth, strip.FaderTrack.Width);
            Assert.Equal(FaderHeight, strip.FaderTrack.Height);
            Assert.Equal(TrackLeft, Canvas.GetLeft(strip.FaderTrack));
            Assert.Equal(new CornerRadius(999), strip.FaderTrack.CornerRadius);
            Assert.Equal(HandleWidth, strip.FaderHandle.Width);
            Assert.Equal(HandleHeight, strip.FaderHandle.Height);
            Assert.Equal(HandleLeft, Canvas.GetLeft(strip.FaderHandle));
            Assert.Equal(new CornerRadius(4), strip.FaderHandle.CornerRadius);

            // 86-94：9 条刻度，宽 8/5 交替，left 52，top 0…498
            var ticks = Ticks(strip.FaderBox);
            Assert.Equal(9, ticks.Length);
            for (int i = 0; i < ticks.Length; i++) {
                Assert.Equal(TickLeft, Canvas.GetLeft(ticks[i]));
                Assert.Equal(TickTops[i], Canvas.GetTop(ticks[i]));
                Assert.Equal(TickWidths[i], ticks[i].Width);
                Assert.Equal(1, ticks[i].Height);
            }

            // 95：推子读数 9px
            Assert.Equal(DbFontSize, strip.VolValueLabel.FontSize);
        }

        /// <summary>决策 B3 / 裁定 R1：通道条里没有插入列表、没有 FX 入口按钮。</summary>
        [AvaloniaFact]
        public void ChannelStrip_HasNoInsertsAndNoFxEntry() {
            var strip = new MixerTrackStrip(new UTrack { TrackNo = 0 });
            Assert.Null(strip.FindControl<Button>("FxEntryBtn"));
            Assert.Null(strip.FindControl<Control>("Inserts"));
            Assert.Null(strip.FindControl<TextBlock>("InsertSectionLabel"));
            // 也没有主输出的 Bus Info / 副标题
            Assert.Null(strip.FindControl<Border>("BusInfo"));
        }

        // ── 主输出条（Mixer.txt:691-736）─────────────────────

        [AvaloniaFact]
        public void MasterStrip_MatchesSpec() {
            var master = new MasterStrip();

            // 691：宽 160 / 内边距 8 / 圆角 12 / 列间距 6
            Assert.Equal(MasterWidth, master.Width);
            Assert.Equal(new Thickness(StripPadding), master.MasterCard.Padding);
            Assert.Equal(new CornerRadius(StripRadius), master.MasterCard.CornerRadius);
            Assert.Equal(StripGap, master.RootGrid.RowSpacing);

            // 692-695：强调条 3px；名称 12 bold；副标题 8
            Assert.Equal(AccentHeight, master.MasterAccentBar.Height);
            Assert.Equal(new CornerRadius(999), master.MasterAccentBar.CornerRadius);
            Assert.Equal(MasterNameSize, master.MasterNameLabel.FontSize);
            Assert.Equal(FontWeight.Bold, master.MasterNameLabel.FontWeight);
            Assert.Equal(MasterSubtitleSize, master.MasterSubtitleLabel.FontSize);

            // 697-717：Bus Info 四行，每行 14 高（标签 8 / 值 9，两端对齐）
            Assert.Equal(new Thickness(BusPadding), master.BusInfo.Padding);
            Assert.Equal(new CornerRadius(BusRadius), master.BusInfo.CornerRadius);
            var busGrid = Assert.IsType<Grid>(master.BusInfo.Child);
            Assert.Equal(BusGap, busGrid.RowSpacing);
            var rows = new (Grid Row, TextBlock Label, TextBlock Value)[] {
                (master.BusRowIntegrated, master.IntegratedLabel, master.IntegratedValue),
                (master.BusRowTruePeak, master.TruePeakLabel, master.TruePeakValue),
                (master.BusRowLimiter, master.LimiterLabel, master.LimiterValue),
                (master.BusRowDither, master.DitherLabel, master.DitherValue),
            };
            Assert.Equal(4, rows.Length);
            foreach (var (row, label, value) in rows) {
                Assert.Equal(BusRowHeight, row.Height);
                Assert.Equal(BusLabelSize, label.FontSize);
                Assert.Equal(BusValueSize, value.FontSize);
                Assert.Equal(HorizontalAlignment.Right, value.HorizontalAlignment);
            }

            // 719-734：推子区 144×500；**双表** 10 宽 left 24 / 38；轨 4 宽 left 74；柄 24×16 left 64；刻度 left 96
            Assert.Equal(BusFaderWidth, master.FaderBox.Width);
            Assert.Equal(FaderHeight, master.FaderBox.Height);
            Assert.Equal(BusMeterWidth, master.MeterBarL.Width);
            Assert.Equal(BusMeterWidth, master.MeterBarR.Width);
            Assert.Equal(24, Canvas.GetLeft(master.MeterBarL));
            Assert.Equal(38, Canvas.GetLeft(master.MeterBarR));
            Assert.Equal(FaderHeight, master.MeterBarL.Height);
            Assert.Equal(FaderHeight, master.MeterBarR.Height);
            Assert.Equal(VerticalAlignment.Bottom, master.MeterFillL.VerticalAlignment);
            Assert.Equal(VerticalAlignment.Bottom, master.MeterFillR.VerticalAlignment);
            Assert.Equal(TrackWidth, master.FaderTrack.Width);
            Assert.Equal(BusFaderTrackLeft, Canvas.GetLeft(master.FaderTrack));
            Assert.Equal(HandleWidth, master.FaderHandle.Width);
            Assert.Equal(BusHandleHeight, master.FaderHandle.Height);
            Assert.Equal(BusHandleLeft, Canvas.GetLeft(master.FaderHandle));
            Assert.Equal(new CornerRadius(4), master.FaderHandle.CornerRadius);
            var ticks = Ticks(master.FaderBox);
            Assert.Equal(9, ticks.Length);
            for (int i = 0; i < ticks.Length; i++) {
                Assert.Equal(BusTickLeft, Canvas.GetLeft(ticks[i]));
                Assert.Equal(TickTops[i], Canvas.GetTop(ticks[i]));
                Assert.Equal(TickWidths[i], ticks[i].Width);
            }

            // 735：峰值读数 13 semibold
            Assert.Equal(BusPeakSize, master.MasterPeakLabel.FontSize);
            Assert.Equal(FontWeight.SemiBold, master.MasterPeakLabel.FontWeight);
        }

        /// <summary>spec-digest §8 第 23 条：主输出没有插入区 / EQ 屏 / 声像 / M/S/R。</summary>
        [AvaloniaFact]
        public void MasterStrip_HasNoChannelOnlyParts() {
            var master = new MasterStrip();
            Assert.Null(master.FindControl<Button>("MuteBtn"));
            Assert.Null(master.FindControl<Button>("SoloBtn"));
            Assert.Null(master.FindControl<Control>("EqDisplay"));
            Assert.Null(master.FindControl<Control>("PanRow"));
        }

        /// <summary>
        /// 数据诚实（裁定 R5）：综合响度没有实现 ⇒ 占位「—」；限制器 / 抖动显示**当前导出设置的
        /// 状态文本**（关 / 16-bit），不是设计稿里的假数值（-14.0 LUFS / 开 / 24-bit）。
        /// </summary>
        [AvaloniaFact]
        public void MasterStrip_BusInfoShowsHonestStatus() {
            var master = new MasterStrip();
            Assert.Equal("—", master.IntegratedValue.Text);              // 无响度计：不编数
            Assert.DoesNotContain("LUFS", master.IntegratedValue.Text);
            Assert.Equal("16-bit", master.DitherValue.Text);             // ExportSession 固定 16-bit PCM
            Assert.NotEqual("24-bit", master.DitherValue.Text);
            Assert.False(string.IsNullOrWhiteSpace(master.LimiterValue.Text));
            Assert.NotEqual("开", master.LimiterValue.Text);              // 没有限制器实现，不显示"开"
            // 每一项的来源都写在 ToolTip 里
            Assert.False(string.IsNullOrWhiteSpace(ToolTip.GetTip(master.IntegratedValue) as string));
            Assert.False(string.IsNullOrWhiteSpace(ToolTip.GetTip(master.LimiterValue) as string));
            Assert.False(string.IsNullOrWhiteSpace(ToolTip.GetTip(master.DitherValue) as string));
            Assert.False(string.IsNullOrWhiteSpace(ToolTip.GetTip(master.TruePeakValue) as string));

            // 真峰值 = MasterAdapter 采样峰（真实值），单位如实写 dBFS（不是设计稿的 dBTP）
            master.UpdateLevel(-6f);
            Assert.Equal("-6.0 dB", master.MasterPeakLabel.Text);
            Assert.Equal("-6.0 dBFS", master.TruePeakValue.Text);
            Assert.DoesNotContain("dB dBFS", master.TruePeakValue.Text);
            Assert.DoesNotContain("dBTP", master.TruePeakValue.Text);   // 没过采样，不冒充真峰值计
            // 双表由同一个真实值驱动（Core 无 L/R 分流）
            Assert.Equal(master.MeterFillL.Height, master.MeterFillR.Height, 3);
            Assert.True(master.MeterFillL.Height > 0);
        }

        // ── 混音台外壳 + 冻结接口（§1.1）─────────────────────

        [AvaloniaFact]
        public void MixerControl_ExposesFrozenChainHost() {
            DocManagerTestSetup.RunOnCurrentThread();
            var mixer = new MixerControl();
            try {
                // §1.1-1：固定宽 280 的 ContentControl「FxChainHost」+ 左侧 1px outline-variant 分隔线
                Assert.NotNull(mixer.FxChainHost);
                Assert.Equal(280, mixer.FxChainHost.Width);
                Assert.Equal(1, mixer.FxChainDivider.Width);
                // 链宿主与分隔线都在**横向滚动区之外**（设计稿 13 条恰好填满 1440，链面板只能占滚动区外）
                Assert.Empty(mixer.StripsScroll.GetLogicalDescendants()
                    .Where(d => ReferenceEquals(d, mixer.FxChainHost)));
                Assert.Empty(mixer.StripsScroll.GetLogicalDescendants()
                    .Where(d => ReferenceEquals(d, mixer.FxChainDivider)));
                // 主输出条固定在滚动区之外（E5）
                Assert.Empty(mixer.StripsScroll.GetLogicalDescendants()
                    .Where(d => ReferenceEquals(d, mixer.MasterStripControl)));
                Assert.Equal(MasterWidth, mixer.MasterStripControl.Width);
            } finally {
                mixer.Shutdown();
            }
        }

        [AvaloniaFact]
        public void MixerControl_AreaPaddingAndGapMatchSpec() {
            DocManagerTestSetup.RunOnCurrentThread();
            var mixer = new MixerControl();
            try {
                // 42：Mixer Area 内边距 16 / 通道条间距 8
                Assert.Equal(new Thickness(16, 16, 8, 16), mixer.TrackStripsPanel.Margin);
                Assert.Equal(MixerMetrics.AreaGap, mixer.TrackStripsPanel.Spacing);
                Assert.Equal(Orientation.Horizontal, mixer.TrackStripsPanel.Orientation);
            } finally {
                mixer.Shutdown();
            }
        }

        // ── 选中轨道（冻结接口 §1.1-2）───────────────────────

        [AvaloniaFact]
        public void SelectedTrack_IsExposedAndSelectable() {
            var vm = new MixerViewModel();
            try {
                var track = new UTrack { TrackNo = 3 };
                vm.SelectTrack(track);
                Assert.Same(track, vm.SelectedTrack);
                vm.SelectTrack(track);      // 同一对象不重复通知（不影响语义，只是稳定）
                Assert.Same(track, vm.SelectedTrack);
            } finally {
                DocManager.Inst.RemoveSubscriber(vm);
            }
        }

        /// <summary>点击通道条 → 抛 Selected（混音台据此更新 SelectedTrack）；已选中不重复抛。</summary>
        [AvaloniaFact]
        public void ChannelStrip_RaisesSelectedOnPointerPress() {
            var strip = new MixerTrackStrip(new UTrack { TrackNo = 4 });
            int raised = 0;
            strip.Selected += (_, _) => raised++;

            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            void Press() => strip.StripCard.RaiseEvent(new PointerPressedEventArgs(
                strip.StripCard, pointer, strip.StripCard, new Point(10, 10), 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
                KeyModifiers.None, 1));

            Press();
            Assert.Equal(1, raised);
            strip.IsSelected = true;
            Assert.True(strip.StripCard.Classes.Contains("selected"));
            Press();
            Assert.Equal(1, raised);
        }
    }

    /// <summary>
    /// 电平表 / EQ 曲线 / 推子映射的纯计算契约（33ms 轮询语义）。
    /// 这些公式就是 Render 与定时器真正用的那一份，不是另写一遍。
    /// </summary>
    [Collection("Theme")]
    public class MixerMeterContractTests {
        [Fact]
        public void Meter_AttackIsInstant_ReleaseFallsTwoDbPerTick() {
            var meter = new MixerMeter();
            meter.Push(0);
            Assert.Equal(0, meter.LevelDb, 3);
            Assert.Equal(1.0, meter.Fraction, 3);
            meter.Push(MixerMeter.MinDb);
            Assert.Equal(-MixerMeter.FallPerTickDb, meter.LevelDb, 3);
            Assert.Equal(MixerMeter.MaxDb, meter.PeakDb, 3);      // 峰值不跟着掉
        }

        [Fact]
        public void Meter_PeakHoldsThenDecaysSlowly() {
            var meter = new MixerMeter();
            meter.Push(-6);
            for (int i = 0; i < MixerMeter.PeakHoldTicks; i++) {
                meter.Push(MixerMeter.MinDb);
            }
            Assert.Equal(-6, meter.PeakDb, 3);                    // 保持期内不动
            meter.Push(MixerMeter.MinDb);
            Assert.True(meter.PeakDb < -6, "保持期结束后峰值应缓降");
            Assert.True(meter.PeakDb > MixerMeter.MinDb);
        }

        [AvaloniaFact]
        public void Meter_FillGrowsFromBottomAndClamps() {
            var fill = new Border();
            var meter = new MixerMeter();
            meter.Push(0);
            meter.Apply(fill, MixerMetrics.FaderHeight);
            Assert.Equal(MixerMetrics.FaderHeight, fill.Height, 3);
            meter.Reset();
            meter.Apply(fill, MixerMetrics.FaderHeight);
            Assert.Equal(0d, fill.Height, 3);
            Assert.Equal(0d, MixerMeter.DbToFraction(-120), 3);
            Assert.Equal(1d, MixerMeter.DbToFraction(6), 3);
        }

        [Fact]
        public void EqCurve_FlatResponseSitsOnZeroLine() {
            var curve = new MixerEqCurve { LowDb = 0, MidDb = 0, HighDb = 0, MidFreq = 1000 };
            var plot = new Rect(0, 0, MixerMetrics.EqWidth, MixerMetrics.EqHeight);
            var points = curve.BuildCurvePoints(plot, 32);
            Assert.Equal(MixerMetrics.EqZeroLineY, plot.Center.Y, 3);   // 76/2 = 38 = 零线
            Assert.All(points, p => Assert.Equal(MixerMetrics.EqZeroLineY, p.Y, 3));
            Assert.Equal(0, points[0].X, 3);                            // 20 Hz 贴左边缘（满幅）
            Assert.Equal(MixerMetrics.EqWidth, points[^1].X, 3);        // 20 kHz 贴右边缘
        }

        [Fact]
        public void EqCurve_MidBoostRaisesCurveAroundOneKilohertz() {
            var curve = new MixerEqCurve { MidDb = 6, MidFreq = 1000 };
            var plot = new Rect(0, 0, MixerMetrics.EqWidth, MixerMetrics.EqHeight);
            var points = curve.BuildCurvePoints(plot, 200);
            double x1k = MixerEqCurve.FrequencyToX(1000, plot);
            var nearest = points.OrderBy(p => Math.Abs(p.X - x1k)).First();
            Assert.True(nearest.Y < MixerMetrics.EqZeroLineY, "1kHz 提升 6dB 应画在零线上方（Y 更小）");
            // 端点仍收敛回 0dB 附近（低架/高架之外）
            Assert.True(MixerEqCurve.LevelToY(6, plot) < MixerEqCurve.LevelToY(0, plot));
        }

        [Fact]
        public void FaderMapping_RoundTripsAndHitsBothEnds() {
            Assert.Equal(0, MixerMetrics.FaderTop(MixerMetrics.FaderMaxDb,
                MixerMetrics.FaderHeight, MixerMetrics.FaderHandleHeight), 3);
            Assert.Equal(MixerMetrics.FaderHeight - MixerMetrics.FaderHandleHeight,
                MixerMetrics.FaderTop(MixerMetrics.FaderMinDb,
                    MixerMetrics.FaderHeight, MixerMetrics.FaderHandleHeight), 3);
            foreach (double db in new[] { -24.0, -12, -4.2, 0, 12 }) {
                double top = MixerMetrics.FaderTop(db, MixerMetrics.FaderHeight, MixerMetrics.FaderHandleHeight);
                Assert.Equal(db, MixerMetrics.FaderTopToDb(top,
                    MixerMetrics.FaderHeight, MixerMetrics.FaderHandleHeight), 3);
            }
        }

        [Theory]
        [InlineData(0, "C")]
        [InlineData(0.4, "C")]
        [InlineData(-12, "L12")]
        [InlineData(15, "R15")]
        [InlineData(-30, "L30")]
        [InlineData(-100, "L100")]
        [InlineData(100, "R100")]
        public void PanText_MatchesDesignSamples(double pan, string expected) {
            Assert.Equal(expected, MixerTrackStripViewModel.FormatPan(pan));
        }

        [Fact]
        public void PanText_FollowsPanValueProperty() {
            var vm = new MixerTrackStripViewModel(new UTrack { TrackNo = 0, Pan = -0.15 });
            Assert.Equal("L15", vm.PanText);
        }
    }

    /// <summary>
    /// reparent（视图 ↔ 分离窗口）与电平定时器的生命周期回归。
    /// 旧实现：`Unloaded → DisposeSubscriptions` 里把 ViewModel 置空、且没有重建路径
    /// ⇒ 分离/贴合一次后 VU 永久冻结、声像静默失效；定时器则构造即启、隐藏后仍在 30fps 轮询。
    /// </summary>
    [Collection("Theme")]
    public class MixerStripLifecycleTests {
        public MixerStripLifecycleTests() {
            DocManagerTestSetup.RunOnCurrentThread();
        }

        [AvaloniaFact]
        public void VuAndPan_SurviveReparent() {
            var track = new UTrack { TrackNo = 61 };
            var strip = new MixerTrackStrip(track);
            var host = new StackPanel();
            var window = new Window { Width = 200, Height = 800, Content = host };
            try {
                window.Show();
                host.Children.Add(strip);              // 挂载（贴合）
                strip.UpdateLevel(-6f);
                Assert.True(strip.MeterFill.Height > 0, "挂载后跳表");

                host.Children.Remove(strip);           // 分离（摘下可视树）
                strip.UpdateLevel(-6f);
                Assert.True(strip.MeterFill.Height > 0, "摘下后跳表不得冻结");

                host.Children.Add(strip);              // 贴合回来
                strip.UpdateLevel(-6f);
                Assert.True(strip.MeterFill.Height > 0, "重挂后跳表必须恢复");

                strip.PanValue = 40;                   // 重挂后声像仍写回模型
                Assert.Equal(0.4, track.Pan, 3);
            } finally {
                window.Close();
                strip.DisposeSubscriptions();
            }
        }

        [AvaloniaFact]
        public void VolumeNotification_IsRebuiltAfterReparent() {
            var track = new UTrack { TrackNo = 62 };
            var strip = new MixerTrackStrip(track);
            var host = new StackPanel();
            var window = new Window { Width = 200, Height = 800, Content = host };
            try {
                window.Show();
                host.Children.Add(strip);
                host.Children.Remove(strip);
                host.Children.Add(strip);              // 分离一次再贴合
                MessageBus.Current.SendMessage(new VolumeChangeNotification(track.TrackNo, -6));
                Assert.Equal("-6.0 dB", strip.VolValueLabel.Text);
            } finally {
                window.Close();
                strip.DisposeSubscriptions();
            }
        }

        [AvaloniaFact]
        public void LevelTimer_FollowsAttachAndVisibility() {
            var mixer = new MixerControl();
            try {
                Assert.False(mixer.LevelTimerRunning, "未挂载的混音台不该在轮询");

                var window = new Window { Width = 900, Height = 700, Content = mixer };
                window.Show();
                Assert.True(mixer.LevelTimerRunning, "挂载后应起表");

                mixer.IsVisible = false;
                Dispatcher.UIThread.RunJobs();
                Assert.False(mixer.LevelTimerRunning, "隐藏后应停表");

                mixer.IsVisible = true;
                Dispatcher.UIThread.RunJobs();
                Assert.True(mixer.LevelTimerRunning, "重新可见应起表");

                window.Content = null;
                Assert.False(mixer.LevelTimerRunning, "摘下后应停表");
            } finally {
                mixer.Shutdown();
            }
        }
    }
}

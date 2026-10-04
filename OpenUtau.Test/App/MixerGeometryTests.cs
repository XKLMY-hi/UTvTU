using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    /// 断言分两层，缺一不可：
    /// ① **属性层**（`*_MatchesSpec`）：XAML 里的规格字面量与 `MixerMetrics` 常数逐条对齐；
    /// ② **布局层**（`*_LayoutSizesMatchSpec`）：把控件放进**真宿主窗口**跑一次布局，
    ///    断言 `Bounds`（实际渲染尺寸）。
    /// 为什么要第二层（W6 真机像素 FAIL 的教训）：本仓 headless 是桩绘制，**但布局是真跑的**；
    /// 而全局 `Md3ButtonTheme` 设了 `MinHeight=32`（`Styles/Md3ControlThemes.axaml:28`），
    /// 布局取 `Max(MinHeight, Height)` ⇒ 只断言属性 `Height == 20` 的用例全绿，真机渲染却是 32。
    /// 两层都锁住，才能同时防"XAML 写错"与"被主题尺寸顶掉"。
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

        /// <summary>
        /// 把控件放进真宿主窗口、跑完布局后执行断言（布局层几何契约的场地）。
        /// headless 下 `UseHeadlessDrawing` 让绘制变桩，但 <see cref="Layoutable.Bounds"/>
        /// 是布局计算的真实结果 —— 主题 MinHeight/MinWidth 的顶替只在这一层可见。
        /// </summary>
        static void InWindow(Control content, double width, double height, Action body) {
            var window = new Window { Width = width, Height = height, Content = content };
            try {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                // 本地样式（MinHeight 覆盖主题）落定后再跑一遍布局：首次测量可能取到中间态
                content.InvalidateMeasure();
                Dispatcher.UIThread.RunJobs();
                body();
            } finally {
                window.Close();
            }
        }

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

        /// <summary>
        /// **布局层**几何契约（W10）：真宿主窗口 + 真布局下的实际渲染尺寸。
        /// 这一层专门抓"属性绿、像素红"—— W6 真机实测 M/S 高 32（主题 MinHeight 顶掉本地 Height），
        /// 而属性断言全绿。凡是"声明尺寸"的元素，这里都按 `Bounds` 复核一遍。
        /// </summary>
        [AvaloniaFact]
        public void ChannelStrip_LayoutSizesMatchSpec() {
            var strip = new MixerTrackStrip(new UTrack { TrackNo = 0 });
            InWindow(strip, 200, 900, () => {
                Assert.Equal(StripWidth, strip.Bounds.Width);

                // M/S（真机曾渲染 32）：本地 MinHeight 必须压住主题的 32、Margin 必须清零
                Assert.Equal(ButtonHeight, strip.MuteBtn.MinHeight);
                Assert.Equal(new Thickness(0), strip.MuteBtn.Margin);
                Assert.Equal(ButtonHeight, strip.MuteBtn.Bounds.Height);
                Assert.Equal(ButtonHeight, strip.SoloBtn.Bounds.Height);
                // 两钮等分行宽：96 − 内边距 16 − 间距 4 = 76 ⇒ 每钮 38
                Assert.Equal(38, strip.MuteBtn.Bounds.Width, 1);
                Assert.Equal(38, strip.SoloBtn.Bounds.Width, 1);
                // M/S 行 = 键高（稿里是 flex 行，无额外高）：无 margin 时行高必须回到 20
                Assert.Equal(ButtonHeight, strip.MsRow.Bounds.Height);

                Assert.Equal(AccentHeight, strip.AccentBar.Bounds.Height);
                Assert.Equal(EqWidth, strip.EqDisplay.Bounds.Width);
                Assert.Equal(EqHeight, strip.EqDisplay.Bounds.Height);
                Assert.Equal(EqWidth, strip.EqCurve.Bounds.Width);
                Assert.Equal(EqHeight, strip.EqCurve.Bounds.Height);
                Assert.Equal(1, strip.EqZeroLine.Bounds.Height);

                Assert.Equal(PanRowHeight, strip.PanRow.Bounds.Height);
                Assert.Equal(MixerMetrics.PanHitHeight, strip.PanHitArea.Bounds.Height);

                Assert.Equal(FaderWidth, strip.FaderBox.Bounds.Width);
                Assert.Equal(FaderHeight, strip.FaderBox.Bounds.Height);
                Assert.Equal(MeterWidth, strip.MeterBar.Bounds.Width);
                Assert.Equal(FaderHeight, strip.MeterBar.Bounds.Height);
                Assert.Equal(TrackWidth, strip.FaderTrack.Bounds.Width);
                Assert.Equal(HandleWidth, strip.FaderHandle.Bounds.Width);
                Assert.Equal(HandleHeight, strip.FaderHandle.Bounds.Height);

                var ticks = Ticks(strip.FaderBox);
                Assert.Equal(9, ticks.Length);
                for (int i = 0; i < ticks.Length; i++) {
                    Assert.Equal(TickWidths[i], ticks[i].Bounds.Width);
                    Assert.Equal(1, ticks[i].Bounds.Height);
                }
            });
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
            // W1b：静音键并入名称行（不新增独立行 —— 竖向余量只有 ≈8px）
            Assert.Equal(7, master.RootGrid.RowDefinitions.Count);
            Assert.Equal(2, master.MasterNameRow.Children.Count);

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

        /// <summary>
        /// spec-digest §8 第 23 条：主输出没有插入区 / EQ 屏 / 声像 / 独奏 / 录音。
        /// **唯一有意偏离（W1b / task-17）**：加回主输出静音键——否则 `SetMasterMuted` 没有 UI 入口。
        /// </summary>
        [AvaloniaFact]
        public void MasterStrip_HasNoChannelOnlyPartsExceptMute() {
            var master = new MasterStrip();
            Assert.Null(master.FindControl<Button>("SoloBtn"));
            Assert.Null(master.FindControl<Control>("EqDisplay"));
            Assert.Null(master.FindControl<Control>("PanRow"));
            Assert.NotNull(master.FindControl<Button>("MuteBtn"));   // W1b：有意偏离设计稿
        }

        /// <summary>
        /// W1b（task-17）：主输出静音 = 通道条 M 键语言（高 20 / 圆角 4 / 10px bold，复用 `mixer.mute` 键），
        /// 行为与既有 <see cref="PlaybackManager.SetMasterMuted"/> 一致：点击即切换、即时作用于 masterMix。
        /// </summary>
        [AvaloniaFact]
        public void MasterStrip_MuteKeyTogglesPlaybackMute() {
            var master = new MasterStrip();
            bool original = PlaybackManager.Inst.MasterMuted;
            try {
                Assert.Equal(20, master.MuteBtn.Height);
                Assert.Equal(new CornerRadius(4), master.MuteBtn.CornerRadius);
                Assert.Equal(10, master.MuteBtn.FontSize);
                Assert.Equal(FontWeight.Bold, master.MuteBtn.FontWeight);
                Assert.Equal(24, master.MuteBtn.Width);
                Assert.False(master.MuteBtn.Classes.Contains("muteOn"));

                master.MuteBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(PlaybackManager.Inst.MasterMuted);
                Assert.True(master.MuteBtn.Classes.Contains("muteOn"));

                master.MuteBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(PlaybackManager.Inst.MasterMuted);
                Assert.False(master.MuteBtn.Classes.Contains("muteOn"));
            } finally {
                PlaybackManager.Inst.SetMasterMuted(original);
            }
        }

        /// <summary>
        /// **布局层**几何契约（W10）：主输出静音键真机曾渲染 24×**31**（主题 MinHeight=32 顶掉
        /// 本地 Height=20），这里按布局后的 `Bounds` 复核；顺带把主条全部声明尺寸复核一遍。
        /// </summary>
        [AvaloniaFact]
        public void MasterStrip_LayoutSizesMatchSpec() {
            var master = new MasterStrip();
            InWindow(master, 260, 900, () => {
                Assert.Equal(MasterWidth, master.Bounds.Width);

                // 主输出静音（真机曾 24×31）：MinHeight 压 20 + Margin 清零
                Assert.Equal(ButtonHeight, master.MuteBtn.MinHeight);
                Assert.Equal(new Thickness(0), master.MuteBtn.Margin);
                Assert.Equal(ButtonHeight, master.MuteBtn.Bounds.Height);
                Assert.Equal(24, master.MuteBtn.Bounds.Width);
                // 键与名称同行：行高 = 键高（无 margin 撑高）
                Assert.Equal(ButtonHeight, master.MasterNameRow.Bounds.Height);

                Assert.Equal(AccentHeight, master.MasterAccentBar.Bounds.Height);

                // Bus Info：padding 10×2 + 四行 14 + 三个 gap 8 = 100
                Assert.Equal(BusPadding * 2 + BusRowHeight * 4 + BusGap * 3, master.BusInfo.Bounds.Height);
                foreach (var row in new[] {
                    master.BusRowIntegrated, master.BusRowTruePeak,
                    master.BusRowLimiter, master.BusRowDither,
                }) {
                    Assert.Equal(BusRowHeight, row.Bounds.Height);
                }

                Assert.Equal(BusFaderWidth, master.FaderBox.Bounds.Width);
                Assert.Equal(FaderHeight, master.FaderBox.Bounds.Height);
                Assert.Equal(BusMeterWidth, master.MeterBarL.Bounds.Width);
                Assert.Equal(BusMeterWidth, master.MeterBarR.Bounds.Width);
                Assert.Equal(FaderHeight, master.MeterBarL.Bounds.Height);
                Assert.Equal(TrackWidth, master.FaderTrack.Bounds.Width);
                Assert.Equal(HandleWidth, master.FaderHandle.Bounds.Width);
                Assert.Equal(BusHandleHeight, master.FaderHandle.Bounds.Height);

                var ticks = Ticks(master.FaderBox);
                Assert.Equal(9, ticks.Length);
                for (int i = 0; i < ticks.Length; i++) {
                    Assert.Equal(TickWidths[i], ticks[i].Bounds.Width);
                    Assert.Equal(1, ticks[i].Bounds.Height);
                }
            });
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

        /// <summary>
        /// 「属性绿、像素红」盲区的**金丝雀**（W10 教训固化）：
        /// 本仓有两条"继承来的几何"会改掉按钮的实际渲染尺寸 ——
        /// ① 主题 `Md3ButtonTheme.MinHeight=32`（ControlTheme）⇒ 裸 Button 即使 `Height=20` 也渲染 **32**；
        /// ② 应用级 `Button { Margin: 0,4 }`（`Styles/Styles.axaml:159`）⇒ 每个按钮外加 8px 竖向 margin。
        /// 混音台三处按钮（M/S、主输出静音、＋轨道）都在**本地**样式里显式压掉这两项；
        /// 第一条断言故意写得宽松（`> 20`），主题若调整 MinHeight 也不会假红，但"存在干涉"这件事必须为真。
        /// </summary>
        [AvaloniaFact]
        public void ThemeGeometryTraps_AreNeutralizedInMixerButtons() {
            var bare = new Button { Height = 20, Content = "M", FontSize = 10 };
            var pinned = new Button {
                Height = 20, MinHeight = 20, Margin = new Thickness(0),
                Content = "M", FontSize = 10,
            };
            InWindow(new StackPanel { Children = { bare, pinned } }, 200, 200, () => {
                // 干涉确实存在（裸按钮不会被 Height 限制住）
                Assert.True(bare.Bounds.Height > 20,
                    $"主题/应用级几何未干涉裸按钮（实测 {bare.Bounds.Height}）——若主题改了，请同步更新本注释与混音台本地样式");
                Assert.NotEqual(new Thickness(0), bare.Margin);
                // 本地两条 setter（MinHeight / Margin）即可完全中和
                Assert.Equal(20, pinned.Bounds.Height, 1);
            });
        }

        /// <summary>
        /// **布局层**几何契约（W10）：工具行按钮（28）与链宿主（280）的真实布局尺寸。
        /// 「＋轨道」按钮是本轮复核发现的**同一条主题陷阱第三处**：声明 Height=28，主题
        /// MinHeight=32 会把它顶到 32（本地 MinHeight/Margin 已压住）。
        /// </summary>
        [AvaloniaFact]
        public void MixerControl_LayoutSizesMatchSpec() {
            DocManagerTestSetup.RunOnCurrentThread();
            var mixer = new MixerControl();
            try {
                InWindow(mixer, 1400, 800, () => {
                    Assert.Equal(28, mixer.AddTrackBtn.MinHeight);
                    Assert.Equal(new Thickness(0), mixer.AddTrackBtn.Margin);
                    Assert.Equal(28, mixer.AddTrackBtn.Bounds.Height);
                    Assert.Equal(MixerMetrics.FxChainHostWidth, mixer.FxChainHost.Bounds.Width);
                    Assert.Equal(MixerMetrics.FxChainDividerWidth, mixer.FxChainDivider.Bounds.Width);
                });
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

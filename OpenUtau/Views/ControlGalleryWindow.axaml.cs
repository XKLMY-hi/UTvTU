using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using OpenUtau.App.Controls;
using OpenUtau.Core;
using OpenUtau.Core.Theming;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Vst;

namespace OpenUtau.App.Views;

/// <summary>
/// 控件陈列室（fx-rack）——新控件家族的一次性审阅入口。
///
/// 为什么用代码后置填一部分：冻结状态（<see cref="Md3RackVisual"/>）与真实数据
/// （一条临时 <see cref="UTrack"/>）都不是静态 XAML 值；页面骨架、文案、实时示例仍在
/// XAML 里声明（见 ControlGalleryWindow.axaml），这里只补三件事：
///   1) 分段选择器的选项（文案走字符串键 ⇒ 用 ThemeManager 取值，随语言切换重建窗口即生效）；
///   2) 每个控件的"冻结状态条"（默认/悬停/按下/激活/禁用/旁通）；
///   3) 机架演示数据（真实 UMixFx + VstPluginSlot；**不写回任何模型**）。
///
/// 本窗口**不需要运行中的工程**：演示轨道是本地构造的临时对象，
/// 空态面板用一条没有效果的轨道（MixFx == null）渲染。
/// </summary>
public partial class ControlGalleryWindow : WindowEx {
    /// <summary>机架演示用的临时轨道（本地构造，不挂到 DocManager / 工程上）。</summary>
    private readonly UTrack demoTrack = BuildDemoTrack();

    /// <summary>空态演示轨道：没有 MixFx、没有 VST 槽 ⇒ 面板显示空态。</summary>
    private readonly UTrack emptyTrack = new UTrack { TrackNo = 1, TrackName = "Empty Track" };

    public ControlGalleryWindow() {
        InitializeComponent();
        FillSegmentedOptions();
        FillStateStrips();
        ApplyAutomationNames();
        RackDemoPanel.Track = demoTrack;
        RackEmptyPanel.Track = emptyTrack;
    }

    /// <summary>
    /// 无障碍名字：一律取字符串键（不写死英文），屏幕阅读器读出的是当前语言的状态名。
    /// 状态条里的控件由 <see cref="AddCell"/> 顺带设置。
    /// </summary>
    private void ApplyAutomationNames() {
        AutomationProperties.SetName(PowerOff, StateName("controlgallery.state.default"));
        AutomationProperties.SetName(PowerOn, StateName("controlgallery.state.active"));
        AutomationProperties.SetName(PowerDisabled, StateName("controlgallery.state.disabled"));
        AutomationProperties.SetName(SegmentedLive, ThemeManager.GetString("controlgallery.section.segmented"));
        AutomationProperties.SetName(SegmentedCompact, ThemeManager.GetString("controlgallery.section.segmented"));
        AutomationProperties.SetName(SegmentedDisabled, ThemeManager.GetString("controlgallery.section.segmented"));
        AutomationProperties.SetName(ReadoutGain, ThemeManager.GetString("controlgallery.demo.gain"));
        AutomationProperties.SetName(ReadoutFreq, ThemeManager.GetString("controlgallery.demo.freq"));
        AutomationProperties.SetName(ReadoutMix, ThemeManager.GetString("controlgallery.demo.mix"));
        AutomationProperties.SetName(ReadoutDisabled, ThemeManager.GetString("controlgallery.demo.mix"));
        AutomationProperties.SetName(RackDemoPanel, ThemeManager.GetString("controlgallery.section.rack"));
        AutomationProperties.SetName(RackEmptyPanel, ThemeManager.GetString("controlgallery.section.rack"));
    }

    private static string StateName(string key) => ThemeManager.GetString(key);

    /// <summary>演示轨道：总开关开、压缩器关、两个 VST 槽（其一为空槽）。</summary>
    private static UTrack BuildDemoTrack() {
        var track = new UTrack { TrackNo = 0, TrackName = "Lead Vocal", TrackColor = "Blue" };
        track.MixFx = new UMixFx {
            Enabled = true,
            EqEnabled = true,
            CompEnabled = false,
            ReverbEnabled = true,
            EqPreset = "vocal_air",
            CompPreset = "gentle",
            ReverbPreset = "small_room",
        };
        track.VstSlots.Add(new VstPluginSlot(0) { PluginUid = "OTT" });
        track.VstSlots.Add(new VstPluginSlot(1));
        return track;
    }

    /// <summary>分段选择器的选项文本（全部复用既有字符串键，不新增文案）。</summary>
    private void FillSegmentedOptions() {
        string[] modules = {
            ThemeManager.GetString("mixfx.eq"),
            ThemeManager.GetString("mixfx.compressor"),
            ThemeManager.GetString("mixfx.reverb"),
        };
        // EQ 卡内：频段选择（复用既有的 Low/Mid/High 键）
        EqBandSegmented.SetOptions(
            ThemeManager.GetString("mixfx.eq.low"),
            ThemeManager.GetString("mixfx.eq.mid"),
            ThemeManager.GetString("mixfx.eq.high"));
        EqBandSegmented.SelectedIndex = 1;
        SegmentedLive.SetOptions(modules);
        SegmentedLive.SelectedIndex = 0;
        SegmentedCompact.SetOptions(ThemeManager.GetString("effects.on"), ThemeManager.GetString("effects.off"));
        SegmentedCompact.SelectedIndex = 0;
        SegmentedDisabled.SetOptions(modules);
        SegmentedDisabled.SelectedIndex = 2;
    }

    /// <summary>五个控件的冻结状态条。</summary>
    private void FillStateStrips() {
        // ── 电源开关 ──
        AddCell(PowerStateStrip, "controlgallery.state.default",
            new Md3PowerSwitch { IsChecked = false, ForcedVisual = Md3RackVisual.Default }, 92);
        AddCell(PowerStateStrip, "controlgallery.state.hover",
            new Md3PowerSwitch { IsChecked = false, ForcedVisual = Md3RackVisual.Hover }, 92);
        AddCell(PowerStateStrip, "controlgallery.state.pressed",
            new Md3PowerSwitch { IsChecked = true, ForcedVisual = Md3RackVisual.Pressed }, 92);
        AddCell(PowerStateStrip, "controlgallery.state.active",
            new Md3PowerSwitch { IsChecked = true, ForcedVisual = Md3RackVisual.Default }, 92);
        AddCell(PowerStateStrip, "controlgallery.state.disabled",
            new Md3PowerSwitch { IsChecked = false, IsEnabled = false, ForcedVisual = Md3RackVisual.Disabled }, 92);
        AddCell(PowerStateStrip, "controlgallery.state.bypass",
            new Md3PowerSwitch { IsChecked = true, ForcedVisual = Md3RackVisual.Bypassed }, 92);

        // ── 分段选择器 ──
        string[] modules = {
            ThemeManager.GetString("mixfx.eq"),
            ThemeManager.GetString("mixfx.compressor"),
            ThemeManager.GetString("mixfx.reverb"),
        };
        AddCell(SegmentedStateStrip, "controlgallery.state.default", NewSegment(modules, Md3RackVisual.Default), 300);
        AddCell(SegmentedStateStrip, "controlgallery.state.hover", NewSegment(modules, Md3RackVisual.Hover), 300);
        AddCell(SegmentedStateStrip, "controlgallery.state.pressed", NewSegment(modules, Md3RackVisual.Pressed), 300);
        AddCell(SegmentedStateStrip, "controlgallery.state.active", NewSegment(modules, Md3RackVisual.Focus), 300);
        AddCell(SegmentedStateStrip, "controlgallery.state.disabled", NewSegment(modules, Md3RackVisual.Disabled), 300);

        // ── 参数读数 ──
        AddCell(ReadoutStateStrip, "controlgallery.state.default", NewReadout(Md3RackVisual.Default), 150);
        AddCell(ReadoutStateStrip, "controlgallery.state.hover", NewReadout(Md3RackVisual.Hover), 150);
        AddCell(ReadoutStateStrip, "controlgallery.state.pressed", NewReadout(Md3RackVisual.Pressed), 150);
        AddCell(ReadoutStateStrip, "controlgallery.state.active", NewReadout(Md3RackVisual.Focus), 150);
        AddCell(ReadoutStateStrip, "controlgallery.state.disabled", NewReadout(Md3RackVisual.Disabled), 150);
        AddCell(ReadoutStateStrip, "controlgallery.state.bypass", NewReadout(Md3RackVisual.Bypassed), 150);

        // ── 模块面板 ──
        AddCell(CardStateStrip, "controlgallery.state.default", NewCard(Md3RackVisual.Default, null), 240);
        AddCell(CardStateStrip, "controlgallery.state.hover", NewCard(Md3RackVisual.Hover, null), 240);
        AddCell(CardStateStrip, "controlgallery.state.disabled", NewCard(Md3RackVisual.Disabled, null), 240);
        AddCell(CardStateStrip, "controlgallery.state.bypass", NewCard(Md3RackVisual.Bypassed, "controlgallery.state.bypass"), 240);
    }

    private static Md3SegmentedControl NewSegment(string[] options, Md3RackVisual visual) {
        var control = new Md3SegmentedControl {
            ForcedVisual = visual,
            Width = 280,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        control.SetOptions(options);
        control.SelectedIndex = 0;
        return control;
    }

    private static Md3NumericReadout NewReadout(Md3RackVisual visual) => new Md3NumericReadout {
        ForcedVisual = visual,
        Width = 138,
        HorizontalAlignment = HorizontalAlignment.Center,
        Unit = "dB",
        Minimum = -12,
        Maximum = 12,
        Value = 1.5,
        Step = 0.1,
        ResetValue = 0,
        Format = "+0.0;-0.0;0.0",
    };

    /// <summary>冻结状态展示卡（Title 由调用方给文案；内容区留一个读数件示意）。</summary>
    private static FxModuleCard NewCard(Md3RackVisual visual, string? titleKey) {
        string title = titleKey == null ? ThemeManager.GetString("mixfx.eq") : ThemeManager.GetString(titleKey);
        var card = new FxModuleCard {
            ForcedVisual = visual,
            Width = 228,
            Title = title,
            Subtitle = "vocal_air",
            Accent = Md3Role.Primary,
            IsPowered = visual != Md3RackVisual.Bypassed,
            CardContent = new Md3NumericReadout {
                Unit = "dB", Minimum = -12, Maximum = 12, Value = 1.5, Format = "+0.0;-0.0;0.0",
            },
        };
        return card;
    }

    /// <summary>一格 = 控件 + 下方状态标签（固定宽度，便于横向并排审阅）。</summary>
    private static void AddCell(Panel strip, string labelKey, Control control, double width) {
        control.HorizontalAlignment = HorizontalAlignment.Center;
        var cell = new StackPanel {
            Spacing = 6,
            Width = width,
            HorizontalAlignment = HorizontalAlignment.Left,
            Children = {
                control,
                new TextBlock {
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    [!TextBlock.TextProperty] = new DynamicResourceExtension(labelKey),
                    [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("md3.on-surface-variant"),
                },
            },
        };
        AutomationProperties.SetName(control, ThemeManager.GetString(labelKey));
        strip.Children.Add(cell);
    }
}

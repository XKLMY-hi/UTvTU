using System;
using System.Collections.Specialized;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.Util;
using OpenUtau.Core.Ustx;
using ReactiveUI;

namespace OpenUtau.App.Controls;

/// <summary>
/// 混音台主体：通道条行（横向滚动）+ 固定主输出条 + 右侧效果链宿主槽。
///
/// 生命周期（本轮修）：电平定时器**随挂载与可见性启停**——旧实现构造即启动、只有
/// <see cref="Shutdown"/> 才停，而 Shutdown 仅由分离窗关闭触发 ⇒ 视图隐藏后仍 30fps 轮询。
/// 现在 `Attached/Detached` 与 `LayoutUpdated`（可见性变化会触发布局）共同驱动，
/// 与 W2 的"视图化 / 分离-贴合"协同：贴合时挂载 → 起表，分离时重挂 → 起表，关闭 → 停表。
/// </summary>
public partial class MixerControl : UserControl
{
    internal readonly MixerViewModel ViewModel;
    private readonly DispatcherTimer levelTimer;
    private bool attached;
    private bool shutdown;
    private bool timerRunning;
    private bool _loggedFirstTick;
    /// <summary>视觉树所属线程（挂载时更新；未挂载时 = 构造线程）。RebuildStrips 的亲和判据。</summary>
    private Thread uiThread = Thread.CurrentThread;
    private IDisposable? _selectionSubscription;
    private IDisposable? _visibilitySubscription;
    /// <summary>右侧效果链面板（宿主装配见构造器；生命周期随本控件，无需窗口侧管理）。</summary>
    private readonly FxChainPanel chainPanel;

    /// <summary>
    /// W19：右侧效果链面板的宽/折叠状态（W16 面板系统 <see cref="PanelSlot"/>）。
    /// XAML 里 `#MixerRoot.ChainPanel` 把它喂给 `FxChainSplitter`（Target 两向绑定 = 用户意图）
    /// 与折叠键的图标；面板容器的 `Width`/`IsVisible` 取分隔条的 `PanelWidth`/`PanelShown`（夹紧后的有效值）。
    /// 状态落 <c>Preferences.Default.PanelLayout.MixerChainWidth/MixerChainCollapsed</c>（不新增平行存储）。
    /// </summary>
    public PanelSlot ChainPanel { get; } = new PanelSlot("fx-chain", 280, 264, 480);

    /// <summary>电平定时器是否在跑（挂载 × 可见性；回归测试与诊断用）。</summary>
    internal bool LevelTimerRunning => timerRunning;

    public MixerControl()
    {
        InitializeComponent();
        InitChainPanelLayout();
        DataContext = ViewModel = new MixerViewModel();
        RebuildStrips();
        ViewModel.Tracks.CollectionChanged += OnTracksChanged;
        // 选中轨道 → 通道条选中态（右侧链面板消费 ViewModel.SelectedTrack，见冻结接口 §1.1-2）
        _selectionSubscription = ViewModel.WhenAnyValue(x => x.SelectedTrack)
            .Subscribe(_ => SyncStripSelection());

        // 右侧效果链面板接线（冻结接口 §1.1；控件由 W3 提供，此处只做宿主装配）：
        // 面板消费 MixerViewModel.SelectedTrack，不反向写；未选轨时显示空态。
        chainPanel = new FxChainPanel();
        chainPanel.BindSelection(ViewModel.WhenAnyValue(x => x.SelectedTrack));
        FxChainHost.Content = chainPanel;

        // Forward space to main window
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Space)
            {
                e.Handled = true;
                var mainWindow = (Application.Current?.ApplicationLifetime
                    as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
                    ?.MainWindow;
                mainWindow?.Focus();
            }
        };

        // Poll track levels at ~30 fps for VU meter animation
        levelTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(33),
            DispatcherPriority.Render,
            OnLevelTimerTick);
        // 可见性驱动停表：自身 IsVisible 变化（同步）+ 祖先可见性变化（随布局）
        _visibilitySubscription = this.GetObservable(IsVisibleProperty).Subscribe(_ => SyncTimer());
        LayoutUpdated += (_, _) => {
            SyncStripsMinHeight();
            SyncTimer();
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
        base.OnAttachedToVisualTree(e);
        // 视觉树所属线程在挂载时确定：RebuildStrips 的线程亲和判据以它为准
        // （不用 Dispatcher.CheckAccess()——headless 测试宿主下会误判）。
        uiThread = Thread.CurrentThread;
        attached = true;
        SyncTimer();
    }

    // ── W19：链面板宽 / 折叠的持久化（写 Preferences.Default.PanelLayout 的自己那一段）──

    private void InitChainPanelLayout() {
        var prefs = Preferences.Default.PanelLayout;
        ChainPanel.Width = prefs.MixerChainWidth;
        ChainPanel.IsCollapsed = prefs.MixerChainCollapsed;
    }

    /// <summary>拖动结束 / 双击复位：此时才落盘（拖动过程只改内存，避免写爆磁盘）。</summary>
    private void OnChainPanelDragCompleted(object? sender, EventArgs e) => PersistChainPanelLayout();

    /// <summary>折叠处边缘标签的展开请求（W44 ②）：展开回**持久化宽度**并落盘。</summary>
    private void OnRevealChainPanel(object? sender, EventArgs e) {
        ChainPanel.IsCollapsed = false;
        PersistChainPanelLayout();
    }

    /// <summary>折叠键（工具行 chevron；折叠后仍常驻 ⇒ 一定能展开回来）。</summary>
    private void OnToggleChainPanel(object? sender, Avalonia.Interactivity.RoutedEventArgs e) {
        ChainPanel.ToggleCollapse();
        PersistChainPanelLayout();
    }

    /// <summary>把槽状态写回 Preferences 并落盘（与 W16 同一份存储，不新增字段之外的东西）。</summary>
    private void PersistChainPanelLayout() {
        var prefs = Preferences.Default.PanelLayout;
        prefs.MixerChainWidth = ChainPanel.Width;
        prefs.MixerChainCollapsed = ChainPanel.IsCollapsed;
        Preferences.Save();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
        base.OnDetachedFromVisualTree(e);
        attached = false;
        SyncTimer();
    }

    /// <summary>定时器只在"已挂载 且 实际可见"时运行（隐藏的混音台不该占 30fps）。</summary>
    private void SyncTimer() {
        bool shouldRun = attached && !shutdown && IsEffectivelyVisible;
        if (shouldRun == timerRunning) {
            return;
        }
        timerRunning = shouldRun;
        if (shouldRun) {
            levelTimer.Start();
        } else {
            levelTimer.Stop();
        }
        SyncStripsMinHeight();
    }

    /// <summary>
    /// 通道条撑到视口高度（设计稿 `h-full`）：竖向内容不足时卡片填满，内容超出
    /// （矮窗口 / 分离窗默认 720×480）时 MinHeight 不生效，由竖向滚动兜底。
    /// 值不变则不动，避免布局抖动。
    /// </summary>
    private void SyncStripsMinHeight() {
        if (shutdown) {
            return;
        }
        double viewport = StripsScroll.Viewport.Height;
        if (viewport <= 0 || Math.Abs(TrackStripsPanel.MinHeight - viewport) < 0.5) {
            return;
        }
        TrackStripsPanel.MinHeight = viewport;
    }

    private void OnLevelTimerTick(object? sender, EventArgs e)
    {
        // 隐藏即停表（可见性变化不一定触发本控件的 LayoutUpdated）
        if (!IsEffectivelyVisible) {
            SyncTimer();
            return;
        }
        if (!_loggedFirstTick) {
            _loggedFirstTick = true;
            Serilog.Log.Information($"[Mixer] Timer started, {TrackStripsPanel.Children.Count} strips");
        }
        foreach (var child in TrackStripsPanel.Children)
        {
            if (child is MixerTrackStrip strip && strip.Track != null)
            {
                strip.UpdateLevel(TrackLevels.ReadAndReset(strip.Track.TrackNo));
                strip.RefreshEq();
            }
        }
        // 主输出电平（E5 主推子条）
        MasterStripControl.UpdateLevel(TrackLevels.ReadMasterAndReset());
    }

    private void OnTracksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildStrips();
    }

    private void OnStripSelected(object? sender, EventArgs e) {
        if (sender is MixerTrackStrip strip) {
            ViewModel.SelectTrack(strip.Track);
        }
    }

    /// <summary>选中态同步到所有通道条（选中轨的通道名 / dB / 手柄三处同亮，spec-digest §8-22）。</summary>
    private void SyncStripSelection() {
        foreach (var child in TrackStripsPanel.Children) {
            if (child is MixerTrackStrip strip) {
                strip.IsSelected = ReferenceEquals(strip.Track, ViewModel.SelectedTrack);
            }
        }
    }

    public void RebuildStrips()
    {
        // 视觉树只能在**它所属的线程**上改：Tracks 的变更通知可能来自别的线程（测试宿主、
        // 未来的后台路径），与 MixerViewModel.RefreshTracks 的编组构成双保险。
        // 判据不用 Dispatcher.CheckAccess()——它在 headless 测试宿主下不可靠（实测会误判为 true），
        // 改为锚定"控件挂载到视觉树时的线程"（未挂载时即构造线程）。
        if (Thread.CurrentThread != uiThread) {
            Dispatcher.UIThread.Post(RebuildStrips);
            return;
        }
        foreach (var child in TrackStripsPanel.Children)
            if (child is MixerTrackStrip strip) {
                strip.Selected -= OnStripSelected;
                strip.DisposeSubscriptions();
            }
        TrackStripsPanel.Children.Clear();
        if (ViewModel.Tracks.Count == 0) {
            StatusText.Text = string.Format(ThemeManager.GetString("mixer.tracks"), 0);
            return;
        }
        for (int i = 0; i < ViewModel.Tracks.Count; i++)
        {
            var strip = new MixerTrackStrip(ViewModel.Tracks[i]) { TrackIndex = i };
            strip.Selected += OnStripSelected;
            TrackStripsPanel.Children.Add(strip);
        }
        SyncStripSelection();
        StatusText.Text = string.Format(ThemeManager.GetString("mixer.tracks"), ViewModel.Tracks.Count);
    }

    public void Shutdown()
    {
        shutdown = true;
        SyncTimer();
        _selectionSubscription?.Dispose();
        _selectionSubscription = null;
        _visibilitySubscription?.Dispose();
        _visibilitySubscription = null;
        ViewModel.Tracks.CollectionChanged -= OnTracksChanged;
        foreach (var child in TrackStripsPanel.Children) {
            if (child is MixerTrackStrip strip) {
                strip.Selected -= OnStripSelected;
                strip.DisposeSubscriptions();
            }
        }
        DocManager.Inst.RemoveSubscriber(ViewModel);
    }

    private void OnAddTrackClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ViewModel.AddTrack();
    }
}

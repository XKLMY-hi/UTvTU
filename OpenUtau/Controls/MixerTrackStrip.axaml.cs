using System;
using System.Linq;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;

using static OpenUtau.Core.DocManager;

namespace OpenUtau.App.Controls {
    /// <summary>
    /// 混音台通道条（设计规格 Mixer.txt:43-96）。
    ///
    /// 生命周期（本轮修的关键缺陷）：本控件会在"嵌入视图 ↔ 分离窗口"之间被**反复重挂**
    /// （`MainWindow.SetMixerAttachment` / `MixerWindow`）。旧实现把 `Unloaded` 绑到
    /// <see cref="DisposeSubscriptions"/> 并在其中把 ViewModel 置空，重挂后没有重建路径 ⇒
    /// VU 永久冻结、声像静默失效。现在：
    /// · ViewModel 与 Track 同生命周期（不再被订阅开关清掉）；
    /// · 通知订阅/声像写回开关**随挂载启停**，重挂即恢复；
    /// · <see cref="UpdateLevel"/> 只依赖 Track，不依赖订阅状态 ⇒ 重挂后立刻恢复跳表。
    /// </summary>
    public partial class MixerTrackStrip : UserControl, ICmdSubscriber {
        private UTrack? track;
        private bool _syncing;
        private bool subscribed;
        private bool suppressed;
        private bool isSelected;
        private IDisposable? _volumeSubscription;
        private IDisposable? _panSubscription;
        private MixerTrackStripViewModel? _vm;
        private readonly MixerMeter meter = new MixerMeter();
        private double panValue;
        private double dragStartX;
        private double dragStartPan;
        private TopLevel? panTrackingRoot;
        private UTrack? volumeEditTrack;
        public TextBlock VolValueLabel => VolumeField.Readout;

        /// <summary>点击通道条时抛出（混音台据此更新 SelectedTrack → 右侧链面板）。</summary>
        public event EventHandler? Selected;

        public UTrack? Track {
            get => track;
            set {
                if (ReferenceEquals(track, value)) {
                    return;
                }
                bool wasSuppressed = suppressed;
                VolumeField.CancelEdit();
                volumeEditTrack = null;
                DisposeSubscriptions();
                _vm?.Edits.Dispose();
                suppressed = wasSuppressed;
                track = value;
                _vm = track != null ? new MixerTrackStripViewModel(track) : null;
                LoadTrackData();
                SubscribeNotifications();
            }
        }

        public int TrackIndex { get; set; } = -1;

        /// <summary>是否被选中（选中后通道名 / dB / 手柄三处同亮 primary）。</summary>
        public bool IsSelected {
            get => isSelected;
            set {
                if (isSelected == value) {
                    return;
                }
                isSelected = value;
                StripCard.Classes.Set("selected", value);
                VolumeField.Classes.Set("selected", value);
            }
        }

        /// <summary>
        /// 声像值（-100 左 .. +100 右）。拖拽 / 滚轮 / ←→ 都改这里；
        /// 设置即写回模型（`ApplyPan` + `PanChangeNotification`），除非订阅已被
        /// <see cref="DisposeSubscriptions"/> 摘掉（既有回归用例锁定的契约）。
        /// </summary>
        public double PanValue {
            get => panValue;
            set {
                if (!double.IsFinite(value)) return;
                double v = Math.Clamp(value, -100, 100);
                if (Math.Abs(v - panValue) < 0.001) {
                    return;
                }
                panValue = v;
                UpdatePanValueDisplay();
                if (subscribed && track != null && _vm != null) {
                    if (_vm.ApplyPan(v)) {
                        MessageBus.Current.SendMessage(new PanChangeNotification(track.TrackNo, v));
                    }
                    // Rejected edits must not become the next keyboard/wheel starting value.
                    panValue = track.Pan * 100;
                    UpdatePanValueDisplay();
                }
            }
        }

        public MixerTrackStrip() {
            InitializeComponent();
            FaderBox.Handle = FaderHandle;
            FaderBox.GestureStarted += (_, _) => _vm?.Edits.BeginGesture();
            FaderBox.GestureEnded += (_, _) => _vm?.Edits.EndGesture();
            FaderBox.ValueEdited += (_, _) => {
                if (subscribed) ApplyVolume(FaderBox.Value);
            };
            VolumeField.EditingStarted += (_, _) => {
                volumeEditTrack = track;
                if (track != null) VolumeField.Value = track.Volume;
            };
            VolumeField.ValueCommitted += (_, _) => {
                if (subscribed && ReferenceEquals(volumeEditTrack, track)) ApplyVolume(VolumeField.Value);
                volumeEditTrack = null;
            };
            StripCard.AddHandler(PointerPressedEvent, OnStripPressed,
                RoutingStrategies.Bubble, handledEventsToo: true);
            // 构造函数里先订阅一次：测试与未挂载用法（直接 new）也要能工作。
            SubscribeNotifications();
        }

        public MixerTrackStrip(UTrack track) : this() {
            Track = track;
        }

        // ── 生命周期 ─────────────────────────────────────────

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            suppressed = false;
            SubscribeNotifications();
            LoadTrackData(); // An undo while detached may have superseded the displayed value.
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            DisposeSubscriptions();
        }

        /// <summary>
        /// 订阅音量/声像联动通知（幂等）。按 TrackNo 过滤：MessageBus 是全进程共享的，
        /// 不过滤时别的轨道（乃至别的用例）的风吹草动都会打到本条的显示上。
        /// 被 <see cref="DisposeSubscriptions"/> 摘掉后不再自恢复，重挂（<c>OnAttachedToVisualTree</c>）才恢复。
        /// </summary>
        private void SubscribeNotifications() {
            if (subscribed || suppressed || track == null) {
                return;
            }
            subscribed = true;
            DocManager.Inst.AddSubscriber(this);
            _volumeSubscription = MessageBus.Current.Listen<VolumeChangeNotification>()
                .Where(n => n.TrackNo == track.TrackNo)
                .Subscribe(n => {
                    if (_syncing) return;
                    _syncing = true;
                    double db = Math.Clamp(n.Volume, MixerMetrics.FaderMinDb, MixerMetrics.FaderMaxDb);
                    UpdateFaderPositionFromDb(db);
                    UpdateVolValueDisplay(db);
                    _syncing = false;
                });
            _panSubscription = MessageBus.Current.Listen<PanChangeNotification>()
                .Where(n => n.TrackNo == track.TrackNo)
                .Subscribe(n => {
                    if (_syncing) return;
                    _syncing = true;
                    panValue = Math.Clamp(n.Pan, -100, 100);
                    UpdatePanValueDisplay();
                    _syncing = false;
                });
        }

        /// <summary>
        /// 摘掉订阅（重建通道条 / 从可视树摘下时调用）。**不再清空 ViewModel** ——
        /// 清空是重挂后 VU 与声像永久失效的根因（旧实现在 Unloaded 里做这件事）。
        /// </summary>
        public void DisposeSubscriptions() {
            DocManager.Inst.RemoveSubscriber(this);
            subscribed = false;
            suppressed = true;
            _volumeSubscription?.Dispose();
            _volumeSubscription = null;
            _panSubscription?.Dispose();
            _panSubscription = null;
            EndPanTracking();
            FaderBox.EndGesture();
            VolumeField.CancelEdit();
            volumeEditTrack = null;
            _vm?.Edits.EndGesture();
        }

        public void OnNext(UCommand cmd, bool isUndo) {
            if (subscribed && cmd is TrackMixChangedNotification mix && track != null &&
                mix.Tracks.Contains(track)) {
                LoadTrackData();
            }
        }

        // ── 数据装载 ─────────────────────────────────────────

        private void LoadTrackData() {
            if (track == null || _vm == null) {
                return;
            }
            _vm.Refresh();
            TrackNameLabel.Text = _vm.TrackName;
            var accent = _vm.TrackColor;
            AccentBar.Background = accent;
            MeterFill.Background = accent;
            UpdateMuteSoloButtons();
            panValue = Math.Clamp(_vm.Pan, -100, 100);
            UpdatePanValueDisplay();
            UpdateVolValueDisplay(Math.Clamp(_vm.Volume, MixerMetrics.FaderMinDb, MixerMetrics.FaderMaxDb));
            UpdateFxCurve();
            UpdateFaderPosition();
        }

        /// <summary>EQ 曲线屏跟随轨道 MixFx（内置效果编辑器是实时写模型的，故由电平定时器定期刷新）。</summary>
        public void RefreshEq() => UpdateFxCurve();

        private void UpdateFxCurve() {
            if (track == null || _vm == null) {
                return;
            }
            EqCurve.LowDb = _vm.EqLowDb;
            EqCurve.MidFreq = _vm.EqMidFreq;
            EqCurve.MidDb = _vm.EqMidDb;
            EqCurve.HighDb = _vm.EqHighDb;
            EqCurve.IsEnabled = _vm.EqActive;
        }

        private void UpdateMuteSoloButtons() {
            if (track == null) return;
            MuteBtn.Classes.Set("muteOn", track.Mute);
            SoloBtn.Classes.Set("soloOn", track.Solo);
        }

        private void UpdateVolValueDisplay(double db) {
            VolumeField.Value = db;
            VolValueLabel.Text = db <= MixerMetrics.FaderMinDb ? "-∞ dB" : $"{db:+0.0;-0.0} dB";
        }

        // ── 选中 ─────────────────────────────────────────────

        private void OnStripPressed(object? sender, PointerPressedEventArgs e) {
            if (!isSelected) {
                Selected?.Invoke(this, EventArgs.Empty);
            }
        }

        // ── 声像行（R2：行 + 拖拽/滚轮/键盘改值）───────────────

        private void UpdatePanValueDisplay() {
            PanValueLabel.Text = MixerTrackStripViewModel.FormatPan(panValue);
        }

        private void OnPanPressed(object? sender, PointerPressedEventArgs e) {
            if (!e.GetCurrentPoint(PanHitArea).Properties.IsLeftButtonPressed) {
                return;
            }
            PanHitArea.Focus();
            _vm?.Edits.BeginGesture();
            dragStartPan = panValue;
            var topLevel = TopLevel.GetTopLevel(this);
            panTrackingRoot = topLevel;
            if (topLevel != null) {
                dragStartX = e.GetPosition(topLevel).X;
                // 与 PanKnob 同款：不用 Pointer.Capture（横向滚动容器会抢捕获导致拖不动），
                // 在窗口根部 Tunnel+Bubble 追踪并吃掉事件。
                topLevel.AddHandler(PointerMovedEvent, OnPanGlobalMoved,
                    RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
                topLevel.AddHandler(PointerReleasedEvent, OnPanGlobalReleased,
                    RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            }
            e.Handled = true;
        }

        private void OnPanGlobalMoved(object? sender, PointerEventArgs e) {
            if (panTrackingRoot == null) {
                return;
            }
            double dx = e.GetPosition(panTrackingRoot).X - dragStartX;
            PanValue = dragStartPan + dx * (100.0 / MixerMetrics.PanDragRange);
            e.Handled = true;
        }

        private void OnPanGlobalReleased(object? sender, PointerReleasedEventArgs e) {
            EndPanTracking();
        }

        private void OnPanCaptureLost(object? sender, PointerCaptureLostEventArgs e) {
            EndPanTracking();
        }

        private void EndPanTracking() {
            _vm?.Edits.EndGesture();
            if (panTrackingRoot == null) {
                return;
            }
            panTrackingRoot.RemoveHandler(PointerMovedEvent, OnPanGlobalMoved);
            panTrackingRoot.RemoveHandler(PointerReleasedEvent, OnPanGlobalReleased);
            panTrackingRoot = null;
        }

        private void OnPanWheel(object? sender, PointerWheelEventArgs e) {
            double step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
            PanValue = panValue + Math.Sign(e.Delta.Y) * step;
            e.Handled = true;
        }

        private void OnPanKeyDown(object? sender, KeyEventArgs e) {
            double step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
            switch (e.Key) {
                case Key.Left:
                    PanValue = panValue - step;
                    break;
                case Key.Right:
                    PanValue = panValue + step;
                    break;
                default:
                    return;
            }
            e.Handled = true;
        }

        private void OnPanDoubleTapped(object? sender, TappedEventArgs e) {
            PanValue = 0;
            e.Handled = true;
        }

        // ── 推子 ─────────────────────────────────────────────

        private void UpdateFaderPositionFromDb(double db) {
            if (track != null) FaderBox.Value = db;
        }

        private void UpdateFaderPosition() {
            if (track == null) return;
            double db = Math.Clamp(track.Volume, MixerMetrics.FaderMinDb, MixerMetrics.FaderMaxDb);
            FaderBox.Value = db;
        }

        private void ApplyVolume(double db) {
            if (track == null || _vm == null || !double.IsFinite(db)) return;
            db = Math.Clamp(db, MixerMetrics.FaderMinDb, MixerMetrics.FaderMaxDb);
            if (_vm.ApplyVolume(db)) {
                MessageBus.Current.SendMessage(new VolumeChangeNotification(track.TrackNo, track.Muted ? MixerMetrics.FaderMinDb : db));
            }
            UpdateFaderPosition();
            UpdateVolValueDisplay(track.Volume);
        }

        // ── 按钮 ─────────────────────────────────────────────

        private void OnMuteClick(object? sender, RoutedEventArgs e) {
            _vm?.ToggleMuteCmd.Execute(null);
            UpdateMuteSoloButtons();
        }

        private void OnSoloClick(object? sender, RoutedEventArgs e) {
            _vm?.ToggleSoloCmd.Execute(null);
            UpdateMuteSoloButtons();
        }

        // ── 电平表（连续条）──────────────────────────────────

        /// <summary>
        /// 喂入本轨最终输出的峰值 dB（`TrackLevels.ReadAndReset` = LevelTracker，**fader 与
        /// FX 之后**，见 RenderEngine.BuildTrackOutputs）。旧实现额外再加了一次 track.Volume，
        /// 与"跟踪点在推子之后"的链路语义重复计入，已去掉；静音/最小音量直接压到底。
        /// 不依赖订阅状态 ⇒ 重挂（视图 ↔ 分离窗）后跳表立刻恢复。
        /// </summary>
        public void UpdateLevel(float rawPeakDb) {
            if (track == null) {
                return;
            }
            bool silent = track.Mute || track.Volume <= MixerMetrics.FaderMinDb;
            meter.Push(silent ? MixerMeter.MinDb : rawPeakDb);
            meter.Apply(MeterFill, MixerMetrics.FaderHeight);
        }

        // ── 规格刻度（86-94：9 条，宽 8/5 交替，left 52）─────
        // 刻度是 XAML 里的静态 FaderTick1..9（走 DynamicResource 才跟得上主题切换）；
        // 数值由 MixerGeometryTests 与 MixerMetrics.TickWidths / TickTops 逐条比对。

        public void Refresh() => LoadTrackData();
    }
}

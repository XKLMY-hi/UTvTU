using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using OpenUtau.App.ViewModels;
using OpenUtau.App.Views;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Vst;
using Serilog;

namespace OpenUtau.App.Controls {
    /// <summary>
    /// 效果链面板（B2/B3/B4/B5/B7）——混音台右侧的链管理面板，**可独立实例化并 headless 测试**：
    /// 不依赖真实窗口、不依赖音频设备（编辑器派发走可注入的 <see cref="IFxChainEditorLauncher"/>）。
    ///
    /// 数据流（冻结契约 §1.1）：
    /// · 宿主把"当前选中轨道"送进来 —— 直接赋 <see cref="Track"/>，或
    ///   <see cref="BindSelection"/> 接一条 <c>IObservable&lt;UTrack?&gt;</c>
    ///   （集成时 = <c>mixerViewModel.WhenAnyValue(x =&gt; x.SelectedTrack)</c>）；
    /// · 面板**不写** <c>MixerViewModel</c>；一切模型变更由 VM 走 <c>DocManager.ExecuteCmd</c>。
    /// </summary>
    public partial class FxChainPanel : UserControl {
        /// <summary>当前选中轨道（面板显示它的链）。</summary>
        public static readonly StyledProperty<UTrack?> TrackProperty =
            AvaloniaProperty.Register<FxChainPanel, UTrack?>(nameof(Track));

        IDisposable? selectionSub;

        public FxChainPanel() {
            InitializeComponent();
            ViewModel = new FxChainViewModel { Launcher = new FxChainEditorLauncher(this) };
            DataContext = ViewModel;
            // 素材库「效果器」页签（W4）按 FxChainDragData.Format 拖入
            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DragOverEvent, OnDragOver);
            AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
            AddHandler(DragDrop.DropEvent, OnDrop);
        }

        /// <summary>面板 ViewModel（测试与宿主都从这里读链行）。</summary>
        public FxChainViewModel ViewModel { get; }

        /// <summary>当前选中轨道。</summary>
        public UTrack? Track {
            get => GetValue(TrackProperty);
            set => SetValue(TrackProperty, value);
        }

        /// <summary>
        /// 冻结契约接线口：把宿主（W1 <c>MixerViewModel</c>）的选中轨道流送进来。
        /// 传 null 也可（面板显示"未选择轨道"）。
        /// </summary>
        public void BindSelection(IObservable<UTrack?> selection) {
            selectionSub?.Dispose();
            selectionSub = selection.Subscribe(t => Track = t);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == TrackProperty) {
                ViewModel.Attach(Track);
            }
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            // 视图切换时面板会被 reparent：订阅随挂载启停（防重复订阅 + 防孤儿监听）
            ViewModel.Subscribe();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            ViewModel.Unsubscribe();
            base.OnDetachedFromVisualTree(e);
        }

        // ══════════════════ 「＋」：加内置模块 / 浏览插件 ══════════════════

        void OnAddClick(object? sender, RoutedEventArgs e) {
            var flyout = new MenuFlyout();
            foreach (var descriptor in FxChainCatalog.BuiltIns) {
                var module = descriptor.Module;
                var item = new MenuItem { Header = ThemeManager.GetString(descriptor.NameKey) };
                item.Click += (_, _) => ViewModel.AddBuiltIn(module);
                flyout.Items.Add(item);
            }
            var browse = new MenuItem { Header = ThemeManager.GetString("fxchain.addvst") };
            browse.Click += (_, _) => ViewModel.BrowseVst();
            flyout.Items.Add(browse);
            flyout.ShowAt(AddButton);
        }

        // ══════════════════ 素材库拖入 ══════════════════

        void OnDragOver(object? sender, DragEventArgs e) {
            bool accept = ViewModel.Track != null && FxChainDragData.Read(e) != null;
            e.DragEffects = accept ? DragDropEffects.Copy : DragDropEffects.None;
            ((IPseudoClasses)ChainRoot.Classes).Set("dragover", accept);
            e.Handled = true;
        }

        void OnDragLeave(object? sender, RoutedEventArgs e) =>
            ((IPseudoClasses)ChainRoot.Classes).Set("dragover", false);

        void OnDrop(object? sender, DragEventArgs e) {
            string? payload = FxChainDragData.Read(e);
            if (payload != null && ViewModel.DropPayload(payload)) {
                e.DragEffects = DragDropEffects.Copy;
            }
            ((IPseudoClasses)ChainRoot.Classes).Set("dragover", false);
            e.Handled = true;
        }
    }

    /// <summary>
    /// 默认派发端（B5）：
    /// · 内置伪插件 → <see cref="MixFxDialog.Open"/>（既有非模态三面板弹层；每轨单窗）；
    /// · VST → **原生 GUI 窗口**（<c>VstEffect.OpenNativeEditorAsync</c>，B5「不做自绘参数界面」）；
    ///   打不开时退回既有 <see cref="VstEditorWindow"/>（信息面板 + 手动"打开 GUI"重试）作为诊断兜底；
    /// · 浏览插件 → 既有选择器（从 TrackEffectRack 迁移，能力不丢）。
    /// </summary>
    public sealed class FxChainEditorLauncher : IFxChainEditorLauncher {
        readonly FxChainPanel panel;

        public FxChainEditorLauncher(FxChainPanel panel) => this.panel = panel;

        Window? OwnerWindow =>
            TopLevel.GetTopLevel(panel) as Window
            ?? (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        /// <summary>B5：内置三件套共用同一个内置编辑器弹层（MixFxDialog 自己保证每轨单窗 + 非模态）。</summary>
        public void OpenBuiltInEditor(UTrack track, MixFxModule module) {
            MixFxDialog.Open(OwnerWindow, track);
        }

        /// <summary>B5：VST → 插件原生窗口。</summary>
        public void OpenVstEditor(UTrack track, int slotIndex) => _ = OpenVstEditorAsync(track, slotIndex);

        async System.Threading.Tasks.Task OpenVstEditorAsync(UTrack track, int slotIndex) {
            var slot = SlotAt(track, slotIndex);
            if (slot == null || !slot.IsLoaded || slot.Entry == null) {
                return;
            }
            // 共享实例：没有就现加载（原生调用走异步路径，不卡 UI 线程）
            var fx = VstPluginManager.Inst.GetEffect(track.TrackNo, slotIndex)
                     ?? await VstPluginManager.Inst.LoadEffectAsync(track.TrackNo, slot);
            if (fx == null) {
                ShowInfo($"{ThemeManager.GetString("effects.error.load")}\n" +
                         $"{VstBridge.LastError() ?? ThemeManager.GetString("effects.error.unknown")}");
                return;
            }
            bool opened = false;
            try {
                opened = await fx.OpenNativeEditorAsync();
            } catch (Exception ex) {
                Log.Error(ex, $"[FxChain] native editor failed for {fx.DisplayName}");
            }
            if (!opened) {
                // 原生 GUI 打不开：退回诊断窗（不丢"打开 GUI / 看插件信息"的能力）
                new VstEditorWindow(fx).Show();
            }
        }

        /// <summary>浏览并加载插件（从 TrackEffectRack.BrowsePlugin 迁移；选择结果走命令写槽）。</summary>
        public void BrowsePlugin(UTrack track, int slotIndex) {
            VstPluginRegistry.Inst.ScanAll();
            var effects = VstPluginRegistry.Inst.Effects;
            var instruments = VstPluginRegistry.Inst.All.Where(p => !p.IsEffect).ToList();

            if (effects.Count == 0) {
                string msg = ThemeManager.GetString("effects.noplugins");
                if (instruments.Count > 0) {
                    msg += $"\n\n{instruments.Count} {ThemeManager.GetString("effects.instruments.excluded")}";
                }
                msg += $"\n\n{ThemeManager.GetString("effects.addscanpaths")}";
                ShowInfo(msg);
                return;
            }

            var picker = new WindowEx {
                Title = ThemeManager.GetString("effects.selecteffect"),
                Width = 520, Height = 420,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            var layout = new StackPanel { Margin = new Thickness(12), Spacing = 6 };

            layout.Children.Add(new TextBlock {
                Text = $"{effects.Count} {ThemeManager.GetString("effects.available")}" +
                       (instruments.Count > 0
                           ? $" ({instruments.Count} {ThemeManager.GetString("effects.instruments.filtered")})"
                           : ""),
                FontSize = 11, Opacity = 0.55,
            });

            var search = new TextBox {
                PlaceholderText = ThemeManager.GetString("effects.filter"), FontSize = 11,
            };
            layout.Children.Add(search);

            var list = new ListBox { ItemsSource = effects.ToList(), Height = 260 };
            layout.Children.Add(list);

            search.TextChanged += (_, _) => {
                string filter = search.Text?.ToLowerInvariant() ?? "";
                list.ItemsSource = string.IsNullOrEmpty(filter)
                    ? effects
                    : effects.Where(p => p.Name.ToLowerInvariant().Contains(filter)
                        || p.Vendor.ToLowerInvariant().Contains(filter)).ToList();
            };

            void Load(VstPluginEntry entry) {
                // 选择对话框留在命令外；写 UID + 异步加载进命令（可撤销）
                panel.ViewModel.LoadPlugin(slotIndex, entry.Uid);
                picker.Close();
            }

            list.DoubleTapped += (_, _) => {
                if (list.SelectedItem is VstPluginEntry entry) {
                    Load(entry);
                }
            };

            var buttons = new StackPanel {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 8,
            };
            var load = new Button { Content = ThemeManager.GetString("effects.load"), Width = 64 };
            load.Click += (_, _) => {
                if (list.SelectedItem is VstPluginEntry entry) {
                    Load(entry);
                } else {
                    picker.Close();
                }
            };
            var cancel = new Button { Content = ThemeManager.GetString("effects.cancel"), Width = 64 };
            cancel.Click += (_, _) => picker.Close();
            buttons.Children.Add(load);
            buttons.Children.Add(cancel);
            layout.Children.Add(buttons);

            picker.Content = layout;
            var owner = OwnerWindow;
            if (owner != null) {
                picker.ShowDialog(owner);
            } else {
                picker.Show();
            }
        }

        static VstPluginSlot? SlotAt(UTrack track, int slotIndex) =>
            track.VstSlots != null && slotIndex >= 0 && slotIndex < track.VstSlots.Count
                ? track.VstSlots[slotIndex]
                : null;

        void ShowInfo(string text) {
            var window = new WindowEx {
                Title = ThemeManager.GetString("effects.info"),
                Width = 400, Height = 190,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            var stack = new StackPanel { Margin = new Thickness(14), Spacing = 8 };
            stack.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12 });
            var ok = new Button {
                Content = ThemeManager.GetString("effects.ok"), Width = 60,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            ok.Click += (_, _) => window.Close();
            stack.Children.Add(ok);
            window.Content = stack;
            var owner = OwnerWindow;
            if (owner != null) {
                window.ShowDialog(owner);
            } else {
                window.Show();
            }
        }
    }
}

using System;
using System.ComponentModel;
using System.Reactive;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using OpenUtau.App.ViewModels;

namespace OpenUtau.App.Controls {
    /// <summary>
    /// 效果链的一行（B2/B4）。自研控件，行为全部落在代码后置：
    ///   · 单击 = 聚焦；**双击 / Enter = 打开该行编辑器**（派发交给 <see cref="FxChainRowViewModel"/>）；
    ///   · 行内开关 = 电源 / 旁通（走 VM 命令，可撤销）；
    ///   · 把手：VST 行可拖拽重排（VST 段内），内置行渲染为**禁用态**
    ///     （内置是固定 DSP 顺序 EQ → 压缩 → 混响，不做"能拖但拖了没用"的假交互）；
    ///   · Delete = 移除（仅 VST 槽）；Alt+↑/↓ = 段内上下移。
    /// 颜色一律 md3.* 色池键；行内开关按 VST-Plugin 规格 `34×20 圆角 999 + 14×14 指示点`。
    /// </summary>
    public partial class FxChainRow : UserControl {
        FxChainRowViewModel? vm;

        // 把手拖拽：按下 → 记录起点；松开 → 按"行高倍数"折算步数
        bool dragging;
        double dragStartY;
        double dragDeltaY;

        public FxChainRow() {
            InitializeComponent();
            Focusable = true;
            IsTabStop = true;
            // 序号用等宽字体（与混音台读数一致；MonoFontFamily 是 C# 常量，非资源键）
            OrderText.FontFamily = ThemeManager.MonoFontFamily;
        }

        protected override void OnDataContextChanged(EventArgs e) {
            if (vm != null) {
                vm.PropertyChanged -= OnViewModelChanged;
            }
            base.OnDataContextChanged(e);
            vm = DataContext as FxChainRowViewModel;
            if (vm != null) {
                vm.PropertyChanged += OnViewModelChanged;
            }
            ApplyAll();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            if (vm != null) {
                vm.PropertyChanged -= OnViewModelChanged;
            }
            base.OnDetachedFromVisualTree(e);
        }

        void OnViewModelChanged(object? sender, PropertyChangedEventArgs e) => ApplyAll();

        /// <summary>
        /// 把 VM 状态刷到部件上。名称/徽标走 <see cref="Md3RackKit"/> 的资源绑定
        /// （内置行是字符串键 ⇒ 切语言即时生效；VST 行是插件数据 ⇒ 直接赋字）。
        /// </summary>
        void ApplyAll() {
            if (vm == null) {
                return;
            }
            // 名称
            if (!string.IsNullOrEmpty(vm.NameKey)) {
                Md3RackKit.PaintText(NameText, vm.NameKey);
            } else {
                NameText.Text = vm.Name;
            }
            NameText[!ToolTip.TipProperty] = new DynamicResourceExtension("fxchain.open.tip");
            // 格式徽标（B4）：内置 → 字符串键；VST → 技术代码（VST3/VST2/VST3i）
            if (vm.BadgeIsBuiltIn) {
                Md3RackKit.PaintText(BadgeLabel, FxChainCatalog.BuiltInBadgeKey);
            } else {
                BadgeLabel.Text = vm.BadgeText;
            }
            Md3RackKit.Paint(BadgeChip, Border.BackgroundProperty,
                vm.BadgeIsBuiltIn ? "md3.surface-container-highest" : "md3.primary-container");
            Md3RackKit.Paint(BadgeLabel, TextBlock.ForegroundProperty,
                vm.BadgeIsBuiltIn ? "md3.on-surface-variant" : "md3.on-primary-container");
            // 副标题（内置 = 预设 id；VST = 厂商）
            DetailText.Text = vm.Detail;
            DetailText.IsVisible = !string.IsNullOrWhiteSpace(vm.Detail);
            // 不过声（旁通 / 模块关 / 总电源关）：名称降为 on-surface-variant + 状态角标（§9.2）
            Md3RackKit.Paint(NameText, TextBlock.ForegroundProperty,
                vm.IsMuted ? "md3.on-surface-variant" : "md3.on-surface");
            StateChip.IsVisible = vm.IsMuted;
            // 把手：内置段禁用（顺序固定）
            GripBox.Opacity = vm.CanReorder ? 1.0 : 0.35;
            GripBox.Cursor = vm.CanReorder ? new Cursor(StandardCursorType.SizeAll) : Cursor.Default;
            GripBox[!ToolTip.TipProperty] = new DynamicResourceExtension(
                vm.CanReorder ? "fxchain.grip.tip" : "fxchain.grip.fixed.tip");
            // 移除：仅 VST 槽
            RemoveButton.IsVisible = vm.CanRemove;
            // 开关与模型对齐（单向绑定之外再兜一次：命令被拒时立刻回弹，不留假状态）
            if (PowerToggle.IsChecked != vm.IsPowered) {
                PowerToggle.IsChecked = vm.IsPowered;
            }
        }

        // ══════════════════ 交互 ══════════════════

        void OnRowPointerPressed(object? sender, PointerPressedEventArgs e) {
            Focus();
            if (vm == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) {
                return;
            }
            // 双击 = 打开编辑器（B4）。用 ClickCount 而不是 DoubleTapped：
            // 与 Knob / PanKnob 既有做法一致，且能在 headless 下用真实路由事件驱动。
            if (e.ClickCount == 2) {
                vm.Activate();
                e.Handled = true;
            }
        }

        void OnPowerClick(object? sender, RoutedEventArgs e) {
            // 命令会重建链行 ⇒ 本控件的 DataContext 可能在命令执行中被清空（容器回收），
            // 因此处理器一律先把 VM 抓进局部变量，不再二次读字段。
            var row = vm;
            if (row == null) {
                return;
            }
            row.RequestPower(PowerToggle.IsChecked == true);
            if (PowerToggle.IsChecked != row.IsPowered) {
                PowerToggle.IsChecked = row.IsPowered;   // 命令未生效 → 回弹
            }
        }

        void OnRemoveClick(object? sender, RoutedEventArgs e) => vm?.RemoveCommand.Execute(null);

        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            var row = vm;
            if (row == null) {
                return;
            }
            bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            switch (e.Key) {
                case Key.Enter:
                    row.Activate();
                    e.Handled = true;
                    break;
                case Key.Delete when row.CanRemove:
                    row.RemoveCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.Up when alt && row.CanReorder:
                    row.MoveUpCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.Down when alt && row.CanReorder:
                    row.MoveDownCommand.Execute(null);
                    e.Handled = true;
                    break;
            }
        }

        // ── 把手拖拽（仅 VST 行） ──────────────────────────────

        void OnGripPressed(object? sender, PointerPressedEventArgs e) {
            e.Handled = true;   // 不要把按下冒泡成行的双击
            if (vm?.CanReorder != true || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) {
                return;
            }
            dragging = true;
            dragStartY = e.GetPosition(this).Y;
            dragDeltaY = 0;
            ((IPseudoClasses)RowRoot.Classes).Set("dragging", true);
            e.Pointer.Capture(GripBox);
        }

        void OnGripMoved(object? sender, PointerEventArgs e) {
            if (!dragging) {
                return;
            }
            dragDeltaY = e.GetPosition(this).Y - dragStartY;
            e.Handled = true;
        }

        void OnGripReleased(object? sender, PointerReleasedEventArgs e) {
            if (!dragging) {
                return;
            }
            e.Handled = true;
            EndDrag();
        }

        void OnGripCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndDrag();

        void EndDrag() {
            dragging = false;
            ((IPseudoClasses)RowRoot.Classes).Set("dragging", false);
            double rowStep = Math.Max(1.0, RowRoot.Bounds.Height + 6);   // 行高 + ItemsControl 间距
            int steps = (int)Math.Round(dragDeltaY / rowStep);
            dragDeltaY = 0;
            var row = vm;
            if (steps != 0 && row != null) {
                row.RequestMove(steps);
            }
        }
    }
}

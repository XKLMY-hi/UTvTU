using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace OpenUtau.App.Controls {
    /// <summary>
    /// 折叠面板的**快捷展开**边缘竖标签（W34 / 设计稿 `.opencode/plans/trackheader-design.md` §8 候选 1）。
    ///
    /// 形态：默认 **14×48 圆角 8**（只显示面板字形）→ 悬停/聚焦 **92×48 圆角 999**（字形 + 本地化面板名）。
    /// 归属四线索：① 位置（就在被折叠那条分隔条列里，顶部对齐面板头）② 箭头/字形朝向与面板头 chevron 镜像
    /// ③ 字形区分面板（轨头 `icon-list`、素材库 `icon-folder-open`，复用 `Assets/Icons.axaml`）
    /// ④ 悬停显名（复用现成的 `panel.toggle.*`，零新增翻译）。
    ///
    /// **零布局占用**：宿主把它放进一个**测量为 0 的 `Canvas`**（与 `PanelSplitter` 同列），
    /// 因此 92px 的悬停展开只是视觉溢出，不改任何列宽、不留夹缝 —— `PanelSplitter` 的五值契约一行未动。
    ///
    /// **镜像**：`AnchorRight=false`（左列面板：标签贴该列左缘、**向右**展开）；
    /// `AnchorRight=true`（右列面板：标签贴该列右缘、**向左**展开）。展开靠改自身 `Width`
    /// 同时在 code-behind 里补偿 `Canvas.Left`，所以两个方向都不会把窗口撑出界。
    /// </summary>
    public partial class PanelRevealTab : UserControl {
        /// <summary>行方向面板（卷帘表达式区）：横向 48×14 胶囊、贴**下缘**、悬停向上展开。</summary>
        public static readonly StyledProperty<bool> AnchorBottomProperty =
            AvaloniaProperty.Register<PanelRevealTab, bool>(nameof(AnchorBottom));

        public static readonly StyledProperty<bool> AnchorRightProperty =
            AvaloniaProperty.Register<PanelRevealTab, bool>(nameof(AnchorRight));

        /// <summary>展开请求（宿主接上：把对应 `PanelSlot.IsCollapsed` 置 false ⇒ 回到**持久化**宽度）。</summary>
        public event EventHandler? ExpandRequested;

        /// <summary>悬停/聚焦时显示的面板名（用现成 `panel.toggle.tracks|library`）。</summary>
        public static readonly StyledProperty<string> PanelLabelProperty =
            AvaloniaProperty.Register<PanelRevealTab, string>(nameof(PanelLabel), string.Empty);

        /// <summary>tooltip（新增 `panel.expand.*` + `panel.expand.hint`）。</summary>
        public static readonly StyledProperty<string> HintProperty =
            AvaloniaProperty.Register<PanelRevealTab, string>(nameof(Hint), string.Empty);

        /// <summary>字形（如 `{StaticResource icon-list}`）——与面板头 chevron **镜像**的朝向由字形本身表达。</summary>
        /// <summary>tooltip 第二行（`panel.expand.hint`：点击展开 · 拖动分隔条调整宽度）。</summary>
        public static readonly StyledProperty<string> HintSuffixProperty =
            AvaloniaProperty.Register<PanelRevealTab, string>(nameof(HintSuffix), string.Empty);

        public static readonly StyledProperty<Geometry?> GlyphDataProperty =
            AvaloniaProperty.Register<PanelRevealTab, Geometry?>(nameof(GlyphData));

        public bool AnchorBottom {
            get => GetValue(AnchorBottomProperty);
            set => SetValue(AnchorBottomProperty, value);
        }
        public bool AnchorRight {
            get => GetValue(AnchorRightProperty);
            set => SetValue(AnchorRightProperty, value);
        }
        public string PanelLabel {
            get => GetValue(PanelLabelProperty);
            set => SetValue(PanelLabelProperty, value);
        }
        public string Hint {
            get => GetValue(HintProperty);
            set => SetValue(HintProperty, value);
        }
        public string HintSuffix {
            get => GetValue(HintSuffixProperty);
            set => SetValue(HintSuffixProperty, value);
        }
        public Geometry? GlyphData {
            get => GetValue(GlyphDataProperty);
            set => SetValue(GlyphDataProperty, value);
        }

        private const double CollapsedWidth = 14;
        private const double CollapsedHeight = 14;
        private const double ExpandedWidth = 92;
        private const double SplitterWidth = 7;

        public PanelRevealTab() {
            InitializeComponent();
            Width = CollapsedWidth;
            // `AnchorBottom` 在 XAML/对象初始化器里是**构造之后**才赋值的 ⇒ 必须响应变化，
            // 否则横向变体会停在竖条的 14×48（本用例第一版就是这么假红的）。
            this.GetObservable(AnchorBottomProperty).Subscribe(_ => ApplyOrientation());
            ApplyOrientation();
            this.GetObservable(HintProperty).Subscribe(_ => UpdateTooltip());
            this.GetObservable(HintSuffixProperty).Subscribe(_ => UpdateTooltip());
            TabButton.PropertyChanged += (_, e) => {
                if (e.Property == BoundsProperty) {
                    SyncCanvasOffset();
                }
            };
            // 悬停/聚焦会让样式把 Width 改到 92；这里在宽度变化后补偿 Canvas.Left，
            // 使"贴右缘、向左展开"的那一侧始终以同一条边为锚，不会把标签推出窗口。
            this.GetObservable(BoundsProperty).Subscribe(_ => SyncCanvasOffset());
            TabButton.PointerEntered += (_, _) => SyncCanvasOffset();
            TabButton.PointerExited += (_, _) => SyncCanvasOffset();
            TabButton.GotFocus += (_, _) => SyncCanvasOffset();
            TabButton.LostFocus += (_, _) => SyncCanvasOffset();
        }

        /// <summary>
        /// 方向档：竖条（左/右列面板）14×48 r8；横向（行方向面板）48×14 r8。
        /// 横向档给按钮挂 `horizontal` 类，样式表里那一档的悬停/聚焦改的是**高度** ⇒ 向上生长。
        /// </summary>
        private void ApplyOrientation() {
            bool horizontal = AnchorBottom;
            Width = horizontal ? 48 : CollapsedWidth;
            Height = horizontal ? CollapsedHeight : 48;
            TabButton.Classes.Set("horizontal", horizontal);
            SyncCanvasOffset();
        }

        /// <summary>把标签自身的宽度同步进 code-behind 自己的 Width（样式改了按钮宽，但控件宽要跟上）。</summary>
        private void SyncCanvasOffset() {
            double target = TabButton.Bounds.Width > 0 ? TabButton.Bounds.Width : CollapsedWidth;
            if (Math.Abs(Width - target) >= 0.5) {
                Width = target;
            }
            if (AnchorRight) {
                // 贴分隔条列的**右缘**：向左展开 ⇒ 左边界随宽度左移
                Canvas.SetLeft(this, SplitterWidth - target);
            } else {
                Canvas.SetLeft(this, 0);
            }
        }

        /// <summary>tooltip = 「展开&lt;面板名&gt;」+ 第二行操作提示（两个键都在 Strings 里成对存在）。</summary>
        private void UpdateTooltip() {
            string tip = string.IsNullOrWhiteSpace(HintSuffix) ? Hint : $"{Hint}\n{HintSuffix}";
            ToolTip.SetTip(TabButton, tip);
        }

        private void OnTabClicked(object? sender, RoutedEventArgs e) {
            ExpandRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }
}

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;

namespace OpenUtau.App.Views {
    /// <summary>
    /// 欢迎视图（内嵌于主窗口）· W36 重设计。
    ///
    /// - DataContext = 主窗口的 <see cref="MainWindowViewModel"/>（最近工程 / 模板 / 恢复状态 / 版本）
    /// - 工程动作（新建/打开/导入/最近/模板/恢复）与窗口级动作（偏好设置/包管理/链接）统一交给
    ///   <see cref="Host"/>（MainWindow），保持"视图不承载业务"的分工。
    /// - 结构对齐设计稿 1-Welcome（<c>.opencode/design/UTVTU-设计交付</c>），
    ///   设计说明与差异清单见 <c>.opencode/plans/welcome-design.md</c>；颜色一律取 md3 颜色池角色键。
    ///
    /// **键盘可达**：所有可点元素都是 `Button`（不是 `Border.PointerPressed`）⇒
    /// Tab 可聚焦、Enter/Space 激活、`:focus-visible` 有焦点环；Tab 序 = XAML 阅读序。
    /// </summary>
    public partial class WelcomeView : UserControl {
        /// <summary>宿主主窗口；由 MainWindow 在构造时注入。</summary>
        public MainWindow? Host { get; set; }

        public WelcomeView() {
            InitializeComponent();
            // 波形：真实包络几何（资源字典 welcome-waveform），纯装饰、无业务状态；
            // 高度按可用空间弹性取值（132…200），缩放口径见 XAML 注释（时基 → 宽 / 幅度 → 高）
            MidHost.SizeChanged += (_, _) => ApplyWaveformHeight();   // 响应式：窗口变高变矮都重算
            // 已安装音源：**真实**数据；取不到就整段隐藏（不编数）
            var singers = WelcomeArt.InstalledSingers();
            SingerChips.ItemsSource = singers;
            SingersSection.IsVisible = singers.Count > 0;
        }

        private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

        /// <summary>
        /// 波形高度 = clamp(左栏中段可用高度 − 其余内容高，132, 200)。
        ///
        /// **响应式两段式**（本轮裁决：处置二 + 处置一 组合）：
        ///   ① 波形先"吃掉"可用余量（上限 200）—— 装饰承担弹性；
        ///   ② 涨到上限后仍有余量时，不再动波形，交给 MidGroup 的 `VerticalAlignment=Center`
        ///      把余量**对称**平摊到上下（`MidHost` 的 MinHeight 撑到视口高，才有可平摊的空间）。
        /// 这样 1226×699 / 1000×660 / 1226×900 都不会出现"只有下边一个洞"的观感。
        /// 其余内容高按**实测**累加（不写死行数），窗口尺寸变化时收敛（其余行高度与波形无关）。
        /// </summary>
        void ApplyWaveformHeight() {
            if (MidHost.Bounds.Height <= 0) {
                return;   // 首轮布局尚未发生
            }
            double others = 0;
            foreach (var child in MidGroup.Children) {
                if (ReferenceEquals(child, WaveformBars)) {
                    continue;
                }
                others += child.Bounds.Height + child.Margin.Top + child.Margin.Bottom;
            }
            if (others <= 0) {
                return;   // 其余内容还没量出来，等下一轮
            }
            // 24 = MidGroup 的顶部外边距；再往上留 8px 呼吸位，其余交给"对称平摊"
            double target = Math.Clamp(
                MidHost.Bounds.Height - others - 32,
                WelcomeArt.MinWaveHeight,
                WelcomeArt.MaxWaveHeight);
            if (Math.Abs(target - WaveformBars.Height) < 0.5) {
                return;
            }
            WaveformBars.Height = target;
        }

        // ── 工程动作 ──
        void OnNewProject(object? sender, RoutedEventArgs args) => Host?.WelcomeNewProject();

        async void OnOpenProject(object? sender, RoutedEventArgs args) {
            if (Host != null) {
                await Host.WelcomeOpenProject();
            }
        }

        void OnOpenRecent(object? sender, RoutedEventArgs args) {
            if (Host != null && sender is StyledElement el && el.DataContext is RecentFileInfo file) {
                Host.WelcomeOpenRecent(file.PathName);
            }
        }

        void OnOpenTemplate(object? sender, RoutedEventArgs args) {
            if (Host != null && sender is StyledElement el && el.DataContext is RecentFileInfo file) {
                Host.WelcomeOpenTemplate(file.PathName);
            }
        }

        /// <summary>打开「模板」卡片上的模板下拉（稿里没有此形态：卡片 + 飞行弹层）。</summary>
        void OnShowTemplates(object? sender, RoutedEventArgs args) {
            if (sender is Control control) {
                FlyoutBase.ShowAttachedFlyout(control);
            }
        }

        /// <summary>导入音频/伴奏（沿用原欢迎窗的第 3 个入口）。</summary>
        void OnImportAudio(object? sender, RoutedEventArgs args) => Host?.ImportAudio();

        void OnRecovery(object? sender, RoutedEventArgs args) {
            string? path = ViewModel?.RecoveryPath;
            if (Host != null && !string.IsNullOrEmpty(path)) {
                Host.WelcomeOpenRecent(path!);
            }
        }

        // ── 窗口级动作 ──
        void OnPreferences(object? sender, RoutedEventArgs args) => Host?.ShowPreferences();
        void OnPackages(object? sender, RoutedEventArgs args) => Host?.ShowPackageManager();
        void OnWiki(object? sender, RoutedEventArgs args) => Host?.OpenUrl("https://github.com/stakira/OpenUtau/wiki/Getting-Started");
        void OnReleases(object? sender, RoutedEventArgs args) => Host?.OpenUrl("https://github.com/XKLMY-hi/UTvTU/releases");
        void OnDiffsinger(object? sender, RoutedEventArgs args) => Host?.OpenUrl("https://github.com/openvpi/DiffSinger");
        void OnTeto(object? sender, RoutedEventArgs args) => Host?.OpenUrl("https://kasaneteto.jp/");
    }
}

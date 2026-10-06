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
            // 几何波形：26 根柱（设计稿逐值），纯装饰、无业务状态 ⇒ 由美术数据直接喂
            WaveformBars.ItemsSource = WelcomeArt.Waveform;
            // 已安装音源：**真实**数据；取不到就整段隐藏（不编数）
            var singers = WelcomeArt.InstalledSingers();
            SingerChips.ItemsSource = singers;
            SingersSection.IsVisible = singers.Count > 0;
        }

        private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

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

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;

namespace OpenUtau.App.Views {
    /// <summary>
    /// 欢迎视图（内嵌于主窗口，取代原独立 WelcomeWindow）。
    ///
    /// - DataContext = 主窗口的 <see cref="MainWindowViewModel"/>（最近工程 / 模板 / 恢复状态 / 版本）
    /// - 音源徽标列表由主窗口把 <c>SidebarViewModel</c> 挂到 <see cref="SingersHost"/> 的 DataContext
    /// - 工程动作（新建/打开/最近/模板/恢复）与窗口级动作（偏好设置/包管理/链接）统一交给
    ///   <see cref="Host"/>（MainWindow），保持"视图不承载业务"的分工。
    /// </summary>
    public partial class WelcomeView : UserControl {
        /// <summary>宿主主窗口；由 MainWindow 在构造时注入。</summary>
        public MainWindow? Host { get; set; }

        /// <summary>音源徽标列表的数据上下文（主窗口注入 <c>SidebarViewModel</c>）。</summary>
        public object? SingersDataContext {
            set => SingersHost.DataContext = value;
        }

        public WelcomeView() {
            InitializeComponent();
        }

        private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

        // ── 工程动作 ──
        void OnNewProject(object? sender, PointerPressedEventArgs args) => Host?.WelcomeNewProject();

        async void OnOpenProject(object? sender, PointerPressedEventArgs args) {
            if (Host != null) {
                await Host.WelcomeOpenProject();
            }
        }

        void OnOpenRecent(object? sender, PointerPressedEventArgs args) {
            if (Host != null && sender is StyledElement el && el.DataContext is RecentFileInfo file) {
                Host.WelcomeOpenRecent(file.PathName);
            }
        }

        void OnOpenTemplate(object? sender, PointerPressedEventArgs args) {
            if (Host != null && sender is StyledElement el && el.DataContext is RecentFileInfo file) {
                Host.WelcomeOpenTemplate(file.PathName);
            }
        }

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

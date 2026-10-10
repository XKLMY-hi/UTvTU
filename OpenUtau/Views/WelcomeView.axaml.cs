using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;

namespace OpenUtau.App.Views {
    /// <summary>
    /// 欢迎视图（内嵌于主窗口）· W50 重建 —— 与主界面同一套 **MD3** 词汇。
    ///
    /// - DataContext = 主窗口的 <see cref="MainWindowViewModel"/>（最近工程 / 模板 / 恢复状态 / 版本）
    /// - 工程动作（新建/打开/导入/最近/模板/恢复）与窗口级动作（偏好设置/包管理/链接）统一交给
    ///   <see cref="Host"/>（MainWindow），保持"视图不承载业务"的分工。
    /// - 左栏导航在本视图内切换**右栏分页**（最近 / 新建 / 打开 / 模板），不涉及业务 ⇒ 归视图。
    ///   选中态使用**应用级共享词汇**的 `.selected` 类（样式在 Styles/Md3Controls.axaml 的
    ///   `Button.navItem` 一族，与偏好设置页共用 ⇒ 两页不会再各写一套）。
    ///
    /// **键盘可达**：所有可点元素都是 `Button`（不是 `Border.PointerPressed`）⇒
    /// Tab 可聚焦、Enter/Space 激活、`:focus-visible` 有焦点环；Tab 序 = XAML 阅读序（左栏 → 右栏）。
    /// </summary>
    public partial class WelcomeView : UserControl {
        /// <summary>宿主主窗口；由 MainWindow 在构造时注入。</summary>
        public MainWindow? Host { get; set; }

        public WelcomeView() {
            InitializeComponent();
            // 已安装音源：**真实**数据；取不到就整段隐藏（不编数）
            var singers = WelcomeArt.InstalledSingers();
            SingerChips.ItemsSource = singers;
            SingersSection.IsVisible = singers.Count > 0;
            ShowPage(0);   // 默认停在「最近」
        }

        private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

        // ── 左栏导航：切换右栏分页（标题/副标随页更新；选中态 = .selected 药丸） ──

        void OnNavRecent(object? sender, RoutedEventArgs args) => ShowPage(0);
        void OnNavNew(object? sender, RoutedEventArgs args) => ShowPage(1);
        void OnNavOpen(object? sender, RoutedEventArgs args) => ShowPage(2);
        void OnNavTemplates(object? sender, RoutedEventArgs args) => ShowPage(3);

        /// <summary>0 最近 / 1 新建 / 2 打开 / 3 模板。同一时刻只有一个分页可见。</summary>
        private void ShowPage(int index) {
            PageRecent.IsVisible = index == 0;
            PageNew.IsVisible = index == 1;
            PageOpen.IsVisible = index == 2;
            PageTemplates.IsVisible = index == 3;

            SetSelected(NavRecent, index == 0);
            SetSelected(NavNew, index == 1);
            SetSelected(NavOpen, index == 2);
            SetSelected(NavTemplates, index == 3);

            (PageTitle.Text, PageSubtitle.Text) = index switch {
                0 => (Str("welcome.recent"), Str("welcome.greeting.sub")),
                1 => (Str("welcome.new"), Str("welcome.new.description")),
                2 => (Str("welcome.open"), Str("welcome.open.description")),
                _ => (Str("welcome.template"), Str("welcome.template.description")),
            };
            if (index == 0) {
                SearchBox.Focus();   // 进「最近」直接可搜（工具习惯）
            }
        }

        /// <summary>选中态用**共享词汇**的 `.selected` 类（不是页面私有的 `.active`）。</summary>
        private static void SetSelected(Button button, bool selected) {
            if (selected) {
                if (!button.Classes.Contains("selected")) {
                    button.Classes.Add("selected");
                }
            } else {
                button.Classes.Remove("selected");
            }
        }

        private static string Str(string key) => ThemeManager.GetString(key);

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

        /// <summary>「新建」页的次要行动：切到「模板」页。</summary>
        void OnShowTemplates(object? sender, RoutedEventArgs args) => ShowPage(3);

        /// <summary>导入音频/伴奏。</summary>
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

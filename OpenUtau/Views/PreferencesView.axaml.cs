using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.App.Views {
    /// <summary>
    /// 全屏偏好设置（设计稿 6-Preferences）：左导航切页，右侧卡片式内容。
    ///
    /// - DataContext = <see cref="PreferencesViewModel"/>（设置项）
    /// - 检测到的音源列表由宿主把 <see cref="SidebarViewModel"/> 挂到 <c>SingerListHost</c>
    /// - 需要窗口级动作（文件夹选择 / 旧版设置）时交给 <see cref="Host"/>（MainWindow）
    /// </summary>
    public partial class PreferencesView : UserControl {
        /// <summary>宿主主窗口；由 MainWindow 注入。</summary>
        public MainWindow? Host { get; set; }

        private static readonly Dictionary<string, string> PageNames = new() {
            ["audio"] = "audio",
            ["library"] = "library",
            ["appearance"] = "appearance",
        };

        public PreferencesView() {
            InitializeComponent();
        }

        /// <summary>音源列表数据源（默认隐藏，仅在库页显示）。</summary>
        public object? SingersDataContext {
            get => SingerListHost.DataContext;
            set => SingerListHost.DataContext = value;
        }

        private PreferencesViewModel? ViewModel => DataContext as PreferencesViewModel;

        /// <summary>打开时定位到音频页（稿子的默认页）。</summary>
        public void ShowDefaultPage() => ShowPage("audio", NavAudio);

        private void OnNavClicked(object? sender, RoutedEventArgs e) {
            if (sender is Button button) {
                ShowPage(button.Tag as string ?? "audio", button);
            }
        }

        private void ShowPage(string tag, Button selected) {
            PageAudio.IsVisible = tag == "audio";
            PageLibrary.IsVisible = tag == "library";
            PageAppearance.IsVisible = tag == "appearance";
            PagePending.IsVisible = !PageNames.ContainsKey(tag);
            foreach (Button nav in new[] { NavAudio, NavLibrary, NavPlayback, NavAppearance, NavEditor, NavMidi, NavGeneral }) {
                nav.Classes.Set("selected", nav == selected);
            }
        }

        private void OnResetAll(object? sender, RoutedEventArgs e) {
            // 与旧版设置一致：确认后重置偏好并重开本视图
            Host?.ResetAllPreferences();
        }

        private void OnOpenLegacy(object? sender, RoutedEventArgs e) => Host?.ShowLegacyPreferences();

        private async void OnChangeSingerFolder(object? sender, RoutedEventArgs e) {
            if (Host == null || ViewModel == null) {
                return;
            }
            string? path = await Host.PickFolder(ThemeManager.GetString("prefs.paths.singer"));
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) {
                ViewModel.SetAddlSingersPath(path!);
            }
        }

        private async void OnChangeSamplesFolder(object? sender, RoutedEventArgs e) {
            if (Host == null || ViewModel == null) {
                return;
            }
            string? path = await Host.PickFolder(ThemeManager.GetString("prefs.paths.samples"));
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) {
                ViewModel.SetSamplesPath(path!);
            }
        }
    }
}

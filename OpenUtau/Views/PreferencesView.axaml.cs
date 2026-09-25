using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Colors;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.App.Views {
    /// <summary>
    /// 全屏偏好设置（设计稿 6-Preferences）：左导航切页，右侧卡片式内容。
    ///
    /// - DataContext = <see cref="PreferencesViewModel"/>（设置项）
    /// - 检测到的音源列表由宿主把 <see cref="SidebarViewModel"/> 挂到 <c>SingerListHost</c>
    /// - 需要窗口级动作（文件夹 / 文件选择）时交给 <see cref="Host"/>（MainWindow）
    /// - 设置项与旧 PreferencesDialog **一一对应**（直接映射，绑定沿用同一个 VM）
    /// </summary>
    public partial class PreferencesView : UserControl {
        /// <summary>宿主主窗口；由 MainWindow 注入。</summary>
        public MainWindow? Host { get; set; }

        public PreferencesView() {
            InitializeComponent();
        }

        /// <summary>音源列表数据源。</summary>
        public object? SingersDataContext {
            get => SingerListHost.DataContext;
            set => SingerListHost.DataContext = value;
        }

        private PreferencesViewModel? ViewModel => DataContext as PreferencesViewModel;
        private Window? HostWindow => Host as Window;

        /// <summary>打开时定位到音频页（稿子的默认页）。</summary>
        public void ShowDefaultPage() => ShowPage("audio", NavAudio);

        private void OnNavClicked(object? sender, RoutedEventArgs e) {
            if (sender is Button button) {
                ShowPage(button.Tag as string ?? "audio", button);
            }
        }

        private void ShowPage(string tag, Button selected) {
            PageAudio.IsVisible = tag == "audio";
            PagePlayback.IsVisible = tag == "playback";
            PageLibrary.IsVisible = tag == "library";
            PageAppearance.IsVisible = tag == "appearance";
            PageEditor.IsVisible = tag == "editor";
            PageGeneral.IsVisible = tag == "general";
            PageMidi.IsVisible = tag == "midi";
            PageAbout.IsVisible = tag == "about";
            foreach (Button nav in new[] { NavAudio, NavLibrary, NavPlayback, NavAppearance, NavEditor, NavMidi, NavGeneral, NavAbout }) {
                nav.Classes.Set("selected", nav == selected);
            }
        }

        // ── 底部操作条 ───────────────────────────────────────────
        private void OnResetAll(object? sender, RoutedEventArgs e) => Host?.ResetAllPreferences();

        private void OnApplyClicked(object? sender, RoutedEventArgs e) => ApplyPreferences();

        private void OnDoneClicked(object? sender, RoutedEventArgs e) {
            ApplyPreferences();
            Host?.HidePreferences();
        }

        private static void ApplyPreferences() {
            Preferences.Save();
            App.SetTheme();
        }

        // ── 路径（音源 / 伴奏库） ─────────────────────────────────
        private void OnOpenSingerFolder(object? sender, RoutedEventArgs e) {
            try {
                Directory.CreateDirectory(ViewModel!.SingerPath);
                OS.OpenFolder(ViewModel!.SingerPath);
            } catch (Exception ex) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
            }
        }

        private void OnOpenSamplesFolder(object? sender, RoutedEventArgs e) {
            try {
                if (Directory.Exists(ViewModel!.SamplesPath)) {
                    OS.OpenFolder(ViewModel!.SamplesPath);
                }
            } catch (Exception ex) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
            }
        }

        private void OnResetAddlSingersPath(object? sender, RoutedEventArgs e) => ViewModel!.SetAddlSingersPath(string.Empty);

        private void OnResetSamplesPath(object? sender, RoutedEventArgs e) => ViewModel!.SetSamplesPath(string.Empty);

        private async void OnChangeSingerFolder(object? sender, RoutedEventArgs e) {
            if (Host == null) {
                return;
            }
            string? path = await Host.PickFolder(ThemeManager.GetString("prefs.paths.addlsinger"));
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) {
                ViewModel!.SetAddlSingersPath(path!);
            }
        }

        private async void OnChangeSamplesFolder(object? sender, RoutedEventArgs e) {
            if (Host == null) {
                return;
            }
            string? path = await Host.PickFolder(ThemeManager.GetString("prefs.paths.samples"));
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) {
                ViewModel!.SetSamplesPath(path!);
            }
        }

        private async void OnReloadSingers(object? sender, RoutedEventArgs e) {
            if (Host == null) {
                return;
            }
            LoadingWindow.BeginLoading(Host);
            await Task.Run(() => SingerManager.Inst.SearchAllSingers());
            DocManager.Inst.ExecuteCmd(new SingersRefreshedNotification());
            LoadingWindow.EndLoading();
        }

        // ── 自定义主题 ───────────────────────────────────────────
        private void OnEditCustomTheme(object? sender, RoutedEventArgs e) {
            if (ViewModel == null || CustomTheme.IsPackageTheme(ViewModel.ThemeName)) {
                return;
            }
            ThemeEditorWindow.Show(CustomTheme.Themes[ViewModel.ThemeName]);
        }

        private void OnCreateCustomTheme(object? sender, RoutedEventArgs e) {
            if (HostWindow == null) {
                return;
            }
            var dialog = new TypeInDialog {
                Title = ThemeManager.GetString("prefs.appearance.customtheme.create.title")
            };
            dialog.SetPrompt(ThemeManager.GetString("prefs.appearance.customtheme.create.prompt"));
            dialog.onFinish = s => {
                if (string.IsNullOrEmpty(s)) {
                    MessageBox.ShowModal(HostWindow,
                        ThemeManager.GetString("prefs.appearance.customtheme.create.empty"),
                        ThemeManager.GetString("prefs.appearance.customtheme.create.title"));
                    return;
                }
                string filename = string.Join("", s.Where(c => Char.IsLetterOrDigit(c) || c == ' '))
                                        .Replace(" ", "-").ToLower() + ".yaml";
                var themeYaml = new CustomTheme.ThemeYaml { Name = s };
                File.WriteAllText(Path.Join(PathManager.Inst.ThemesPath, filename),
                    Yaml.DefaultSerializer.Serialize(themeYaml));
                ViewModel!.RefreshThemes();
            };
            dialog.ShowDialog(HostWindow);
        }

        // ── 外部工具路径（vLabeler / setParam / wine） ────────────
        private void OnResetVLabelerPath(object? sender, RoutedEventArgs e) => ViewModel!.SetVLabelerPath(string.Empty);

        private void OnResetSetParamPath(object? sender, RoutedEventArgs e) => ViewModel!.SetSetParamPath(string.Empty);

        private void OnResetWinePath(object? sender, RoutedEventArgs e) => ViewModel!.SetWinePath(string.Empty);

        private async void OnSelectVLabelerPath(object? sender, RoutedEventArgs e) {
            if (HostWindow == null) {
                return;
            }
            var path = await FilePicker.OpenFile(HostWindow, "prefs.advanced.vlabelerpath", FilePicker.EXE);
            if (!string.IsNullOrEmpty(path) && OS.AppExists(path)) {
                ViewModel!.SetVLabelerPath(path!);
            }
        }

        private async void OnSelectSetParamPath(object? sender, RoutedEventArgs e) {
            if (HostWindow == null) {
                return;
            }
            var path = await FilePicker.OpenFile(HostWindow, "prefs.otoeditor.setparampath", FilePicker.EXE);
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) {
                ViewModel!.SetSetParamPath(path!);
            }
        }

        private async void OnSelectWinePath(object? sender, RoutedEventArgs e) {
            if (HostWindow == null) {
                return;
            }
            var path = await FilePicker.OpenFile(HostWindow, "prefs.advanced.winepath", FilePicker.UnixExecutable);
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) {
                ViewModel!.SetWinePath(path!);
            }
        }

        private void OnDetectWinePath(object? sender, RoutedEventArgs e) {
            string[] wineNames = { "wine", "wine64", "wine32", "wine32on64" };
            string winePath = string.Empty;
            foreach (string wineName in wineNames) {
                winePath = OS.WhereIs(wineName);
                if (!string.IsNullOrEmpty(winePath)) {
                    break;
                }
            }
            if (!string.IsNullOrEmpty(winePath)) {
                ViewModel!.SetWinePath(winePath);
            }
        }

        // ── 效果器（VST 插件路径） ───────────────────────────────
        private void OnAddVstPath(object? sender, RoutedEventArgs e) {
            var path = NewVstPath.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(path)) {
                ViewModel!.AddVstScanPath(path!);
                NewVstPath.Text = string.Empty;
            }
        }

        private void OnRemoveVstPath(object? sender, RoutedEventArgs e) {
            if (VstPathsList.SelectedItem is string path) {
                ViewModel!.RemoveVstScanPath(path);
            }
        }

        private void OnRescanVstPlugins(object? sender, RoutedEventArgs e) => ViewModel!.RefreshVstPlugins();

        private void OnOpenReadme(object? sender, RoutedEventArgs e) {
            string path = Path.Combine(PathManager.Inst.RootPath, "README.md");
            if (File.Exists(path)) {
                try {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                    return;
                } catch (Exception ex) {
                    Log.Error(ex, "[Prefs] Failed to open README");
                }
            }
            OnOpenGithub(sender, e);
        }

        private void OnOpenGithub(object? sender, RoutedEventArgs e) {
            try {
                Process.Start(new ProcessStartInfo("https://github.com/XKLMY-hi/UTvTU") { UseShellExecute = true });
            } catch (Exception ex) {
                Log.Error(ex, "[Prefs] Failed to open GitHub");
            }
        }
    }
}

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;

namespace UTvTU.Installer {
    /// <summary>
    /// 安装器应用：把**写死的**调色板（InstallerPalette，来自主程序算法的一次性固化）注入为
    /// `inst.*` 资源键，供 XAML 以 {DynamicResource inst.xxx} 使用 —— 不做色池、不做动态取色（用户明确要求）。
    /// 跟随系统深浅；切换主题时重刷同一批键。
    /// </summary>
    public partial class App : Application {
        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted() {
            // 主题：默认跟随系统；`UTVTU_INSTALLER_THEME=Light|Dark` 可强制（供截图/验证用，不影响用户）
            PlatformThemeVariant sys = Current?.PlatformSettings?.GetColorValues().ThemeVariant
                                      ?? PlatformThemeVariant.Dark;
            string? forced = Environment.GetEnvironmentVariable("UTVTU_INSTALLER_THEME");
            RequestedThemeVariant = forced?.ToLowerInvariant() switch {
                "light" => ThemeVariant.Light,
                "dark" => ThemeVariant.Dark,
                _ => sys == PlatformThemeVariant.Light ? ThemeVariant.Light : ThemeVariant.Dark,
            };
            ApplyInstallerPalette();
            if (ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop) {
                desktop.MainWindow = InstallerCore.UninstallMode
                    ? new MainWindow(uninstallMode: true)
                    : new MainWindow(uninstallMode: false);
            }
            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>把当前主题的一套颜色写成 `inst.*` 动态资源（键名 = 角色名的 kebab-case）。</summary>
        public static void ApplyInstallerPalette() {
            if (Current == null) {
                return;
            }
            bool dark = Current.ActualThemeVariant == ThemeVariant.Dark;
            IReadOnlyDictionary<InstRole, Color> table = dark ? InstallerPalette.Dark : InstallerPalette.Light;
            foreach (KeyValuePair<InstRole, Color> kv in table) {
                Current.Resources["inst." + Kebab(kv.Key.ToString())] = new SolidColorBrush(kv.Value);
            }
            Current.Resources["inst.isDark"] = dark;
        }

        private static string Kebab(string pascal) {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < pascal.Length; i++) {
                char c = pascal[i];
                if (char.IsUpper(c) && i > 0) {
                    sb.Append('-');
                }
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }
    }
}

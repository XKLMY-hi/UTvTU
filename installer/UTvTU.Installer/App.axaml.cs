using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using OpenUtau.Core.Theming;
using OpenUtau.Theming;

namespace UTvTU.Installer;

public partial class App : Application {
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted() {
        RequestedThemeVariant = Environment.GetEnvironmentVariable("UTVTU_INSTALLER_THEME")?.ToLowerInvariant() switch { "light" => ThemeVariant.Light, "dark" => ThemeVariant.Dark, _ => ThemeVariant.Default };
        ApplyTheme(); ActualThemeVariantChanged += (_, _) => ApplyTheme();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
    static void ApplyTheme() => ColorPool.Initialize(0xFF0B57D0, Md3SchemeVariant.TonalSpot, Current?.ActualThemeVariant == ThemeVariant.Dark);
}

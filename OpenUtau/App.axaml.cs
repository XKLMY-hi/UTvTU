using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Styling;
using OpenUtau.App.Views;
using OpenUtau.Colors;
using Serilog;

namespace OpenUtau.App {
    public class App : Application {
        public override void Initialize() {
            Log.Information("Initializing application.");
            AvaloniaXamlLoader.Load(this);
            InitializeCulture();
            InitializeTheme();
            Log.Information("Initialized application.");
        }

        public override void OnFrameworkInitializationCompleted() {
            Log.Information("Framework initialization completed.");
            // 注册打包的 HarmonyOS Sans SC 字体（Assets/Fonts 内嵌资源）——
            // 系统未装该字体的机器也能完整呈现设计字体；DefaultFamilyName
            // 仍为 "HarmonyOS Sans SC"，匹配本集合
            try {
                FontManager.Current.AddFontCollection(new EmbeddedFontCollection(
                    new Uri("fonts:HarmonyOS", UriKind.Absolute),
                    new Uri("avares://OpenUtau/Assets/Fonts/#HarmonyOS Sans SC")));
            } catch (Exception e) {
                Log.Warning(e, "[Font] 打包字体注册失败，回退系统字体");
            }
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
                desktop.MainWindow = new SplashWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }

        public void InitializeCulture() {
            Log.Information("Initializing culture.");
            string sysLang = CultureInfo.InstalledUICulture.Name;
            string prefLang = Core.Util.Preferences.Default.Language;
            var languages = GetLanguages();
            if (languages.ContainsKey(prefLang)) {
                SetLanguage(prefLang);
            } else if (languages.ContainsKey(sysLang)) {
                SetLanguage(sysLang);
                Core.Util.Preferences.Default.Language = sysLang;
                Core.Util.Preferences.Save();
            } else {
                SetLanguage("en-US");
            }

            // Force using InvariantCulture to prevent issues caused by culture dependent string conversion, especially for floating point numbers.
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
            Log.Information("Initialized culture.");
        }

        public static Dictionary<string, IResourceProvider> GetLanguages() {
            if (Current == null) {
                return new();
            }
            var result = new Dictionary<string, IResourceProvider>();
            foreach (string key in Current.Resources.Keys.OfType<string>()) {
                if (key.StartsWith("strings-") &&
                    Current.Resources.TryGetResource(key, ThemeVariant.Default, out var res) &&
                    res is IResourceProvider rp) {
                    result.Add(key.Replace("strings-", ""), rp);
                }
            }
            return result;
        }

        public static void SetLanguage(string language) {
            if (Current == null) {
                return;
            }
            var languages = GetLanguages();
            foreach (var res in languages.Values) {
                Current.Resources.MergedDictionaries.Remove(res);
            }
            if (language != "en-US") {
                Current.Resources.MergedDictionaries.Add(languages["en-US"]);
            }
            if (languages.TryGetValue(language, out var res1)) {
                Current.Resources.MergedDictionaries.Add(res1);
            }
        }

        static async void InitializeTheme() {
            Log.Information("Initializing theme.");
            try {
                CustomTheme.ListThemes();
                await OudepLoaderRegistry.LoadAllAsync();
            } catch (Exception e) {
                Log.Error(e, "Failed to load themes from packages.");
            }
            SetTheme();
            Log.Information("Initialized theme.");
        }

        public static void SetTheme() {
            if (Current == null) {
                return;
            }
            // v4.0：主题状态收敛到 ThemeManager.Apply 单一入口
            ThemeManager.Apply(Core.Util.Preferences.Default.ThemeName);
        }
    }
}

using System;
using Xunit;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using OpenUtau.App;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

public class TestAppBuilder {
    /// <summary>
    /// 可选：把整个测试会话固定在某个变体（T8 验收需要「浅色 / 深色各跑一遍全量」）：
    /// <code>
    /// $env:OPENUTAU_TEST_THEME = "Light"   # 或 "Dark"
    /// dotnet test OpenUtau.Test\OpenUtau.Test.csproj --no-build
    /// </code>
    /// 不设该环境变量时行为与以前完全一致（沿用 Preferences.Default.ThemeName / App 初始化结果）。
    /// </summary>
    internal const string ThemeEnvVar = "OPENUTAU_TEST_THEME";

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions())
        .AfterSetup(_ => ApplyForcedThemeVariant());

    private static void ApplyForcedThemeVariant() {
        string? forced = Environment.GetEnvironmentVariable(ThemeEnvVar);
        if (string.IsNullOrWhiteSpace(forced)) {
            return;
        }
        if (!forced.Equals("Light", StringComparison.OrdinalIgnoreCase) &&
            !forced.Equals("Dark", StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException($"{ThemeEnvVar} 只接受 Light / Dark，收到：{forced}");
        }
        string name = forced.Equals("Dark", StringComparison.OrdinalIgnoreCase) ? "Dark" : "Light";
        // App.InitializeTheme() 是 async void（要等主题包加载完才调 SetTheme），与本钩子的先后顺序不定，
        // 所以两条路都铺上：① 立刻应用一次；② 把偏好里的主题名改成同一个值（**仅内存** ——
        // 测试项目全仓不调用 Preferences.Save，不会写用户设置文件）。
        OpenUtau.Core.Util.Preferences.Default.ThemeName = name;
        OpenUtau.App.ThemeManager.Apply(name);
    }
}

namespace OpenUtau.App {
    public class AppTest {
        [Fact]
        public void BuildTest() {
            Assert.False(typeof(App).IsAbstract);
            Assert.False(typeof(Program).IsAbstract);
        }

        /// <summary>
        /// 在 headless session 内测多语言资源（改自手动 SetupWithoutStarting——
        /// 手动建 App 会残留静态 Dispatcher/MediaContext 状态，污染后续 AvaloniaFact 全量顺序）。
        /// </summary>
        [AvaloniaFact]
        public void StringsTest() {
            var languages = App.GetLanguages();
            Assert.True(languages.Count > 1);
            Assert.Contains("en-US", languages.Keys);
            Assert.Contains("zh-CN", languages.Keys);
            Assert.Contains("ja-JP", languages.Keys);
            foreach (var pair in languages) {
                Assert.NotNull(pair.Value);
            }
        }
    }
}

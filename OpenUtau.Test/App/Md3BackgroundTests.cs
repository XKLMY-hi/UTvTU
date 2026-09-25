using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using OpenUtau.App.Controls;
using OpenUtau.Core.Theming;
using OpenUtau.Theming;
using SukiUI.Enums;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// 背景与色阶编排契约（2026-09-25 用户定）：
    /// ① 窗口背景舍弃 SukiUI（不再渐变/着色器/动画），由颜色池的 md3.surface 提供；
    /// ② 旧的背景/线条/文字/描边/强调色颜色键全部退场，画刷直接接 md3 角色色；
    /// ③ 面板层与控件层按 MD3 容器梯度分工（surface → container → high → highest）。
    /// </summary>
    [Collection("Theme")]   // 这些用例会改全局主题/颜色池，串行执行避免互相污染
    public class Md3BackgroundTests {
        private static Color RoleOf(Md3Role role) => ColorPool.Current.Color(role);

        private static Color BrushColor(Application app, string key) {
            Assert.True(app.TryFindResource(key, out object? value), $"画刷键未解析：{key}");
            return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
        }

        [AvaloniaFact]
        public void Window_AbandonsSukiBackground() {
            var window = new WindowEx();
            Assert.Equal(SukiBackgroundStyle.Flat, window.BackgroundStyle);
            Assert.Null(window.BackgroundShaderFile);
            Assert.Null(window.BackgroundShaderCode);
            Assert.False(window.BackgroundAnimationEnabled);
            Assert.False(window.BackgroundTransitionsEnabled);
        }

        [AvaloniaFact]
        public void LegacyBackgroundBrushes_PointAtColorPool() {
            ColorPool.Initialize(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, false);
            Application app = Application.Current!;
            // 面板层 / 控件层 / 悬停层 / 内容层 / 线条 / 文字 / 描边 / 强调色
            Assert.Equal(RoleOf(Md3Role.Surface), BrushColor(app, "SystemControlBackgroundAltHighBrush"));
            Assert.Equal(RoleOf(Md3Role.SurfaceContainer), BrushColor(app, "PlusBrushSurfaceRaised"));
            Assert.Equal(RoleOf(Md3Role.SurfaceContainerHigh), BrushColor(app, "PlusBrushSurfaceControl"));
            Assert.Equal(RoleOf(Md3Role.SurfaceContainerHighest), BrushColor(app, "PlusBrushSurfaceHover"));
            Assert.Equal(RoleOf(Md3Role.SurfaceContainerLowest), BrushColor(app, "PlusBrushSurfaceDeep"));
            Assert.Equal(RoleOf(Md3Role.SurfaceContainerLow), BrushColor(app, "TrackBackgroundAltBrush"));
            Assert.Equal(RoleOf(Md3Role.OutlineVariant), BrushColor(app, "TickLineBrush"));
            Assert.Equal(RoleOf(Md3Role.OnSurfaceVariant), BrushColor(app, "BarNumberBrush"));
            Assert.Equal(RoleOf(Md3Role.OnSurface), BrushColor(app, "SystemControlForegroundBaseHighBrush"));
            Assert.Equal(RoleOf(Md3Role.Primary), BrushColor(app, "AccentBrush1"));
            Assert.Equal(RoleOf(Md3Role.Tertiary), BrushColor(app, "AccentBrush2"));
        }

        [AvaloniaFact]
        public void LegacyBackgroundBrushes_FollowColorPoolRelight() {
            // 换种子重着色后，旧键派生出来的画刷必须跟着变（证明是活的绑定，不是抄下来的 hex）
            ColorPool.Initialize(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, false);
            Application app = Application.Current!;
            Color before = BrushColor(app, "PlusBrushSurfaceRaised");
            ColorPool.Initialize(0xFF00897B, Md3SchemeVariant.TonalSpot, false);
            Assert.Equal(RoleOf(Md3Role.SurfaceContainer), BrushColor(app, "PlusBrushSurfaceRaised"));
            Assert.NotEqual(before, BrushColor(app, "PlusBrushSurfaceRaised"));
            // 复原默认种子，避免影响同进程其它用例
            ColorPool.Initialize(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, false);
        }
    }
}

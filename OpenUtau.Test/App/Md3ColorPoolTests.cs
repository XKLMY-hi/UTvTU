using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using OpenUtau.Core.Theming;
using OpenUtau.Theming;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// 统一颜色池（MD3 角色）契约测试。
    ///
    /// 覆盖：49 个角色在深浅两套下都能取到颜色／画刷／画笔；XAML 资源键齐全且可解析；
    /// 键名规则稳定；深浅切换会刷新资源并触发 Changed；与基线种子对照谷歌参考值。
    /// </summary>
    public class Md3ColorPoolTests {
        [AvaloniaFact]
        public void AllRoles_ResolveColorBrushPen_LightAndDark() {
            var pool = new Md3ColorPool(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, false);
            foreach (Md3Role role in Enum.GetValues<Md3Role>()) {
                foreach (bool dark in new[] { false, true }) {
                    Color color = pool.Color(role, dark);
                    Assert.Equal(255, color.A);

                    var brush = Assert.IsAssignableFrom<ISolidColorBrush>(pool.Brush(role, dark));
                    Assert.Equal(color, brush.Color);

                    IPen pen = pool.Pen(role);
                    var penBrush = Assert.IsAssignableFrom<ISolidColorBrush>(pen.Brush);
                    Assert.Equal(pool.Color(role), penBrush.Color);
                }
            }
        }

        [AvaloniaFact]
        public void ResourceKeys_CoverAllRoles_AndResolveFromApplication() {
            ColorPool.Initialize(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, false);
            Application app = Application.Current!;
            foreach (Md3Role role in Enum.GetValues<Md3Role>()) {
                Assert.True(app.TryFindResource(ColorPool.Key(role), out object? brush), $"缺画刷键：{ColorPool.Key(role)}");
                Assert.IsAssignableFrom<IBrush>(brush);
                Assert.True(app.TryFindResource(ColorPool.ColorKey(role), out object? color), $"缺颜色键：{ColorPool.ColorKey(role)}");
                Assert.IsType<Color>(color);
            }
        }

        [AvaloniaFact]
        public void KeyNaming_FollowsKebabCaseConvention() {
            Assert.Equal("md3.primary", ColorPool.Key(Md3Role.Primary));
            Assert.Equal("md3.on-primary-container", ColorPool.Key(Md3Role.OnPrimaryContainer));
            Assert.Equal("md3.surface-container-highest", ColorPool.Key(Md3Role.SurfaceContainerHighest));
            Assert.Equal("md3.primary-fixed-dim", ColorPool.Key(Md3Role.PrimaryFixedDim));
            Assert.Equal("md3.color.outline-variant", ColorPool.ColorKey(Md3Role.OutlineVariant));
        }

        [AvaloniaFact]
        public void DarkSwitch_UpdatesResources_AndRaisesChanged() {
            ColorPool.Initialize(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, false);
            Color lightSurface = ColorPool.Instance.Color(Md3Role.Surface);
            Assert.Equal(lightSurface, ResolveResource(ColorPool.Key(Md3Role.Surface)));

            bool raised = false;
            EventHandler handler = (_, _) => raised = true;
            ColorPool.Changed += handler;
            try {
                ColorPool.SetDark(true);
                Color darkSurface = ColorPool.Instance.Color(Md3Role.Surface);
                Assert.NotEqual(lightSurface, darkSurface);
                Assert.True(raised, "深浅切换未触发 Changed");
                Assert.True(ColorPool.Instance.IsDark);
                // 资源随深浅重建：XAML 侧无需感知变体即可取到当前值
                Assert.Equal(darkSurface, ResolveResource(ColorPool.Key(Md3Role.Surface)));

                ColorPool.SetDark(false);
                Assert.Equal(lightSurface, ResolveResource(ColorPool.Key(Md3Role.Surface)));
            } finally {
                ColorPool.Changed -= handler;
                ColorPool.SetDark(false);
            }
        }

        private static Color ResolveResource(string key) {
            Assert.True(Application.Current!.TryFindResource(key, out object? value), $"资源未找到：{key}");
            return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
        }

        [AvaloniaFact]
        public void BaselineSeed_MatchesGoogleReference_ForSpotRoles() {
            ColorPool.Initialize(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, false);
            Md3SchemeColors light = ColorPool.Instance.Colors(false);
            Assert.Equal("#65558f", light.Hex(Md3Role.Primary));
            Assert.Equal("#fdf7ff", light.Hex(Md3Role.Surface));
            Assert.Equal("#1d1b20", light.Hex(Md3Role.OnSurface));
            Assert.Equal("#e6e0e9", light.Hex(Md3Role.SurfaceContainerHighest));
            Assert.Equal("#ba1a1a", light.Hex(Md3Role.Error));

            Md3SchemeColors dark = ColorPool.Instance.Colors(true);
            Assert.Equal("#cfbdfe", dark.Hex(Md3Role.Primary));
            Assert.Equal("#141218", dark.Hex(Md3Role.Surface));
        }

        [AvaloniaFact]
        public void SchemeSwitch_ChangesAccentsButKeepsNeutralSurfaceOrder() {
            var tonalSpot = new Md3ColorPool(ColorPool.DefaultSeed, Md3SchemeVariant.TonalSpot, false);
            var vibrant = new Md3ColorPool(ColorPool.DefaultSeed, Md3SchemeVariant.Vibrant, false);
            Assert.NotEqual(tonalSpot.Color(Md3Role.Primary), vibrant.Color(Md3Role.Primary));

            // 无论方案如何，浅色 surface 必须比 onSurface 亮（层级不可反转）
            foreach (Md3SchemeVariant variant in Enum.GetValues<Md3SchemeVariant>()) {
                var pool = new Md3ColorPool(ColorPool.DefaultSeed, variant, false);
                Assert.True(pool.Color(Md3Role.Surface).R + pool.Color(Md3Role.Surface).G + pool.Color(Md3Role.Surface).B
                    > pool.Color(Md3Role.OnSurface).R + pool.Color(Md3Role.OnSurface).G + pool.Color(Md3Role.OnSurface).B,
                    $"{variant} 方案下 surface/onSurface 层级反转");
            }
        }
    }
}

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpenUtau.Core.Theming;

namespace OpenUtau.Theming {
    /// <summary>
    /// 把颜色池的 MD3 角色写入 Avalonia 资源字典，使 XAML 能声明式取色：
    ///
    ///   Background="{DynamicResource md3.surface-container}"
    ///   BorderBrush="{DynamicResource md3.outline-variant}"
    ///   Foreground="{DynamicResource md3.on-surface}"
    ///
    /// 每个角色提供画刷键（<c>md3.&lt;role&gt;</c>）与颜色键（<c>md3.color.&lt;role&gt;</c>，供渐变使用）。
    /// 安装的是**当前变体**的一套值，深浅色切换时随 <see cref="ColorPool.SetDark"/> 重建——
    /// 这与本应用"主题收敛到 ThemeManager.Apply 单一入口"的现状一致，也避免了 ThemeDictionaries
    /// 与根键并存时的解析歧义。
    /// 与旧资源键**并存**：旧控件不受影响，替换一个控件就迁一个。
    /// </summary>
    public static class Md3ThemeResources {
        private static ResourceDictionary? installed;

        /// <summary>已安装的 MD3 资源字典（未安装时为 null）。</summary>
        public static ResourceDictionary? Installed => installed;

        /// <summary>按当前颜色池重建资源字典并挂到 Application.Resources（替换上一次安装的）。</summary>
        public static void Install(IMd3ColorPool pool) {
            Application? app = Application.Current;
            if (app == null) {
                return;
            }
            var dict = new ResourceDictionary();
            foreach (Md3Role role in Enum.GetValues<Md3Role>()) {
                dict[ColorPool.Key(role)] = pool.Brush(role);
                dict[ColorPool.ColorKey(role)] = pool.Color(role);
            }
            dict["Md3ElevationLevel1"] = BoxShadows.Parse("0 1 3 0 #33000000, 0 1 2 0 #26000000");
            dict["Md3ElevationLevel2"] = BoxShadows.Parse("0 2 6 0 #33000000, 0 1 2 0 #26000000");
            dict["Md3ElevationLevel3"] = BoxShadows.Parse("0 4 8 0 #33000000, 0 1 3 0 #26000000");
            var scrim = pool.Color(Md3Role.Scrim);
            dict["Md3DialogScrim"] = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromArgb(82, scrim.R, scrim.G, scrim.B));
            dict["Md3FocusRing"] = BoxShadows.Parse("0 0 0 2 " + pool.Color(Md3Role.Primary));
            if (installed != null) {
                app.Resources.MergedDictionaries.Remove(installed);
            }
            app.Resources.MergedDictionaries.Add(dict);
            installed = dict;
        }
    }
}

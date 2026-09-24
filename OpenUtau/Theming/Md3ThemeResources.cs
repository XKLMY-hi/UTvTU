using System;
using Avalonia;
using Avalonia.Controls;
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
            if (installed != null) {
                app.Resources.MergedDictionaries.Remove(installed);
            }
            app.Resources.MergedDictionaries.Add(dict);
            installed = dict;
        }
    }
}

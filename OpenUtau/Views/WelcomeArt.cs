using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;

namespace OpenUtau.App.Views {
    /// <summary>欢迎页几何波形的一根柱（设计稿 1-Welcome：宽 6 / 圆角 999 / 高与透明度成阶梯）。</summary>
    public sealed class WelcomeWaveBar {
        public double Height { get; init; }
        public double Opacity { get; init; }
    }

    /// <summary>「已安装音源」chip：一个状态点 + 名称。</summary>
    public sealed class WelcomeSingerChip {
        public string Label { get; init; } = string.Empty;
    }

    /// <summary>
    /// 欢迎页的**声明式美术数据**（W36）。
    ///
    /// 波形取自设计稿 `Welcome.txt` 的 26 根柱（高 18…122、透明度 0.446…1.0），
    /// 这一层是"几何专业风"的核心装饰：纯几何、无渐变无阴影，颜色由 XAML 给 `md3.primary`。
    /// 放在视图层而不是 VM：它是静态版式，不含业务状态。
    /// </summary>
    public static class WelcomeArt {
        /// <summary>设计稿的 26 根柱（逐值抄录，不"美化"）。</summary>
        public static readonly IReadOnlyList<WelcomeWaveBar> Waveform = new[] {
            (18.0, 0.446), (70.0, 0.723), (108.0, 0.925), (122.0, 1.0),
            (109.0, 0.931), (72.0, 0.734), (20.0, 0.457), (68.0, 0.712),
            (107.0, 0.92), (122.0, 1.0), (110.0, 0.936), (74.0, 0.744),
            (22.0, 0.467), (66.0, 0.702), (105.0, 0.909), (122.0, 1.0),
            (111.0, 0.941), (75.0, 0.75), (25.0, 0.483), (64.0, 0.691),
            (104.0, 0.904), (122.0, 1.0), (112.0, 0.947), (77.0, 0.76),
            (27.0, 0.494), (62.0, 0.68),
        }.Select(t => new WelcomeWaveBar { Height = t.Item1, Opacity = t.Item2 }).ToList();

        /// <summary>波形区的**基准高度**（设计基准；实际高度由可用空间弹性决定，上限 200）。</summary>
        public const double BaseWaveHeight = 132.0;

        /// <summary>波形区的**高度上限**（响应式：涨到上限即停，余量交给中段组对称居中）。</summary>
        public const double MaxWaveHeight = 200.0;

        /// <summary>波形区的**高度下限**（低于基准不再压，避免波形被压得不可辨）。</summary>
        public const double MinWaveHeight = BaseWaveHeight;

        /// <summary>
        /// 按目标高度**等比缩放**柱高：柱子的相对高度与透明度阶梯不变 ⇒ **波形不变形**。
        /// 柱宽（6px）与柱间距固定，只改高度系数，观感仍是同一段波形。
        /// </summary>
        public static IReadOnlyList<WelcomeWaveBar> Scaled(double waveHeight) {
            double factor = Math.Max(0.0, waveHeight) / BaseWaveHeight;
            return Waveform
                .Select(b => new WelcomeWaveBar {
                    Height = Math.Max(4.0, b.Height * factor),
                    Opacity = b.Opacity,
                })
                .ToList();
        }

        /// <summary>
        /// 「已安装音源」= **真实**数据（`SingerManager`），最多取 <paramref name="max"/> 个；
        /// 取不到就返回空表 ⇒ 调用方整段隐藏（**不编数**，与 spec-digest §6 同一条纪律）。
        /// </summary>
        public static IReadOnlyList<WelcomeSingerChip> InstalledSingers(int max = 4) {
            try {
                var singers = SingerManager.Inst?.Singers?.Values;
                if (singers == null) {
                    return Array.Empty<WelcomeSingerChip>();
                }
                return singers
                    .Where(s => s != null && s.Found && !string.IsNullOrWhiteSpace(s.Name))
                    .OrderBy(s => s.Name, StringComparer.CurrentCulture)
                    .Take(max)
                    .Select(s => new WelcomeSingerChip {
                        Label = s.SingerType == USingerType.Classic
                            ? $"{s.Name} · UTAU"
                            : $"{s.Name} · {s.SingerType}",
                    })
                    .ToList();
            } catch {
                // 音源索引尚未建立（启动早期 / 测试宿主）⇒ 视为"没有"，不抛
                return Array.Empty<WelcomeSingerChip>();
            }
        }
    }
}

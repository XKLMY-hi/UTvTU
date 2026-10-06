using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;

namespace OpenUtau.App.Views {
    /// <summary>「已安装音源」chip：一个状态点 + 名称。</summary>
    public sealed class WelcomeSingerChip {
        public string Label { get; init; } = string.Empty;
    }

    /// <summary>
    /// 欢迎页的**声明式美术数据**（W36）。
    ///
    /// 波形已从"26 根几何柱"换成**真实一小节演唱**的包络几何（生成器产物，见
    /// `Assets/WelcomeWaveform.axaml`，单一来源、可重生成）；这里只保留**弹性高度**的三个口径常量。
    /// 放在视图层而不是 VM：它是静态版式，不含业务状态。
    /// </summary>
    public static class WelcomeArt {
        /// <summary>波形区的**基准高度**（设计基准；实际高度由可用空间弹性决定，上限 200）。</summary>
        public const double BaseWaveHeight = 132.0;

        /// <summary>波形区的**高度上限**（响应式：涨到上限即停，余量交给中段组对称居中）。</summary>
        public const double MaxWaveHeight = 200.0;

        /// <summary>波形区的**高度下限**（低于基准不再压，避免波形被压得不可辨）。</summary>
        public const double MinWaveHeight = BaseWaveHeight;

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

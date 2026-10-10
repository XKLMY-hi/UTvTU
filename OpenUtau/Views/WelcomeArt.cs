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
    /// 欢迎页的**声明式美术数据**（W50：MD3 重建版）。
    ///
    /// 波形是**真实一小节演唱的响度几何**（生成器产物 `Assets/WelcomeWaveform.axaml`，单一来源、可重生成）。
    /// W50 起它从"左栏中段弹性大图（132–200）"改为**右栏页头右侧的装饰带**：装饰不再与导航抢层级，
    /// 版面全部让给信息（对齐主界面 MD3 的页头 28+14 规格）。
    /// 放在视图层而不是 VM：它是静态版式，不含业务状态。
    /// </summary>
    public static class WelcomeArt {
        /// <summary>页头波形装饰带的高度（XAML 里的 Height 必须与此一致；测试把两者钉在一起防漂移）。</summary>
        public const double WaveBandHeight = 64.0;

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

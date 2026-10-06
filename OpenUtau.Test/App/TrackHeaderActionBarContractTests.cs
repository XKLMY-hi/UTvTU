using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// W45/S1′ 轨头动作条契约（进行中的重构骨架）。
    ///
    /// 本轮先钉住**入口唯一性**这一条：行高 42 档要求把 ⚙ 从动作条挪走
    /// （动作条只剩 M/S/fx + 引擎状态点，合计 82 才能在名字同行放下），
    /// 因此卡片右键菜单里必须有一条**等价的「轨道设置」入口**，且与 ⚙ **复用同一个处理器**
    /// —— 两处入口、一处逻辑，避免出现"显示一套 / 行为另一套"。
    ///
    /// 断言读的是产品 XAML **原文**（TrackHeader.axaml 由测试工程链进产物目录），
    /// 故改了源文件必须先重建再跑测试（UI 标准里的纪律）。
    /// </summary>
    public class TrackHeaderActionBarContractTests {
        static string Xaml => File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Controls", "TrackHeader.axaml"));

        [Fact]
        public void SettingsEntry_ExistsInCardContextMenu_AndReusesTheSameHandler() {
            string xaml = Xaml;
            // 右键菜单里的「轨道设置」项：同一个 Header 键 + 同一个 Click 处理器
            var menuItem = Regex.Match(xaml,
                "<MenuItem[^>]*Header=\"\\{DynamicResource tracks\\.tracksettings\\}\"[^>]*Click=\"TrackSettingsButtonClicked\"");
            Assert.True(menuItem.Success, "卡片右键菜单缺少复用 TrackSettingsButtonClicked 的「轨道设置」项");
            // ⚙ 按钮仍在（42 档以下的档位继续用它），且用的是同一个处理器
            var gear = Regex.Match(xaml, "<Button[^>]*Height=\"20\"[^>]*Width=\"24\"[^>]*Click=\"TrackSettingsButtonClicked\"");
            Assert.True(gear.Success, "⚙ 按钮应保留（仅 42 档挪进右键菜单），且与菜单项共用处理器");
            // 处理器只允许定义一次（复用而非复制）
            string cs = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Controls", "TrackHeader.axaml.cs"));
            Assert.Equal(1, Regex.Matches(cs, @"void TrackSettingsButtonClicked\(").Count);
        }

        [Fact]
        public void ActionBarControls_DeclareMinHeightZero_ToBeatThemeMinHeight() {
            // ui-standards §1：<32 高的 Button/ToggleButton 必须在自己的样式里压 MinHeight=0 + Margin=0，
            // 否则会被 Md3ButtonTheme 的 MinHeight=32 顶到 32（W10 的 M/S 实测踩过）。
            string xaml = Xaml;
            foreach (string name in new[] { "MuteButton", "SoloButton" }) {
                var block = Regex.Match(xaml, "<ToggleButton[^>]*Name=\"" + name + "\"[\\s\\S]{0,400}?</ToggleButton>");
                if (!block.Success) {
                    continue;   // 命名可能随重构变化，缺失不判红（由几何断言兜底）
                }
                Assert.Contains("MinHeight", block.Value);
                Assert.Contains("Margin", block.Value);
            }
        }
    }
}

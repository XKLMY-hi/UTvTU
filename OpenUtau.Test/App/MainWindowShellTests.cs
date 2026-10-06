using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// 主窗外壳契约（S1，对齐设计稿 2-Main-Window）：
    /// 顶栏 56 / 状态条 32 / 轨道头列 264 / 素材库列 296（表头 44 + 页签 40）；
    /// 品牌区当菜单按钮；老侧栏（240 宽、项目页）已退役。
    /// </summary>
    public class MainWindowShellTests {
        private static string ReadXaml() =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Views", "MainWindow.axaml"));

        [AvaloniaFact]
        public void Shell_UsesDesignGeometry() {
            string xaml = ReadXaml();
            // 三行骨架：顶栏 56 / 工作区 / 状态条 32
            Assert.Contains("RowDefinitions=\"56,*,32\"", xaml);
            // 工作区五列骨架（W16 面板系统）：左面板 / 分隔条 / 中央 / 分隔条 / 右面板。
            // 面板宽**不再写死在列定义里**，而是来自绑定（宽度与折叠态可调、可持久化）——
            // 所以这里断言"列宽来自绑定"，以后调默认值不会再假红（默认值见 PanelLayoutPreferences）。
            Assert.Contains("x:Name=\"MainLayout\" ColumnDefinitions=\"Auto,Auto,*,Auto,Auto\"", xaml);
            Assert.Contains("Width=\"{Binding #TracksPanelSplitter.PanelWidth}\"", xaml);
            Assert.Contains("Width=\"{Binding #LibraryPanelSplitter.PanelWidth}\"", xaml);
            Assert.Contains("IsVisible=\"{Binding #LibraryPanelSplitter.PanelShown}\"", xaml);
            // 素材库内部：表头 44 / 页签 40 / 内容
            Assert.Contains("RowDefinitions=\"44,40,*\"", xaml);
            // 编排区标尺 34（与轨道头表头同高）
            Assert.Contains("<RowDefinition Height=\"34\"/>", xaml);
        }

        [AvaloniaFact]
        public void Shell_BrandOpensAppMenu() {
            string xaml = ReadXaml();
            Assert.Contains("x:Name=\"AppMenuButton\"", xaml);
            Assert.Contains("Click=\"OnAppMenuClicked\"", xaml);
            Assert.Contains("<MenuFlyout", xaml);
            // 五个顶层菜单都还在
            foreach (string key in new[] { "menu.file", "menu.edit", "menu.project", "menu.tools", "menu.help" }) {
                Assert.Contains($"Header=\"{{DynamicResource {key}}}\"", xaml);
            }
        }

        [AvaloniaFact]
        public void Shell_HasLibraryTabs() {
            string xaml = ReadXaml();
            foreach (string tab in new[] { "SingersTab", "SamplesTab", "MidiTab", "EffectsTab" }) {
                Assert.Contains($"x:Name=\"{tab}\"", xaml);
            }
            foreach (string key in new[] { "sidebar.singers", "sidebar.samples", "sidebar.midi", "sidebar.effects" }) {
                Assert.Contains($"{{DynamicResource {key}}}", xaml);
            }
            foreach (string page in new[] { "SingersPanel", "SamplesPanel", "MidiPanel", "VstPanel" }) {
                Assert.Contains($"x:Name=\"{page}\"", xaml);
            }
        }

        [AvaloniaFact]
        public void Shell_RetiresOldSidebar() {
            string xaml = ReadXaml();
            foreach (string gone in new[] { "SidebarSplitter", "SidebarOpenBtn", "ProjectsTab", "LibraryTab", "sideTab", "subTab" }) {
                Assert.DoesNotContain(gone, xaml);
            }
        }

        [AvaloniaFact]
        public void Shell_ChromeUsesMd3Roles() {
            string xaml = ReadXaml();
            // 新外壳（顶栏 / 状态条 / 素材库）只吃颜色池角色键。
            // 注：`md3.primary-container` / `md3.on-primary-container` 于 W40 退出外壳 ——
            // 顶栏那颗音符瓦片被品牌锁定（标志 md3.primary + 字标 md3.on-surface）替换，用户要求删瓦片。
            foreach (string key in new[] {
                "md3.surface-container",
                "md3.surface-container-high", "md3.on-surface", "md3.on-surface-variant",
                "md3.outline-variant", "md3.primary", "md3.on-primary",
            }) {
                Assert.Contains($"{{DynamicResource {key}}}", xaml);
            }
            // 撤销/重做进右侧图标组（稿子没有，用户裁定保留功能）
            Assert.Contains("Command=\"{Binding Undo}\"", xaml);
            Assert.Contains("Command=\"{Binding Redo}\"", xaml);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using OpenUtau.App;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.App.Views;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using OpenUtau.Core.Vst;
using OpenUtau.Theming;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// W4（决策 B6）：素材库「效果器」页签 = 插件浏览器 + 插件扫描路径管理契约。
    ///
    /// 覆盖：空态 / 列表投影（只列效果器、类型徽标、按名排序）/ 搜索过滤与"未找到" /
    /// 路径增删往返（同一份 <c>Preferences.Default.VstScanPaths</c>）/ **两处同步**（广播 + 内容比对防环）/
    /// 拖拽负载与 W3 冻结契约一致 / 页签结构（源码契约）/ 新增键 EN+zh 成对。
    ///
    /// 插件来源用**测试接缝**注入假数据（不清真扫盘）：<c>PluginBrowserViewModel(pluginSource, rescanAction)</c>。
    /// </summary>
    [Collection("Theme")]   // 会切全局语言 / 改 Preferences.Default 字段，与其它全局态用例串行
    public class EffectsTabTests {
        /// <summary>与语言无关的键（路径示例），EN/zh 取值**应当相同**。</summary>
        static readonly string[] LanguageNeutralKeys = { "sidebar.effects.paths.watermark" };

        /// <summary>W4 新增的字符串键（fx-rack 之外的 mx-lib 区块；EN/zh 各一份）。</summary>
        static readonly string[] NewKeys = {
            "sidebar.effects.search", "sidebar.effects.countlabel", "sidebar.effects.scanning",
            "sidebar.effects.empty", "sidebar.effects.nomatch", "sidebar.effects.paths",
            "sidebar.effects.paths.manage", "sidebar.effects.paths.watermark", "sidebar.effects.paths.hint",
        };

        static VstPluginInfo Info(string uid, string name, string vendor, VstPluginType type, bool isEffect = true) =>
            new VstPluginInfo {
                PluginUid = uid, PluginName = name, Vendor = vendor, PluginPath = $@"C:\Plugins\{name}.dll",
                PluginType = type, IsEffect = isEffect,
            };

        /// <summary>三个插件：两个效果器（VST3/VST2）+ 一个乐器（不该出现在效果器页签）。</summary>
        static List<VstPluginInfo> SamplePlugins() => new() {
            Info("uid-ott", "OTT", "Xfer Records", VstPluginType.VST3),
            Info("uid-reaeq", "ReaEQ", "Cockos", VstPluginType.VST2),
            Info("uid-serum", "Serum", "Xfer Records", VstPluginType.VST3, isEffect: false),
        };

        static PluginBrowserViewModel Browser(IReadOnlyList<VstPluginInfo> plugins = null, Action rescan = null) =>
            new PluginBrowserViewModel(() => plugins ?? SamplePlugins(), rescan);

        static WindowEx Host(Control content) {
            var win = new WindowEx { Width = 400, Height = 600, Content = content };
            win.Show();
            win.Measure(new Size(400, 600));
            win.Arrange(new Rect(0, 0, 400, 600));
            Dispatcher.UIThread.RunJobs();
            return win;
        }

        // ── 列表投影 ────────────────────────────────────────────────────────

        [AvaloniaFact]
        public void Browser_ListsEffectsOnly_WithBadgesAndNameOrder() {
            using var vm = Browser();
            Assert.Equal(2, vm.Plugins.Count);                      // 乐器被滤掉
            Assert.Equal(new[] { "OTT", "ReaEQ" }, vm.Plugins.Select(p => p.Name));   // 按名排序
            Assert.Equal(new[] { "VST3", "VST2" }, vm.Plugins.Select(p => p.Badge));
            Assert.Equal("uid-ott", vm.Plugins[0].Uid);             // 拖拽负载就是它
            Assert.Equal("Xfer Records", vm.Plugins[0].Vendor);
            Assert.True(vm.Plugins[0].HasVendor);
            Assert.True(vm.HasPlugins);
            Assert.False(vm.ShowNoPlugins);
            Assert.False(vm.ShowNoMatch);
            Assert.True(vm.ShowCount);
            Assert.Equal(2, vm.PluginCount);
        }

        [AvaloniaFact]
        public void Browser_EmptySource_ShowsEmptyState() {
            using var vm = Browser(new List<VstPluginInfo>());
            Assert.Empty(vm.Plugins);
            Assert.True(vm.ShowNoPlugins);
            Assert.False(vm.HasPlugins);
            Assert.False(vm.ShowCount);
            Assert.False(vm.ShowNoMatch);
            Assert.Equal(0, vm.PluginCount);
        }

        [AvaloniaFact]
        public void Browser_BadgeMapping_MatchesChainPanelConvention() {
            // 与 VstPluginSlot.PluginTypeDisplay 同口径（链面板徽标）
            Assert.Equal("VST3", PluginBrowserViewModel.BadgeFor(VstPluginType.VST3, true));
            Assert.Equal("VST2", PluginBrowserViewModel.BadgeFor(VstPluginType.VST2, true));
            Assert.Equal("VST3i", PluginBrowserViewModel.BadgeFor(VstPluginType.VST3, false));
            Assert.Equal("VST2i", PluginBrowserViewModel.BadgeFor(VstPluginType.VST2, false));
        }

        [AvaloniaFact]
        public void Browser_SearchFiltersByNameVendorAndBadge() {
            using var vm = Browser();
            vm.SearchText = "rea";
            Assert.Single(vm.Plugins);
            Assert.Equal("ReaEQ", vm.Plugins[0].Name);
            Assert.False(vm.ShowNoMatch);

            vm.SearchText = "xfer";                                 // 厂商命中：两个都是 Xfer，但乐器已滤掉
            Assert.Single(vm.Plugins);

            vm.SearchText = "vst2";                                 // 徽标命中
            Assert.Equal("ReaEQ", vm.Plugins[0].Name);

            vm.SearchText = "zzz";
            Assert.Empty(vm.Plugins);
            Assert.True(vm.ShowNoMatch);                            // 「未找到」提示
            Assert.False(vm.ShowNoPlugins);                          // 但空态不该出现
            Assert.True(vm.HasPlugins);

            vm.SearchText = string.Empty;
            Assert.Equal(2, vm.Plugins.Count);
            Assert.False(vm.ShowNoMatch);
        }

        [AvaloniaFact]
        public async Task Browser_RescanRefreshesListAndPublishes() {
            var plugins = SamplePlugins();
            bool scanned = false;
            using var vm = new PluginBrowserViewModel(
                () => plugins,
                () => {
                    scanned = true;
                    plugins.Add(Info("uid-new", "AmpVerb", "Acme", VstPluginType.VST3));   // 扫描后多了一个
                });
            int notifications = 0;
            using var sub = ReactiveUI.MessageBus.Current
                .Listen<VstLibraryChangedNotification>()
                .Subscribe(_ => notifications++);

            Assert.Equal(2, vm.Plugins.Count);
            await vm.RescanInBackgroundAsync();          // 扫描在后台，续体回 UI 线程刷新

            Assert.True(scanned, "重扫动作应被调用");
            Assert.Equal(3, vm.Plugins.Count);           // 新插件出现在列表里
            Assert.Equal("AmpVerb", vm.Plugins[0].Name);
            Assert.Equal(1, notifications);              // 恰好广播一次
            Assert.False(vm.IsScanning);
        }

        // ── 路径管理 + 两处同步 ─────────────────────────────────────────────

        [AvaloniaFact]
        public void Browser_ScanPaths_AddRemoveRoundTripsThroughPreferences() {
            List<string> backup = Preferences.Default.VstScanPaths?.ToList() ?? new List<string>();
            try {
                Preferences.Default.VstScanPaths = new List<string>();
                using var vm = Browser();

                Assert.Empty(vm.ScanPaths);
                Assert.True(vm.AddScanPath(@"  C:\Fake\A  "));       // 去空白后写入
                Assert.Equal(new[] { @"C:\Fake\A" }, vm.ScanPaths);
                Assert.Equal(new[] { @"C:\Fake\A" }, Preferences.Default.VstScanPaths);   // 同一份存储

                Assert.False(vm.AddScanPath(@"C:\Fake\A"));          // 重复不加
                Assert.Single(vm.ScanPaths);

                vm.NewPath = @"C:\Fake\B";
                Assert.True(vm.AddPathFromInput());
                Assert.Equal(string.Empty, vm.NewPath);              // 成功后清空输入
                Assert.Equal(2, vm.ScanPaths.Count);

                Assert.True(vm.RemoveScanPath(@"C:\Fake\A"));
                Assert.Equal(new[] { @"C:\Fake\B" }, vm.ScanPaths);
                Assert.Equal(new[] { @"C:\Fake\B" }, Preferences.Default.VstScanPaths);
                Assert.False(vm.RemoveScanPath(@"C:\Fake\A"));       // 已不在，返回 false
            } finally {
                Preferences.Default.VstScanPaths = backup;
                Preferences.Save();
            }
        }

        /// <summary>
        /// 两处同步（方向一）：素材库改动 → 广播 → 另一侧（此处用第二个浏览 VM 代表"偏好设置那一处"的监听端）
        /// 即时跟随；内容一致时不产生回环（用计数式断言证明"各发一次"）。
        /// </summary>
        [AvaloniaFact]
        public void Browser_ChangeBroadcasts_AndOtherSideFollows() {
            List<string> backup = Preferences.Default.VstScanPaths?.ToList() ?? new List<string>();
            try {
                Preferences.Default.VstScanPaths = new List<string>();
                using var library = Browser();      // 素材库页签
                using var other = Browser();        // 代表"偏好设置"侧的监听端（同一份存储 + 同一广播）

                int received = 0;
                using var sub = ReactiveUI.MessageBus.Current
                    .Listen<VstLibraryChangedNotification>()
                    .Subscribe(_ => received++);

                Assert.True(library.AddScanPath(@"C:\Fake\Shared"));
                Assert.Equal(1, received);                                  // 恰好一条广播
                Assert.Equal(new[] { @"C:\Fake\Shared" }, other.ScanPaths); // 另一侧已跟上（同一 UI 线程内同步完成）

                // 反向：外部（偏好设置）改了存储 + 广播 → 素材库跟随
                Preferences.Default.VstScanPaths = new List<string> { @"C:\Fake\Shared", @"C:\Fake\External" };
                VstLibraryChangedNotification.Publish(VstLibraryChangedNotification.SourcePreferences);
                Assert.Equal(2, library.ScanPaths.Count);
                Assert.Equal(2, received);                                  // 自己发的广播不会被自己再发一次（防环）
            } finally {
                Preferences.Default.VstScanPaths = backup;
                Preferences.Save();
            }
        }

        /// <summary>
        /// 两处同步（方向二 / 真视图）：<c>PreferencesView</c> 挂载后监听广播，
        /// 素材库改动 → 该页的路径列表自动重载（直接走视图的订阅与重载方法，不点按钮）。
        /// </summary>
        [AvaloniaFact]
        public void PreferencesView_ReloadsScanPaths_WhenLibraryChanges() {
            List<string> backup = Preferences.Default.VstScanPaths?.ToList() ?? new List<string>();
            try {
                Preferences.Default.VstScanPaths = new List<string> { @"C:\Fake\Old" };
                var preferences = new PreferencesViewModel();
                var view = new PreferencesView { DataContext = preferences };
                var win = Host(view);
                try {
                    Assert.Equal(new[] { @"C:\Fake\Old" }, preferences.VstScanPaths);

                    using var library = Browser();
                    Assert.True(library.AddScanPath(@"C:\Fake\New"));
                    Dispatcher.UIThread.RunJobs();      // 视图侧用 Post 编组，跑空队列

                    Assert.Equal(new[] { @"C:\Fake\Old", @"C:\Fake\New" }, preferences.VstScanPaths);
                } finally {
                    win.Close();
                }
            } finally {
                Preferences.Default.VstScanPaths = backup;
                Preferences.Save();
            }
        }

        // ── 拖拽负载（与 W3 冻结契约一致） ──────────────────────────────────

        [AvaloniaFact]
        public void PluginDragData_UsesFrozenFxChainContract() {
            using var vm = Browser();
            VstPluginItem item = vm.Plugins[0];

            DataTransfer data = PluginBrowserViewModel.CreatePluginDragData(item);
            Assert.Equal("OpenUtau.FxChainItem", FxChainDragData.FormatName);
            string payload = data.TryGetValue(FxChainDragData.Format);
            Assert.Equal("uid-ott", payload);                       // 负载 = 插件 UID
            Assert.Equal(FxChainDragData.VstPayload(item.Uid), payload);
            Assert.Null(FxChainDragData.BuiltInOf(payload));         // VST 负载不是内置负载
        }

        [AvaloniaFact]
        public void PluginDragData_BuiltInPayload_Untouched() {
            // 素材库只发起 VST 负载；内置负载仍由 W3 的契约提供（回归保护）
            string builtIn = FxChainDragData.BuiltInPayload(MixFxModule.Eq);
            Assert.StartsWith(FxChainDragData.BuiltInPrefix, builtIn);
            Assert.Equal("eq", FxChainDragData.BuiltInOf(builtIn)!.Id);
        }

        // ── 页签结构（源码契约，与 MainWindowShellTests 同款做法） ──────────

        [AvaloniaFact]
        public void MainWindow_EffectsTab_HasPluginBrowserStructure() {
            string xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Views", "MainWindow.axaml"));

            // 四页签外壳保持不变（MainWindowShellTests 也断言这些名字）
            Assert.Contains("x:Name=\"VstPanel\"", xaml);
            Assert.Contains("x:Name=\"EffectsTab\"", xaml);
            Assert.Contains("{DynamicResource sidebar.effects}", xaml);

            // 浏览器：搜索框 36 胶囊 + 列表 + 空态/未找到 + 路径管理 + 拖拽源
            Assert.Contains("x:Name=\"PluginSearchBox\"", xaml);
            Assert.Contains("Height=\"36\" CornerRadius=\"999\"", xaml);
            Assert.Contains("Watermark=\"{DynamicResource sidebar.effects.search}\"", xaml);
            Assert.Contains("ItemsSource=\"{Binding Plugins}\"", xaml);
            Assert.Contains("DataContext=\"{Binding PluginBrowser}\"", xaml);
            Assert.Contains("{Binding ShowNoPlugins}", xaml);
            Assert.Contains("{Binding ShowNoMatch}", xaml);
            Assert.Contains("x:Name=\"VstPathManager\"", xaml);
            Assert.Contains("ItemsSource=\"{Binding ScanPaths}\"", xaml);
            Assert.Contains("x:DataType=\"vm:VstPluginItem\"", xaml);
            Assert.Contains("Height=\"46\"", xaml);                          // 列表项 46 高（圆角 8 来自 sideItem）
            Assert.Contains("Classes=\"sideItem\"", xaml);
            Assert.Contains("PointerPressed=\"OnPluginPointerPressed\"", xaml);
            Assert.Contains("PointerMoved=\"OnPluginPointerMoved\"", xaml);

            // 处理器都在代码后置
            string code = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Views", "MainWindow.axaml"));
            Assert.Contains("Click=\"OnRescanVstFromLibrary\"", code);
            Assert.Contains("Click=\"OnToggleVstPathManager\"", code);
            Assert.Contains("Click=\"OnAddVstPathFromLibrary\"", code);
            Assert.Contains("Click=\"OnRemoveVstPathFromLibrary\"", code);
        }

        // ── 文案 ────────────────────────────────────────────────────────────

        [AvaloniaFact]
        public void EffectsTab_NewKeys_ResolveInBothLanguages() {
            var en = new Dictionary<string, string>();
            OpenUtau.App.App.SetLanguage("en-US");
            foreach (string key in NewKeys) {
                Assert.True(ThemeManager.TryGetString(key, out string value), $"EN 缺键：{key}");
                Assert.NotEqual(key, value);
                Assert.False(string.IsNullOrWhiteSpace(value), $"EN 空值：{key}");
                en[key] = value;
            }
            OpenUtau.App.App.SetLanguage("zh-CN");
            try {
                foreach (string key in NewKeys) {
                    Assert.True(ThemeManager.TryGetString(key, out string value), $"zh-CN 缺键：{key}");
                    Assert.NotEqual(key, value);
                    if (LanguageNeutralKeys.Contains(key)) {
                        Assert.Equal(en[key], value);   // 路径示例：两语相同（有意为之）
                    } else {
                        // 与英文不同 ⇒ zh 字典确实有该键（否则会回退成英文值）
                        Assert.NotEqual(en[key], value);
                    }
                }
            } finally {
                OpenUtau.App.App.SetLanguage("en-US");
            }
        }

        /// <summary>复用既有键（不新增平行文案）：路径管理按钮与重扫按钮沿用偏好设置那套。</summary>
        [AvaloniaFact]
        public void EffectsTab_ReusesPreferenceKeys() {
            OpenUtau.App.App.SetLanguage("en-US");
            foreach (string key in new[] { "prefs.vst.add", "prefs.vst.remove", "prefs.vst.rescan", "prefs.vst.scanpaths" }) {
                Assert.True(ThemeManager.TryGetString(key, out string value), $"缺键：{key}");
                Assert.NotEqual(key, value);
            }
        }
    }
}

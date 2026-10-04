using System;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using OpenUtau.App;
using OpenUtau.App.ViewModels;
using OpenUtau.App.Views;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// S5 视图化契约（决策 A1/A2/A4/A5 + §11-S5）：
    /// 顶栏胶囊几何（容器 36 / 内边距 3；选项 30 / 左右 18 / 文字 11 semibold）；
    /// 工作台 / 钢琴卷帘 / 混音台**同格叠放**（切视图只翻可见性 ⇒ 不参与布局、无抖动）；
    /// 视图级「分离」+ 状态持久化；三条既有语义（Ctrl+M / 双击片段进卷帘 / 返回路径）各一条断言。
    /// 说明：headless 起不了 MainWindow（构造即拉 Updater/定时器），故窗口层用 XAML/源码契约 +
    /// 纯策略层（ViewSwitcherPolicy / ViewSwitcherState / MapGlobalShortcut）行为断言。
    /// </summary>
    [Collection("Theme")]   // 末条用例切全局语言，与其它主题/文案用例串行
    public class ViewSwitcherTests {
        private static string Xaml() =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Views", "MainWindow.axaml"));

        private static string WindowCode() =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Views", "MainWindow.axaml.cs"));

        // ── 1. 胶囊几何（A4 / 设计稿 VST-Plugin Tab Bar）────────────────────

        [AvaloniaFact]
        public void Capsule_UsesDesignGeometry() {
            string xaml = Xaml();
            // 容器：高 36 / 内边距 3 / 圆角 full
            Assert.Contains("<Style Selector=\"Border.viewSwitch\">", xaml);
            Assert.Contains("x:Name=\"ViewSwitcher\" Classes=\"viewSwitch\"", xaml);
            Assert.Contains("<Setter Property=\"Height\" Value=\"36\"/>", xaml);
            Assert.Contains("<Setter Property=\"Padding\" Value=\"3\"/>", xaml);
            // 选项：高 30 / 左右 18 / 文字 11 semibold（圆角 full 与容器同）
            Assert.Contains("<Style Selector=\"Button.viewTab\">", xaml);
            Assert.Contains("<Setter Property=\"Height\" Value=\"30\"/>", xaml);
            Assert.Contains("<Setter Property=\"Padding\" Value=\"18,0\"/>", xaml);
            Assert.Contains("<Setter Property=\"FontSize\" Value=\"11\"/>", xaml);
            Assert.Contains("<Setter Property=\"FontWeight\" Value=\"SemiBold\"/>", xaml);
            // 容器 36 = 内边距 3 + 选项 30 + 内边距 3：三处几何必须同时成立
            Assert.Contains("<StackPanel Orientation=\"Horizontal\" Spacing=\"3\">", xaml);
            // 颜色只取 md3.*（无硬编码色值）
            Assert.Contains("Button.viewTab.selected", xaml);
            Assert.Contains("{DynamicResource md3.secondary-container}", xaml);
        }

        // ── 2. 三个工作视图都在胶囊里；Tag 能被枚举解析 ────────────────────

        [AvaloniaFact]
        public void Capsule_CoversThreeWorkViews() {
            string xaml = Xaml();
            foreach (string name in new[] { "ViewTabWorkspace", "ViewTabPianoRoll", "ViewTabMixer" }) {
                Assert.Contains($"x:Name=\"{name}\"", xaml);
            }
            foreach (string key in new[] { "view.workspace", "view.pianoroll", "view.mixer" }) {
                Assert.Contains($"{{DynamicResource {key}}}", xaml);
            }
            Assert.Contains("Classes.selected=\"{Binding ViewSwitcher.ShowWorkspace}\"", xaml);
            Assert.Contains("Classes.selected=\"{Binding ViewSwitcher.ShowPianoRoll}\"", xaml);
            Assert.Contains("Classes.selected=\"{Binding ViewSwitcher.ShowMixer}\"", xaml);
            // Tag → AppSurface（拼错会让胶囊点了没反应，这里把映射钉住）
            foreach (string tag in new[] { "workspace", "pianoroll", "mixer" }) {
                Assert.Contains($"Tag=\"{tag}\"", xaml);
                Assert.True(Enum.TryParse<AppSurface>(tag, ignoreCase: true, out var surface));
                Assert.True(ViewSwitcherPolicy.IsWorkView(surface), $"Tag {tag} 不是工作视图");
            }
        }

        // ── 3. 视图化结构：停靠行退役 + 视图铺满工作区行（无布局抖动）────────

        [AvaloniaFact]
        public void Views_CoverWorkspaceRow_DockedRowsRetired() {
            string xaml = Xaml();
            // 工作台 = 三列（轨头 264 / 编排 / 素材库 296），列宽固定；两个整行视图宿主铺满这三列
            Assert.Contains("x:Name=\"MainLayout\" ColumnDefinitions=\"264,*,296\"", xaml);
            Assert.Contains("x:Name=\"ArrangementArea\"", xaml);
            Assert.Contains("x:Name=\"PianoRollContainer\"", xaml);
            Assert.Contains("x:Name=\"MixerContainer\"", xaml);
            Assert.Contains("IsVisible=\"{Binding ViewSwitcher.ShowPianoRoll}\"", xaml);
            Assert.Contains("IsVisible=\"{Binding ViewSwitcher.ShowMixer}\"", xaml);
            // 视图宿主跨全部三列（设计稿里卷帘/混音台都是整屏；混音台区还是「满宽」）
            int spanAll = xaml.Split("Grid.Column=\"0\" Grid.ColumnSpan=\"3\"").Length - 1;
            Assert.True(spanAll >= 2, $"卷帘/混音台宿主应跨三列，实际出现 {spanAll} 次");
            // 工作台三列随视图显隐（列宽本身不变 ⇒ 几何恒定、无抖动）
            int workspaceBindings = xaml.Split("IsVisible=\"{Binding ViewSwitcher.ShowWorkspace}\"").Length - 1;
            Assert.True(workspaceBindings >= 3, $"工作台三列都应随 ShowWorkspace 显隐，实际 {workspaceBindings} 处");
            // 编排区几何恒定：只有 标尺 34 / 横滚 16 / 内容 * 三行
            Assert.Contains("<RowDefinition Height=\"34\"/>", xaml);
            Assert.Contains("<RowDefinition Height=\"16\"/>", xaml);
            foreach (string gone in new[] {
                "PianoRollRow", "ResizeTooltip", "GridSplitter", "OnMixerSplitterPressed",
                "RowDefinition Height=\"3*\"", "IsVisible=\"{Binding ShowPianoRoll}\"",
                "IsVisible=\"{Binding ShowMixer}\"", "PianoRollMinHeight", "MixerMinHeight",
            }) {
                Assert.DoesNotContain(gone, xaml);
            }
            // 停靠行的拖拽/提示代码也一并退役（.cs 侧）
            string code = WindowCode();
            foreach (string gone in new[] { "RowDefinitions[6]", "OnMixerSplitterMoved", "UpdateResizeTooltip" }) {
                Assert.DoesNotContain(gone, code);
            }
        }

        // ── 4. 视图切换：可见性互斥（最小可见性断言）───────────────────────

        [AvaloniaFact]
        public void ViewSwitcher_VisibilityIsExclusive() {
            var switcher = new ViewSwitcherState();
            Assert.True(switcher.ShowWorkspace);
            Assert.False(switcher.ShowPianoRoll);
            Assert.False(switcher.ShowMixer);

            switcher.SwitchTo(AppSurface.PianoRoll);
            Assert.False(switcher.ShowWorkspace);
            Assert.True(switcher.ShowPianoRoll);
            Assert.False(switcher.ShowMixer);

            switcher.SwitchTo(AppSurface.Mixer);
            Assert.False(switcher.ShowWorkspace);
            Assert.False(switcher.ShowPianoRoll);
            Assert.True(switcher.ShowMixer);

            // 返回路径 = 顶栏胶囊「工作台」（A5：返回走顶栏）
            switcher.SwitchToWorkspace();
            Assert.True(switcher.ShowWorkspace);
            Assert.False(switcher.ShowPianoRoll);
            Assert.False(switcher.ShowMixer);

            // overlay 面不许混进工作区切换
            Assert.Throws<ArgumentOutOfRangeException>(() => switcher.SwitchTo(AppSurface.Welcome));
            Assert.Throws<ArgumentOutOfRangeException>(() => switcher.SwitchTo(AppSurface.Preferences));
        }

        [AvaloniaFact]
        public void ViewSwitcher_RaisesVisibilityNotifications() {
            var switcher = new ViewSwitcherState();
            var changed = new System.Collections.Generic.List<string>();
            switcher.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);
            switcher.SwitchTo(AppSurface.Mixer);
            Assert.Contains(nameof(ViewSwitcherState.CurrentView), changed);
            Assert.Contains(nameof(ViewSwitcherState.ShowWorkspace), changed);
            Assert.Contains(nameof(ViewSwitcherState.ShowPianoRoll), changed);
            Assert.Contains(nameof(ViewSwitcherState.ShowMixer), changed);
        }

        // ── 5. SetChromeForView 契约（视图名键 / 运输条 / 右侧组 / 胶囊 / 分离）─

        [AvaloniaFact]
        public void Chrome_ContractPerSurface() {
            var welcome = ViewSwitcherPolicy.ChromeFor(AppSurface.Welcome);
            Assert.Equal("view.welcome", welcome.TitleKey);
            Assert.False(welcome.ShowTransport);
            Assert.False(welcome.ShowViewSwitcher);
            Assert.False(welcome.ShowDetachButton);

            var prefs = ViewSwitcherPolicy.ChromeFor(AppSurface.Preferences);
            Assert.Equal("prefs.caption", prefs.TitleKey);
            Assert.False(prefs.ShowViewSwitcher);
            Assert.False(prefs.ShowDetachButton);

            var workspace = ViewSwitcherPolicy.ChromeFor(AppSurface.Workspace);
            Assert.Equal("view.workspace", workspace.TitleKey);
            Assert.True(workspace.ShowTransport);
            Assert.True(workspace.ShowViewSwitcher);
            Assert.False(workspace.ShowDetachButton);   // 工作台不可分离

            foreach (var surface in new[] { AppSurface.PianoRoll, AppSurface.Mixer }) {
                var chrome = ViewSwitcherPolicy.ChromeFor(surface);
                Assert.True(chrome.ShowTransport, $"{surface} 应保留运输组");
                Assert.True(chrome.ShowViewSwitcher, $"{surface} 应显示胶囊");
                Assert.True(chrome.ShowDetachButton, $"{surface} 应显示分离按钮（A5）");
            }
            Assert.Equal("view.pianoroll", ViewSwitcherPolicy.ChromeFor(AppSurface.PianoRoll).TitleKey);
            Assert.Equal("view.mixer", ViewSwitcherPolicy.ChromeFor(AppSurface.Mixer).TitleKey);

            // 运输组与右侧组已是两个独立轴（旧实现同一个布尔）
            Assert.NotEqual(ViewChromeForType().GetProperty("ShowTransport")!.Name,
                ViewChromeForType().GetProperty("ShowRightCluster")!.Name);
        }

        private static Type ViewChromeForType() => typeof(ViewChrome);

        [AvaloniaFact]
        public void Chrome_WiringExistsInWindow() {
            string xaml = Xaml();
            foreach (string name in new[] { "ScreenTitle", "TransportGroup", "TopRightCluster", "ViewSwitcherGroup" }) {
                Assert.Contains($"x:Name=\"{name}\"", xaml);
            }
            string code = WindowCode();
            Assert.Contains("private void SetChromeForView(ViewChrome chrome)", code);
            Assert.Contains("TransportGroup.IsVisible = chrome.ShowTransport;", code);
            Assert.Contains("TopRightCluster.IsVisible = chrome.ShowRightCluster;", code);
            Assert.Contains("ViewSwitcher.IsVisible = chrome.ShowViewSwitcher;", code);
            Assert.Contains("DetachViewButton.IsVisible = chrome.ShowDetachButton;", code);
        }

        // ── 6. 既有语义之一：Ctrl+M 开混音台 ───────────────────────────────

        [AvaloniaFact]
        public void CtrlM_OpensMixer() {
            // 键位映射（窗口与窗体共用的一张表）
            Assert.Equal(MainWindow.GlobalShortcut.ToggleMixer,
                MainWindow.MapGlobalShortcut(Key.M, KeyModifiers.Control, KeyModifiers.Control));
            Assert.Equal(MainWindow.GlobalShortcut.None,
                MainWindow.MapGlobalShortcut(Key.M, KeyModifiers.Shift, KeyModifiers.Control));
            Assert.Equal(MainWindow.GlobalShortcut.ToggleMixerAttachment,
                MainWindow.MapGlobalShortcut(Key.W, KeyModifiers.Control, KeyModifiers.Control));
            // 菜单里的 Ctrl+M 手势与处理器仍在（内部/外部两条入口）
            string xaml = Xaml();
            Assert.Contains("InputGesture=\"Ctrl+M\"", xaml);
            Assert.Contains("Click=\"OnMenuMixer\"", xaml);
            // Ctrl+M 的落点决策表
            Assert.Equal(MixerOpenAction.CreateEmbedded,
                ViewSwitcherPolicy.DecideMixerOpen(false, false, false, false));
            Assert.Equal(MixerOpenAction.CreateDetached,
                ViewSwitcherPolicy.DecideMixerOpen(false, false, true, false));
            Assert.Equal(MixerOpenAction.ActivateDetached,
                ViewSwitcherPolicy.DecideMixerOpen(true, true, false, false));
            Assert.Equal(MixerOpenAction.SwitchToMixerView,
                ViewSwitcherPolicy.DecideMixerOpen(true, false, false, false));
            Assert.Equal(MixerOpenAction.BackToWorkspace,
                ViewSwitcherPolicy.DecideMixerOpen(true, false, false, true));
            // 第二下 Ctrl+M 回工作台 = 胶囊状态互斥（同一条真值）
            var switcher = new ViewSwitcherState();
            switcher.SwitchTo(AppSurface.Mixer);
            Assert.True(switcher.ShowMixer);
            switcher.SwitchTo(AppSurface.Workspace);
            Assert.True(switcher.ShowWorkspace);
            Assert.Contains("case MixerOpenAction.BackToWorkspace:", WindowCode());
        }

        // ── 7. 既有语义之二：双击片段进卷帘（A5 进入方式不变）──────────────

        [AvaloniaFact]
        public void DoubleClickPart_EntersPianoRollView() {
            string xaml = Xaml();
            Assert.Contains("DoubleTapped=\"PartsCanvasDoubleTapped\"", xaml);
            // 双击 → 切到卷帘视图（不再靠 ShowPianoRoll 行展开）
            string code = WindowCode();
            Assert.Contains("public async void PartsCanvasDoubleTapped", code);
            Assert.Contains("SwitchToView(AppSurface.PianoRoll);", code);
            Assert.Contains("RequestView(view);", code);
            Assert.Contains("case AppSurface.PianoRoll:", code);
            // 未创建过卷帘时胶囊不抢跑（入口仍是双击）
            Assert.Contains("if (pianoRoll == null) {", code);
            Assert.True(ViewSwitcherPolicy.IsWorkView(AppSurface.PianoRoll));
        }

        // ── 8. 既有语义之三：返回路径走顶栏 ────────────────────────────────

        [AvaloniaFact]
        public void ReturnPath_GoesBackToWorkspace() {
            string xaml = Xaml();
            Assert.Contains("Click=\"OnViewTabClicked\"", xaml);
            string code = WindowCode();
            Assert.Contains("private void OnViewTabClicked", code);
            Assert.Contains("RequestView(view);", code);
            // 胶囊默认分支（含「工作台」标签）回工作台视图
            Assert.Contains("default:\n                    SwitchToView(AppSurface.Workspace);", code.Replace("\r\n", "\n"));
            // 分离当前视图后，视图区落回工作台（A5）；别的工作视图不受打扰
            Assert.Equal(AppSurface.Workspace, ViewSwitcherPolicy.ViewAfterDetach(AppSurface.PianoRoll, AppSurface.PianoRoll));
            Assert.Equal(AppSurface.Workspace, ViewSwitcherPolicy.ViewAfterDetach(AppSurface.Mixer, AppSurface.Mixer));
            Assert.Equal(AppSurface.Workspace, ViewSwitcherPolicy.ViewAfterDetach(AppSurface.Workspace, AppSurface.Workspace));
            Assert.Equal(AppSurface.Mixer, ViewSwitcherPolicy.ViewAfterDetach(AppSurface.PianoRoll, AppSurface.Mixer));
            Assert.Contains("SwitchAwayIfShowing(AppSurface.PianoRoll);", code);
            Assert.Contains("SwitchAwayIfShowing(AppSurface.Mixer);", code);
        }

        // ── 9. 视图级分离：按钮 + 状态持久化往返 ───────────────────────────

        [AvaloniaFact]
        public void DetachButton_IsViewScopedAndPersisted() {
            string xaml = Xaml();
            Assert.Contains("x:Name=\"DetachViewButton\"", xaml);
            Assert.Contains("{DynamicResource view.detach}", xaml);
            Assert.Contains("Click=\"OnDetachViewClicked\"", xaml);
            // 胶囊与分离按钮同属一个视图组（工作台视图里按钮由 chrome 隐藏）
            Assert.Contains("x:Name=\"ViewSwitcherGroup\"", xaml);

            // 分离状态读写往返：Preferences 里是同一份真值，分离窗口的落点由它决定
            bool oldMixer = Preferences.Default.DetachMixer;
            bool oldRoll = Preferences.Default.DetachPianoRoll;
            try {
                Preferences.Default.DetachPianoRoll = true;
                Assert.True(Preferences.Default.DetachPianoRoll);
                Preferences.Default.DetachPianoRoll = false;
                Assert.False(Preferences.Default.DetachPianoRoll);

                Preferences.Default.DetachMixer = true;
                Assert.Equal(MixerOpenAction.CreateDetached,
                    ViewSwitcherPolicy.DecideMixerOpen(false, false, Preferences.Default.DetachMixer, false));
                Preferences.Default.DetachMixer = false;
                Assert.Equal(MixerOpenAction.CreateEmbedded,
                    ViewSwitcherPolicy.DecideMixerOpen(false, false, Preferences.Default.DetachMixer, false));
            } finally {
                Preferences.Default.DetachMixer = oldMixer;
                Preferences.Default.DetachPianoRoll = oldRoll;
            }
        }

        [AvaloniaFact]
        public void DetachWiring_ReturnsControlToViewArea() {
            string code = WindowCode();
            // 用户关分离窗口 = 控件收回视图区（不复用旧 ForceClose→Shutdown 的生命周期）
            Assert.Contains("ReturnToHost = () => AttachMixerView();", code);
            Assert.Contains("ReturnToHost = () => AttachPianoRollView();", code);
            Assert.Contains("window.ReleaseControl();", code);
            Assert.Contains("MixerContainer.Content = mixerControl;", code);
            Assert.Contains("PianoRollContainer.Content = pianoRoll;", code);
            // VU 定时器 + 订阅的归宿只在退出期（挂载/分离/收回一律不碰控件生命周期）
            Assert.Contains("mixerControl?.Shutdown();", code);
            Assert.Contains("private void ShutdownDetachedViews()", code);
        }

        // ── 10. 新增字符串键 EN/zh 成对 ───────────────────────────────────

        [AvaloniaFact]
        public void NewStrings_ResolveInBothLanguages() {
            var en = new System.Collections.Generic.Dictionary<string, string>();
            // 会话语言是全局状态：跑完还原（优先还原偏好里的实际值，取不到才回落 en-US）
            string original = Preferences.Default.Language;
            try {
                OpenUtau.App.App.SetLanguage("en-US");
                foreach (string key in new[] { "view.pianoroll", "view.mixer", "view.detach" }) {
                    Assert.True(ThemeManager.TryGetString(key, out string value), $"EN 缺键：{key}");
                    Assert.NotEqual(key, value);
                    en[key] = value;
                }
                OpenUtau.App.App.SetLanguage("zh-CN");
                foreach (string key in new[] { "view.pianoroll", "view.mixer", "view.detach" }) {
                    Assert.True(ThemeManager.TryGetString(key, out string value), $"zh-CN 缺键：{key}");
                    Assert.NotEqual(en[key], value);   // 与英文不同 ⇒ zh 字典确有该键
                }
            } finally {
                OpenUtau.App.App.SetLanguage(string.IsNullOrWhiteSpace(original) ? "en-US" : original);
            }
        }
    }
}

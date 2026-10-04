using System;
using ReactiveUI;

namespace OpenUtau.App.ViewModels {
    /// <summary>
    /// 单窗口内的「面」（决策 A1 / §11-S5）：欢迎页与偏好设置仍是覆盖层（overlay），
    /// 工作台 / 钢琴卷帘 / 混音台是同一个工作区里**互斥显示**的三个视图（顶栏胶囊切换）。
    /// </summary>
    public enum AppSurface {
        Welcome,
        Workspace,
        PianoRoll,
        Mixer,
        Preferences,
    }

    /// <summary>
    /// 顶栏 chrome 契约（<c>MainWindow.SetChromeForView</c> 的唯一入参）。
    /// 五个轴彼此独立：视图名键 / 运输组 / 右侧图标组 / 视图胶囊 / 分离按钮。
    /// （旧实现用**同一个布尔**同时开关运输组与右侧组，S5 拆成两轴，今后可单独调。）
    /// </summary>
    public readonly record struct ViewChrome(
        AppSurface Surface,
        string TitleKey,
        bool ShowTransport,
        bool ShowRightCluster,
        bool ShowViewSwitcher,
        bool ShowDetachButton);

    /// <summary>「开混音台」的落点（Ctrl+M / 工具菜单 / 顶栏按钮 / 胶囊共用一张决策表）。</summary>
    public enum MixerOpenAction {
        /// <summary>还没有混音台控件，且当前偏好是内嵌 → 建控件并切到混音台视图。</summary>
        CreateEmbedded,
        /// <summary>还没有混音台控件，且当前偏好是分离 → 建控件并弹独立窗口。</summary>
        CreateDetached,
        /// <summary>分离窗口已存在 → 置前（不新建、不 reparent）。</summary>
        ActivateDetached,
        /// <summary>内嵌且当前不在混音台视图 → 切到混音台视图（Ctrl+M 的「开」）。</summary>
        SwitchToMixerView,
        /// <summary>内嵌且当前就在混音台视图 → 回工作台（Ctrl+M 的「关」）。</summary>
        BackToWorkspace,
    }

    /// <summary>
    /// S5 视图化的纯策略层：不碰 UI、不碰窗口，只做「视图名键 / chrome / 落点」的判定，
    /// 便于契约测试直接断言（headless 下 MainWindow 起不来）。
    /// </summary>
    public static class ViewSwitcherPolicy {
        /// <summary>胶囊覆盖的三个工作视图（欢迎页 / 偏好设置不在胶囊内，保持 overlay 行为）。</summary>
        public static bool IsWorkView(AppSurface surface) =>
            surface is AppSurface.Workspace or AppSurface.PianoRoll or AppSurface.Mixer;

        /// <summary>三个工作视图的默认落点。</summary>
        public const AppSurface BaseView = AppSurface.Workspace;

        /// <summary>
        /// 顶栏按视图切内容（三个工作视图共用同一条 56px 顶栏，与设计稿一致）：
        /// 欢迎页 / 偏好页只留品牌 + 屏名；工作台 / 卷帘 / 混音台显示运输组 + 右侧组 + 胶囊；
        /// 「分离」是**视图级**动作，故只在卷帘 / 混音台出现（A5：稿里三钮收敛成一个分离按钮）。
        /// </summary>
        public static ViewChrome ChromeFor(AppSurface surface) => surface switch {
            AppSurface.Welcome => new ViewChrome(surface, "view.welcome", false, false, false, false),
            AppSurface.Preferences => new ViewChrome(surface, "prefs.caption", false, false, false, false),
            AppSurface.PianoRoll => new ViewChrome(surface, "view.pianoroll", true, true, true, true),
            AppSurface.Mixer => new ViewChrome(surface, "view.mixer", true, true, true, true),
            _ => new ViewChrome(AppSurface.Workspace, "view.workspace", true, true, true, false),
        };

        /// <summary>
        /// 分离某个视图后视图区该显示什么：被分离的视图正显示着 → 落回工作台（A5：主窗口仍要有视图）；
        /// 否则不动（在别的工作视图里从菜单分离卷帘，不该被拽回工作台）。
        /// </summary>
        public static AppSurface ViewAfterDetach(AppSurface detachedView, AppSurface currentView) =>
            currentView == detachedView && detachedView != AppSurface.Workspace
                ? AppSurface.Workspace
                : currentView;

        /// <summary>
        /// 「开混音台」决策表；<paramref name="mixerViewActive"/> = 内嵌且当前视图正是混音台。
        /// </summary>
        public static MixerOpenAction DecideMixerOpen(
            bool hasControl, bool hasDetachedWindow, bool detachPreferred, bool mixerViewActive) {
            if (!hasControl) {
                return detachPreferred ? MixerOpenAction.CreateDetached : MixerOpenAction.CreateEmbedded;
            }
            if (hasDetachedWindow) {
                return MixerOpenAction.ActivateDetached;
            }
            return mixerViewActive ? MixerOpenAction.BackToWorkspace : MixerOpenAction.SwitchToMixerView;
        }
    }

    /// <summary>
    /// 工作区视图切换状态（<see cref="MainWindowViewModel.ViewSwitcher"/>）：
    /// 单一真值 <see cref="CurrentView"/>，三个 <c>Show*</c> 是它的派生可见性，
    /// 供 XAML 直接绑定（切换只翻 <c>IsVisible</c>；工作台三列 / 卷帘与混音台整行宿主彼此互斥，
    /// 列宽行高都不动 ⇒ 不参与布局、无抖动）。
    /// </summary>
    public sealed class ViewSwitcherState : ViewModelBase {
        private AppSurface currentView = ViewSwitcherPolicy.BaseView;

        /// <summary>当前工作区视图（只取三个工作视图；欢迎页 / 偏好设置走 overlay）。</summary>
        public AppSurface CurrentView {
            get => currentView;
            private set {
                if (currentView == value) {
                    return;
                }
                currentView = value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(ShowWorkspace));
                this.RaisePropertyChanged(nameof(ShowPianoRoll));
                this.RaisePropertyChanged(nameof(ShowMixer));
                this.RaisePropertyChanged(nameof(Chrome));
            }
        }

        public bool ShowWorkspace => currentView == AppSurface.Workspace;
        public bool ShowPianoRoll => currentView == AppSurface.PianoRoll;
        public bool ShowMixer => currentView == AppSurface.Mixer;

        /// <summary>当前视图的顶栏 chrome（见 <see cref="ViewSwitcherPolicy.ChromeFor"/>）。</summary>
        public ViewChrome Chrome => ViewSwitcherPolicy.ChromeFor(currentView);

        /// <summary>切到某个工作视图；传入 overlay 面直接抛（调用点 bug，早失败）。</summary>
        public void SwitchTo(AppSurface view) {
            if (!ViewSwitcherPolicy.IsWorkView(view)) {
                throw new ArgumentOutOfRangeException(nameof(view), view, "视图胶囊只覆盖工作台 / 钢琴卷帘 / 混音台");
            }
            CurrentView = view;
        }

        /// <summary>回工作台（卷帘的「隐藏卷帘」菜单项、分离后的落点都走这里）。</summary>
        public void SwitchToWorkspace() => SwitchTo(AppSurface.Workspace);
    }
}

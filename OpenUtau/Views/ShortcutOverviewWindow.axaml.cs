using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core.Util;

namespace OpenUtau.App.Views {
    /// <summary>
    /// 快捷键总览（只读）。每轨/每窗口至多一个实例；数据源 = <c>CommandRegistry</c>。
    /// </summary>
    public partial class ShortcutOverviewWindow : WindowEx {
        ShortcutOverviewViewModel? viewModel;

        public ShortcutOverviewWindow() {
            InitializeComponent();
            // 主修饰键按平台解析（macOS = Meta），总览显示的手势与用户实际按的一致
            var cmdKey = OS.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
            DataContext = viewModel = new ShortcutOverviewViewModel(cmdKey: cmdKey);
            SyncLabels();
            SearchBox.TextChanged += (_, _) => {
                if (viewModel != null) {
                    viewModel.Search = SearchBox.Text ?? string.Empty;
                    SyncLabels();
                }
            };
        }

        /// <summary>打开总览（已开着就抬到最前）。</summary>
        public static void Open(Window? owner) {
            // 已开着就抬到最前（避免连点开出一堆）
            var desktop = Avalonia.Application.Current?.ApplicationLifetime as
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var existing = desktop?.Windows.OfType<ShortcutOverviewWindow>().FirstOrDefault();
            if (existing != null) {
                existing.Activate();
                return;
            }
            var window = new ShortcutOverviewWindow();
            if (owner != null) {
                window.Show(owner);
            } else {
                window.Show();
            }
        }

        void SyncLabels() {
            if (viewModel == null) {
                return;
            }
            GroupsList.ItemsSource = viewModel.Groups;
            ConflictLabel.Text = viewModel.ConflictSummary;
            EmptyLabel.IsVisible = viewModel.IsEmpty;
        }
    }
}

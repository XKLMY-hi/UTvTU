using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using OpenUtau.Core.Util;
using OpenUtau.Core.Vst;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace OpenUtau.App.ViewModels {
    public class VstEditorViewModel : ViewModelBase {
        private readonly VstEffect? _fx;

        public string DisplayName { get; }
        public string TypeLabel { get; }
        public Avalonia.Media.IBrush TypeBadgeColor { get; }
        public string Vendor { get; }
        public string SubCategories { get; }
        public string PathDisplay { get; }
        public string ParamInfo { get; }
        [Reactive] public string StatusText { get; set; }
        public ICommand OpenEditorCommand { get; }

        public VstEditorViewModel() {
            DisplayName = ""; StatusText = ThemeManager.GetString("vsteditor.noplugin");
            TypeLabel = ""; Vendor = ""; SubCategories = ""; PathDisplay = ""; ParamInfo = "";
            TypeBadgeColor = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(128, 128, 128));
            OpenEditorCommand = ReactiveCommand.Create(() => { });
        }

        public VstEditorViewModel(VstEffect fx) {
            _fx = fx;
            var entry = fx.Entry;
            DisplayName = entry.Name;
            TypeLabel = entry.Type == VstPluginType.VST3
                ? (entry.IsEffect ? "VST3 Effect" : "VST3 Instrument")
                : (entry.IsEffect ? "VST2 Effect" : "VST2 Instrument");
            TypeBadgeColor = entry.IsEffect
                ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(60, 160, 100))
                : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(220, 120, 40));
            Vendor = !string.IsNullOrEmpty(entry.Vendor) ? entry.Vendor : ThemeManager.GetString("vsteditor.novendor");
            SubCategories = entry.SubCategories.Count > 0
                ? string.Join(" · ", entry.SubCategories) : ThemeManager.GetString("vsteditor.nocategory");
            string path = entry.Path;
            if (path.Length > 60) path = "..." + path[^57..];
            PathDisplay = path;
            ParamInfo = $"{entry.CategoryDisplay}  ·  {entry.Vendor}";
            StatusText = "";
            OpenEditorCommand = ReactiveCommand.Create(OpenEditor);
        }

        async void OpenEditor() {
            if (_fx == null) return;
            try {
                // attached 在 VST 专用线程执行（秒级初始化不卡 UI），await 后回到 UI 线程更新状态
                StatusText = ThemeManager.GetString("vsteditor.loading");
                var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                bool ok = await _fx.OpenNativeEditorAsync(owner?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
                StatusText = ok ? ThemeManager.GetString("vsteditor.nativeopened")
                    : $"{ThemeManager.GetString("vsteditor.noeditor")}: {_fx.EditorError ?? ThemeManager.GetString("vsteditor.nogui")}";
            } catch (Exception ex) {
                StatusText = $"{ThemeManager.GetString("vsteditor.error")}: {ex.Message}";
                Log.Error(ex, $"[VstEditor] Failed for {_fx.DisplayName}");
            }
        }
    }
}

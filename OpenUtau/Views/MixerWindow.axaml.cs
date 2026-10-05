using System;
using OpenUtau.App.Controls;
using OpenUtau.Core.Util;

namespace OpenUtau.App.Views;

/// <summary>
/// 混音台分离窗口（S5/A2）：**纯宿主** —— 控件由 MainWindow 持有并 reparent，
/// 本窗口不负责它的生死（不再调 <c>Shutdown()</c>，否则 VU 定时器与订阅会在
/// 「内嵌 → 分离 → 内嵌」的首次往复后永久停摆）。
/// 用户关窗 = 控件收回视图区（<see cref="ReturnToHost"/>）；宿主回收/退出走 <see cref="ReleaseControl"/>。
/// </summary>
public partial class MixerWindow : WindowEx
{
    private MixerControl? _mixerControl;
    private bool _released;
    private bool _detached;
    private bool _closed;

    /// <summary>用户关闭分离窗口时的回调：宿主把控件放回视图区（控件继续存活）。</summary>
    public Action? ReturnToHost { get; set; }

    public MixerWindow() { InitializeComponent(); }

    public MixerWindow(MixerControl mixerControl) : this()
    {
        _mixerControl = mixerControl;
        MixerContainer.Content = mixerControl;

        // 恢复窗口位置与尺寸（旧实现只存不取，尺寸永远回到 XAML 默认值）
        if (Preferences.Default.MixerWindowSize.TryGetPosition(out int x, out int y)) {
            Position = new Avalonia.PixelPoint(x, y);
        }
        if (Preferences.Default.MixerWindowSize.Width > 0) {
            Width = Preferences.Default.MixerWindowSize.Width;
        }
        if (Preferences.Default.MixerWindowSize.Height > 0) {
            Height = Preferences.Default.MixerWindowSize.Height;
        }
        WindowState = (Avalonia.Controls.WindowState)Preferences.Default.MixerWindowSize.State;
    }

    /// <summary>
    /// 把控件交回宿主：摘掉 Content（走 <see cref="MainWindow.DetachAndFlush"/>，含旧树布局冲洗，
    /// 见其注释里的 crash 根因）→ 关窗；不触发收回归位、不动控件生命周期。
    /// 幂等（窗口已关时只清 Content），宿主回收与用户关窗两条路都安全。
    /// </summary>
    public void ReleaseControl()
    {
        _released = true;
        DetachControl();
        _mixerControl = null;
        if (!_closed) {
            Close();
        }
    }

    /// <summary>摘控件 + 冲洗本窗口挂起布局（幂等；窗口已关时冲洗自然失效，只清 Content）。</summary>
    private void DetachControl() {
        if (_detached) {
            return;
        }
        _detached = true;
        MainWindow.DetachAndFlush(MixerContainer);
    }

    /// <summary>
    /// 旧 <c>ForceClose()</c>（关窗即 <c>Shutdown()</c> 控件）已删除：控件生命周期不再属于窗口，
    /// 宿主回收用 <see cref="ReleaseControl"/>，真正销毁在 MainWindow 退出时做。
    /// </summary>
    protected override void OnClosing(Avalonia.Controls.WindowClosingEventArgs e)
    {
        Preferences.Default.MixerWindowSize.Set(Width, Height, Position.X, Position.Y, (int)WindowState);
        Preferences.Save();
        // 用户关窗：趁窗口还活着把控件摘掉并冲洗布局（窗口销毁后再摘就冲洗不到了）
        DetachControl();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _closed = true;
        _mixerControl = null;   // 控件生命周期归 MainWindow（退出时由它 Shutdown）
        if (_released) {
            return;
        }
        // 用户关窗 → 收回视图区（VU 定时器与订阅不中断）
        var returnToHost = ReturnToHost;
        ReturnToHost = null;
        _released = true;
        returnToHost?.Invoke();
    }
}

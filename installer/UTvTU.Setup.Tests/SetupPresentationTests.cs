using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.Core.Theming;
using OpenUtau.Theming;
using UTvTU.DawUI.Controls;
using UTvTU.Installer;
using UTvTU.Setup;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(SetupTestApp))]
public static class SetupTestApp {
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

[Collection("Setup")]
public class SetupPresentationTests {
    [AvaloniaTheory]
    [InlineData(false, false, 820, 600)] [InlineData(false, true, 940, 680)]
    [InlineData(true, false, 820, 600)] [InlineData(true, true, 940, 680)]
    public void BothWizardsUseSharedControlsWithSafeDefaults(bool uninstall, bool dark, int width, int height) {
        var previous = InstallerCore.Command;
        InstallerCore.Command = new SetupCommandLine { Uninstall = uninstall, Preview = true, Directory = Path.Combine(Path.GetTempPath(), "UTvTU-GUI-Preview") };
        Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        ColorPool.Initialize(0xFF0B57D0, Md3SchemeVariant.TonalSpot, dark);
        var window = new MainWindow { Width = width, Height = height };
        try {
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var controls = window.GetVisualDescendants().OfType<Control>().ToArray();
            var action = controls.OfType<DawButton>().Single(button => button.Name == "ActionButton");
            Assert.False(action.IsEnabled); Assert.True(action.Bounds.Width >= 112); Assert.True(action.Bounds.Height >= 30);
            var position = action.TranslatePoint(default, window)!.Value;
            Assert.InRange(position.X, 0, window.Bounds.Width - action.Bounds.Width);
            Assert.InRange(position.Y, 0, window.Bounds.Height - action.Bounds.Height);
            var remove = controls.OfType<CheckBox>().Single(control => control.Name == "RemoveDataChoice");
            Assert.False(remove.IsChecked); Assert.Equal(uninstall, controls.Single(control => control.Name == "UninstallChoices").IsVisible);
            Assert.Equal(!uninstall, controls.Single(control => control.Name == "InstallChoices").IsVisible);
            Assert.DoesNotContain(controls.OfType<TextBlock>(), text => text.Text?.Contains("UTvTU.DawUI.Controls.") == true);
            Assert.False(Directory.Exists(InstallerCore.Command.Directory));
        } finally { window.Close(); InstallerCore.Command = previous; }
    }
    [AvaloniaFact]
    public void PreviewCannotExecuteAnyFileOperation() {
        var previous = InstallerCore.Command;
        InstallerCore.Command = new SetupCommandLine { Preview = true };
        try {
            Assert.ThrowsAsync<InvalidOperationException>(() => InstallerCore.RunAsync(new SetupOptions(), new InstallerCore.DirectProgress(_ => { }), CancellationToken.None)).GetAwaiter().GetResult();
        } finally { InstallerCore.Command = previous; }
    }
    [AvaloniaFact]
    public async Task FailedOperationReturnsToOptionsWithRetryInsteadOfLaunch() {
        var previous = InstallerCore.Command;
        InstallerCore.Command = new SetupCommandLine { Directory = Path.Combine(Path.GetTempPath(), "UTvTU-GUI-Preview") };
        var window = new MainWindow { ExecuteOperation = (_, _, _) => Task.FromException<SetupResult>(new IOException("locked library")) };
        try {
            window.Show(); await window.ExecuteAsync(); Dispatcher.UIThread.RunJobs();
            var controls = window.GetVisualDescendants().OfType<Control>().ToArray();
            Assert.True(controls.Single(control => control.Name == "OptionsPage").IsVisible);
            Assert.False(controls.Single(control => control.Name == "ResultPage").IsVisible);
            Assert.Equal("重试", controls.OfType<DawButton>().Single(button => button.Name == "ActionButton").Content);
            Assert.Contains("locked library", controls.OfType<TextBlock>().Single(text => text.Name == "NoticeText").Text);
        } finally { window.Close(); InstallerCore.Command = previous; }
    }
    [AvaloniaFact]
    public async Task ClosingDuringOperationRequestsCancellationAndWaitsForRecovery() {
        var previous = InstallerCore.Command;
        InstallerCore.Command = new SetupCommandLine { Directory = Path.Combine(Path.GetTempPath(), "UTvTU-GUI-Preview") };
        bool restored = false;
        var window = new MainWindow { ExecuteOperation = async (_, _, token) => { try { await Task.Delay(Timeout.Infinite, token); } finally { restored = true; } return SetupResult.Completed("unused"); } };
        try {
            window.Show(); var running = window.ExecuteAsync(); window.Close();
            await running; Dispatcher.UIThread.RunJobs();
            Assert.True(restored); Assert.False(window.IsVisible);
        } finally { window.Close(); InstallerCore.Command = previous; }
    }
}

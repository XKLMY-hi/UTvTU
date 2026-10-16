using UTvTU.Installer;
using UTvTU.Setup;
using Xunit;

[Collection("Setup")]
public class SetupShellTests {
    [Fact] public async Task ShortcutRemovalUsesExactExecutableInsteadOfDirectoryPrefix() {
        string sandbox = Path.Combine(Path.GetTempPath(), "UTvTU-Setup-Shell-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => {
            try {
                string app = Path.Combine(sandbox, "App", "OpenUtau.exe"), neighbor = Path.Combine(sandbox, "App-other", "OpenUtau.exe");
                string link = Path.Combine(sandbox, "UTvTU.lnk");
                WindowsIntegration.Shortcut(link, neighbor); WindowsIntegration.RemoveShortcut(link, app); Assert.True(File.Exists(link));
                WindowsIntegration.Shortcut(link, app); WindowsIntegration.RemoveShortcut(link, app); Assert.False(File.Exists(link));
                done.SetResult();
            } catch (Exception error) { done.SetException(error); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        try { await done.Task; }
        finally {
            if (!SetupPaths.Within(Path.Combine(Path.GetTempPath(), "UTvTU-Setup-Shell-Tests"), sandbox)) throw new Exception("Invalid test cleanup.");
            Directory.Delete(sandbox, true);
        }
    }
}

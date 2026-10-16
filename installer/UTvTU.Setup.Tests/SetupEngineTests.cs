using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using UTvTU.Setup;
using Xunit;

[CollectionDefinition("Setup", DisableParallelization = true)] public class SetupCollection { }
[Collection("Setup")]
public sealed class SetupEngineTests : IDisposable {
    readonly string sandbox = Path.Combine(Path.GetTempPath(), "UTvTU-Setup-Tests", Guid.NewGuid().ToString("N"));
    string Target => Path.Combine(sandbox, "Application");
    SetupOptions Options => new() { Directory = Target, UserSid = "test-user", DataDirectory = Path.Combine(sandbox, "Documents", "OpenUtau Plus") };
    readonly SetupEngine engine = new();
    public SetupEngineTests() => Directory.CreateDirectory(sandbox);
    public void Dispose() {
        if (!SetupPaths.Within(Path.Combine(Path.GetTempPath(), "UTvTU-Setup-Tests"), sandbox)) throw new Exception("Invalid fixture cleanup.");
        Directory.Delete(sandbox, true);
    }
    internal static MemoryStream Package(string version = "UTvTU-26.10.16-000000", params (string Path, string Content)[] extra) {
        var files = new[] { ("OpenUtau.exe", version), ("UTvTU-Uninstall.exe", "uninstaller") }.Concat(extra).ToArray();
        var manifest = new PackageManifest { Version = version, SourceCommit = new string('a', 40) };
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true)) {
            foreach (var (path, text) in files) {
                var bytes = Encoding.UTF8.GetBytes(text);
                manifest.Files.Add(new(path, bytes.Length, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))));
                using var entry = zip.CreateEntry(path).Open(); entry.Write(bytes);
            }
            using var writer = new StreamWriter(zip.CreateEntry(PackageManifest.FileName).Open());
            writer.Write(JsonSerializer.Serialize(manifest, SetupJson.Options));
        }
        stream.Position = 0; return stream;
    }
    void Install(string version = "UTvTU-26.10.16-000000") => engine.Install(Package(version), Options, null, CancellationToken.None);
    [Fact] public void FreshInstallHasMarkerVerifiedReceiptAndCorrectDataDirectory() {
        Install(); var receipt = SetupEngine.ReadReceipt(Target)!;
        Assert.True(File.Exists(Path.Combine(Target, "installed.txt"))); Assert.False(receipt.PortableData);
        Assert.Equal(Options.DataDirectory, receipt.DataDirectory);
        Assert.All(receipt.Files, file => Assert.True(SetupPackage.Matches(SetupPaths.Resolve(Target, file.Path), file)));
    }
    [Fact] public void UninstallPreservesUnknownProjectsBackupsSingersAndPreferences() {
        Install(); var names = new[] { "Backups/song.ustxp", "Singers/demo/character.txt", "prefs.json", "song.ustxp", "unknown.txt" };
        foreach (string name in names) { string path = Path.Combine(Target, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "user"); }
        var result = engine.Uninstall(Options, null, CancellationToken.None);
        Assert.True(result.Success); Assert.All(names, name => Assert.Equal("user", File.ReadAllText(Path.Combine(Target, name))));
        Assert.False(File.Exists(Path.Combine(Target, "OpenUtau.exe")));
    }
    [Fact] public void UninstallWithoutReceiptRefusesAnyDirectory() {
        Directory.CreateDirectory(Target); File.WriteAllText(Path.Combine(Target, "song.ustxp"), "user");
        Assert.Throws<IOException>(() => engine.Uninstall(Options, null, CancellationToken.None));
        Assert.True(File.Exists(Path.Combine(Target, "song.ustxp")));
    }
    [Theory] [InlineData("../Application-other/leak.txt")] [InlineData("a/../../leak")] [InlineData("/outside")] [InlineData("C:/outside")] [InlineData("a\\..\\outside")] [InlineData("a/file:stream")] [InlineData("NUL.txt")] [InlineData("a/../b")]
    public void MalformedPathsAreRejectedBeforeTargetChanges(string path) {
        Assert.ThrowsAny<Exception>(() => engine.Install(Package("test", (path, "escape")), Options, null, CancellationToken.None));
        Assert.False(Directory.Exists(Target));
    }
    [Theory] [InlineData("prefs.json")] [InlineData("Singers/test/character.txt")] [InlineData("Backups/song.ustxp")]
    public void UserDataCannotBeShippedAsProgramFiles(string path) => Assert.Throws<InvalidDataException>(() => new SetupPackage(Package("test", (path, "bad"))));
    [Fact] public void LockedLibraryPreventsAnyReplacement() {
        engine.Install(Package("old", ("library.dll", "old")), Options, null, CancellationToken.None);
        using var file = new FileStream(Path.Combine(Target, "library.dll"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Throws<IOException>(() => engine.Install(Package("new", ("library.dll", "new")), Options, null, CancellationToken.None));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Target, "OpenUtau.exe")));
    }
    [Fact] public void FailureDuringCommitRestoresOriginalFilesAndReceipt() {
        Install("old"); string oldReceipt = File.ReadAllText(Path.Combine(Target, InstallReceipt.FileName));
        Assert.Throws<IOException>(() => engine.Install(Package("new"), Options, new Callback(p => { if (p.Percent >= 70) throw new IOException("injected I/O failure"); }), CancellationToken.None));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Target, "OpenUtau.exe")));
        Assert.Equal(oldReceipt, File.ReadAllText(Path.Combine(Target, InstallReceipt.FileName)));
    }
    [Fact] public void CancellationDuringCommitRestoresOriginalVersion() {
        Install("old"); using var stop = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => engine.Install(Package("new"), Options, new Callback(p => { if (p.Percent >= 70) stop.Cancel(); }), stop.Token));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Target, "OpenUtau.exe")));
        Assert.Equal("old", SetupEngine.ReadReceipt(Target)!.Version);
    }
    [Fact] public void ModifiedOwnedFilesArePreservedAndReported() {
        Install(); File.WriteAllText(Path.Combine(Target, "OpenUtau.exe"), "user modification");
        var result = engine.Uninstall(Options, null, CancellationToken.None);
        Assert.Single(result.Warnings); Assert.Equal("user modification", File.ReadAllText(Path.Combine(Target, "OpenUtau.exe")));
    }
    [Fact] public void PortableUpgradeKeepsExistingDataMode() {
        Directory.CreateDirectory(Target); File.WriteAllText(Path.Combine(Target, "OpenUtau.exe"), "old app"); File.WriteAllText(Path.Combine(Target, "prefs.json"), "prefs");
        new SetupEngine { IsLegacyApplication = _ => true }.Install(Package(), Options, null, CancellationToken.None);
        Assert.True(SetupEngine.ReadReceipt(Target)!.PortableData); Assert.False(File.Exists(Path.Combine(Target, "installed.txt")));
        Assert.Equal("prefs", File.ReadAllText(Path.Combine(Target, "prefs.json")));
    }
    [Fact] public void UpstreamAndUnknownFoldersAreNotAutomaticallyOverwritten() {
        Directory.CreateDirectory(Target); File.WriteAllText(Path.Combine(Target, "OpenUtau.exe"), "upstream");
        Assert.Throws<IOException>(() => engine.Install(Package(), Options, null, CancellationToken.None));
        Assert.Equal("upstream", File.ReadAllText(Path.Combine(Target, "OpenUtau.exe")));
    }
    [Fact] public void ObsoleteOwnedFilesAreRemovedButUnknownFilesSurviveUpgrade() {
        engine.Install(Package("old", ("obsolete.dll", "old")), Options, null, CancellationToken.None);
        File.WriteAllText(Path.Combine(Target, "unknown.dll"), "user"); Install("new");
        Assert.False(File.Exists(Path.Combine(Target, "obsolete.dll"))); Assert.True(File.Exists(Path.Combine(Target, "unknown.dll")));
    }
    [Fact] public void AnotherUsersReceiptCannotBeUninstalled() {
        Install(); Assert.Throws<IOException>(() => engine.Uninstall(Options with { UserSid = "other" }, null, CancellationToken.None));
        Assert.True(File.Exists(Path.Combine(Target, "OpenUtau.exe")));
    }
    [Fact] public void DataRemovalIsExplicitAndLimitedToRecordedDataRoot() {
        Install(); string data = Options.DataDirectory; Directory.CreateDirectory(data); File.WriteAllText(Path.Combine(data, "prefs.json"), "prefs");
        string outside = Path.Combine(sandbox, "outside.ustxp"); File.WriteAllText(outside, "project");
        engine.Uninstall(Options with { RemoveData = true }, null, CancellationToken.None);
        Assert.False(File.Exists(Path.Combine(data, "prefs.json"))); Assert.True(File.Exists(outside));
    }
    [Fact] public void FileWorkerLeavesProfileCleanupToOriginalUserContext() {
        Install(); var receipt = SetupEngine.ReadReceipt(Target)!; string data = Options.DataDirectory;
        Directory.CreateDirectory(data); File.WriteAllText(Path.Combine(data, "prefs.json"), "user");
        UTvTU.Installer.InstallerCore.RunFiles(Options with { Uninstall = true, RemoveData = true }, null, CancellationToken.None);
        Assert.True(File.Exists(Path.Combine(data, "prefs.json")));
        SetupEngine.DeleteUserData(receipt, Options with { RemoveData = true });
        Assert.False(File.Exists(Path.Combine(data, "prefs.json")));
    }
    [Fact] public void CorruptPayloadCannotReplaceExistingInstallation() {
        Install("old"); using var payload = Package("new");
        using (var zip = new ZipArchive(payload, ZipArchiveMode.Update, true)) { zip.GetEntry("OpenUtau.exe")!.Delete(); using var writer = new StreamWriter(zip.CreateEntry("OpenUtau.exe").Open()); writer.Write("BAD"); }
        payload.Position = 0;
        Assert.Throws<InvalidDataException>(() => engine.Install(payload, Options, null, CancellationToken.None));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Target, "OpenUtau.exe")));
    }
    [Fact] public void UninstallCancellationRestoresManagedFilesAndReceipt() {
        Install(); using var stop = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => engine.Uninstall(Options, new Callback(p => stop.Cancel()), stop.Token));
        Assert.NotNull(SetupEngine.ReadReceipt(Target)); Assert.True(File.Exists(Path.Combine(Target, "OpenUtau.exe")));
    }
    [Fact] public void ForgedReceiptCannotTurnUserProjectIntoAnOwnedFile() {
        Install(); string project = Path.Combine(Target, "song.ustxp"); File.WriteAllText(project, "user");
        var receipt = SetupEngine.ReadReceipt(Target)!; receipt.Files.Add(SetupPackage.Describe(Target, project));
        SetupJson.Write(Path.Combine(Target, InstallReceipt.FileName), receipt);
        Assert.Throws<InvalidDataException>(() => engine.Uninstall(Options, null, CancellationToken.None));
        Assert.Equal("user", File.ReadAllText(project)); Assert.True(File.Exists(Path.Combine(Target, "OpenUtau.exe")));
    }
    [Fact] public void PrefixSimilarDirectoriesDoNotPassContainmentCheck() {
        Assert.False(SetupPaths.Within(Target, Target + "-other/file"));
        Assert.True(SetupPaths.Within(Target, Path.Combine(Target, "library.dll")));
    }
    [Fact] public void BrokenDirectoryJunctionCannotRedirectInstallation() {
        string outside = Path.Combine(sandbox, "missing-destination");
        Directory.CreateDirectory(Target);
        try {
            byte[] substitute = Encoding.Unicode.GetBytes(@"\??\" + outside), display = Encoding.Unicode.GetBytes(outside);
            byte[] data = new byte[16 + substitute.Length + 2 + display.Length + 2];
            BitConverter.GetBytes(0xA0000003u).CopyTo(data, 0); BitConverter.GetBytes((ushort)(data.Length - 8)).CopyTo(data, 4);
            BitConverter.GetBytes((ushort)substitute.Length).CopyTo(data, 10); BitConverter.GetBytes((ushort)(substitute.Length + 2)).CopyTo(data, 12); BitConverter.GetBytes((ushort)display.Length).CopyTo(data, 14);
            substitute.CopyTo(data, 16); display.CopyTo(data, 16 + substitute.Length + 2);
            using (var handle = CreateFileW(Target, 0x40000000, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero)) {
                if (handle.IsInvalid || !DeviceIoControl(handle, 0x900A4, data, data.Length, IntPtr.Zero, 0, out _, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            Assert.True((File.GetAttributes(Target) & FileAttributes.ReparsePoint) != 0);
            Assert.False(Directory.Exists(outside));
            Assert.Throws<IOException>(() => engine.Install(Package(), Options, null, CancellationToken.None));
            Assert.False(Directory.Exists(outside));
        } finally { Directory.Delete(Target); }
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint mode, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool DeviceIoControl(SafeFileHandle file, uint code, byte[] input, int inputSize, IntPtr output, int outputSize, out int returned, IntPtr overlapped);
    sealed class Callback(Action<SetupProgress> action) : IProgress<SetupProgress> { public void Report(SetupProgress value) => action(value); }
}

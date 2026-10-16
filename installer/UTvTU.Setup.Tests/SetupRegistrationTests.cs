using UTvTU.Setup;
using Xunit;

public class SetupRegistrationTests {
    sealed class Store : IRegistrationStore {
        public readonly Dictionary<(string, string), object> Values = new();
        public int FailAt = -1, Writes;
        public object? Read(string key, string name) => Values.GetValueOrDefault((key, name));
        public void Write(string key, string name, object value) { if (++Writes == FailAt) throw new IOException("registry failure"); Values[(key, name)] = value; }
        public void DeleteValue(string key, string name) => Values.Remove((key, name));
        public void DeleteTree(string key) { foreach (var value in Values.Keys.Where(item => item.Item1 == key || item.Item1.StartsWith(key + "\\")).ToArray()) Values.Remove(value); }
    }
    static InstallReceipt Receipt(string name = "UTvTU") => new() { Directory = Path.Combine(Path.GetTempPath(), name), UserSid = "user", Version = "UTvTU-test" };
    [Fact] public void RegistersBothExtensionsAndPreservesOtherDefaultApplications() {
        var store = new Store(); store.Write(@"Software\Classes\.ustx", "", "OtherDAW"); new SetupRegistration(store).Install(Receipt(), true);
        Assert.Equal("OtherDAW", store.Read(@"Software\Classes\.ustx", ""));
        Assert.Equal(SetupRegistration.ProgId, store.Read(@"Software\Classes\.ustxp", ""));
        Assert.NotNull(store.Read(@"Software\Classes\.ustx\OpenWithProgids", SetupRegistration.ProgId));
    }
    [Fact] public void OldUninstallerCannotRemoveNewInstallationsRegistration() {
        var store = new Store(); var registration = new SetupRegistration(store); registration.Install(Receipt("new-install"), true);
        registration.Uninstall(Receipt("old-install").Directory, "user");
        Assert.Equal(Receipt("new-install").Directory, store.Read(SetupRegistration.UninstallKey, "InstallLocation"));
    }
    [Fact] public void FailedRegistrationRestoresPreviousValues() {
        var store = new Store(); var registration = new SetupRegistration(store); registration.Install(Receipt("old-install"), true);
        var original = store.Values.ToDictionary(item => item.Key, item => item.Value); store.FailAt = store.Writes + 5;
        Assert.Throws<IOException>(() => registration.Install(Receipt("new-install"), true));
        Assert.Equal(original.Count, store.Values.Count); Assert.All(original, item => Assert.Equal(item.Value, store.Read(item.Key.Item1, item.Key.Item2)));
    }
    [Fact] public void QuietUninstallCommandUsesSupportedFlagAndQuotedPaths() {
        var store = new Store(); new SetupRegistration(store).Install(Receipt("with spaces 中文"), false);
        string command = (string)store.Read(SetupRegistration.UninstallKey, "QuietUninstallString")!;
        Assert.Contains("--silent", command); Assert.DoesNotContain("--quiet", command); Assert.StartsWith("\"", command);
    }
    [Fact] public void UninstallPreservesExtensionMetadataAndAnotherUsersEntry() {
        var store = new Store(); var registration = new SetupRegistration(store); registration.Install(Receipt(), true);
        store.Write(@"Software\Classes\.ustxp", "CustomMetadata", "keep"); registration.Uninstall(Receipt().Directory, "other-user");
        Assert.NotNull(store.Read(SetupRegistration.UninstallKey, "DisplayName"));
        registration.Uninstall(Receipt().Directory, "user");
        Assert.Null(store.Read(SetupRegistration.UninstallKey, "DisplayName")); Assert.Equal("keep", store.Read(@"Software\Classes\.ustxp", "CustomMetadata"));
    }
    [Fact] public void CommandLineOptionsSurviveRoundTripWithChineseSpacesAndQuietAlias() {
        var parsed = SetupCommandLine.Parse(new[] { "--quiet", "--uninstall", "--no-desktop", "--no-association", "--remove-data", "--dir", @"G:\中文 空格\UTvTU" });
        Assert.True(parsed.Silent); Assert.Equal(parsed, SetupCommandLine.Parse(parsed.Arguments().ToArray()));
        Assert.Throws<ArgumentException>(() => SetupCommandLine.Parse(new[] { "--dir" }));
        Assert.Throws<ArgumentException>(() => SetupCommandLine.Parse(new[] { "--unknown" }));
    }
}

using UTvTU.Setup;
using Xunit;

[Collection("Setup")]
public class SetupWorldlineAbiTests {
    static string Root {
        get {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OpenUtau.sln"))) dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root missing.");
        }
    }
    [Fact]
    public void CurrentWindowsLibraryHasTheCallerOwnedWorldlineAbi() =>
        PackageBuilder.ValidateWorldlineAbi(Path.Combine(Root, "runtimes", "win-x64", "native", "worldline.dll"));

    [Fact]
    public void UnrelatedNativeBridgeCannotPassAsWorldline() =>
        Assert.Throws<InvalidDataException>(() => PackageBuilder.ValidateWorldlineAbi(Path.Combine(Root, "runtimes", "win-x64", "native", "vst_bridge.dll")));
}

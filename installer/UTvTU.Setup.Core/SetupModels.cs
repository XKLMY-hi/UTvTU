using System.Text.Json;

namespace UTvTU.Setup;

public sealed record PackageFile(string Path, long Length, string Sha256);

public sealed class PackageManifest {
    public const string FileName = "utvtu-package.json";
    public int Schema { get; set; } = 1;
    public string Product { get; set; } = "UTvTU";
    public string Version { get; set; } = "";
    public string SourceCommit { get; set; } = "";
    public string Runtime { get; set; } = "win-x64";
    public List<PackageFile> Files { get; set; } = new();
}

public sealed class InstallReceipt {
    public const string FileName = ".utvtu-install.json";
    public int Schema { get; set; } = 1;
    public string Product { get; set; } = "UTvTU";
    public string Version { get; set; } = "";
    public string SourceCommit { get; set; } = "";
    public string Directory { get; set; } = "";
    public string UserSid { get; set; } = "";
    public string DataDirectory { get; set; } = "";
    public bool PortableData { get; set; }
    public List<PackageFile> Files { get; set; } = new();
}

public sealed record SetupOptions {
    public string Directory { get; init; } = "";
    public string UserSid { get; init; } = "";
    public string DataDirectory { get; init; } = "";
    public bool DesktopShortcut { get; init; } = true;
    public bool FileAssociation { get; init; } = true;
    public bool RemoveData { get; init; }
    public bool Uninstall { get; init; }
    public bool Silent { get; init; }
}

public sealed record SetupProgress(int Percent, string Stage, string? Detail = null);
public sealed record SetupResult(bool Success, string Message, IReadOnlyList<string> Warnings) {
    public static SetupResult Completed(string message, params string[] warnings) => new(true, message, warnings);
}

public static class SetupJson {
    public static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("安装记录为空。");
    public static void Write<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Options));
}

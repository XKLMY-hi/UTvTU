using System.Diagnostics;
using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;

namespace UTvTU.Setup;

public sealed record BuildIdentity(string Version, string SourceCommit, string Runtime);
public static class PackageBuilder {
    public const string IdentityFile = "utvtu-build.json";
    public static readonly string[] RequiredFiles = {
        "OpenUtau.exe", "OpenUtau.dll", "OpenUtau.deps.json", "OpenUtau.runtimeconfig.json", "OpenUtau.Core.dll", "OpenUtau.Plugin.Builtin.dll", "UTvTU.DawUI.dll", "UTvTU.Md3.dll",
        "vst_probe.exe", "vst_probe.dll", "vst_probe.deps.json", "vst_probe.runtimeconfig.json", "vst_bridge.dll", "worldline.dll",
        "coreclr.dll", "hostfxr.dll", "hostpolicy.dll", "System.Private.CoreLib.dll", "vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll", "UTvTU-Uninstall.exe"
    };
    public static BuildIdentity ValidatePublished(string directory) {
        directory = SetupPaths.Full(directory); SetupPaths.RejectLinks(directory);
        foreach (string path in SetupPaths.EnumerateFiles(directory)) {
            string relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
            SetupPaths.Relative(relative);
            if (SetupPaths.IsUserData(relative) || relative.Equals("installed.txt", StringComparison.OrdinalIgnoreCase) || relative.Equals(InstallReceipt.FileName, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"发布目录混入用户数据或安装状态：{relative}");
        }
        var identity = SetupJson.Read<BuildIdentity>(Path.Combine(directory, IdentityFile));
        if (identity.Runtime != "win-x64" || !identity.Version.StartsWith("UTvTU-", StringComparison.Ordinal) || identity.SourceCommit.Length != 40 || !identity.SourceCommit.All(Uri.IsHexDigit)) throw new InvalidDataException("发布来源标识无效。");
        foreach (string required in RequiredFiles) if (!File.Exists(SetupPaths.Resolve(directory, required))) throw new InvalidDataException($"发布缺少文件：{required}");
        foreach (string exe in new[] { "OpenUtau.exe", "OpenUtau.dll", "UTvTU-Uninstall.exe" }) {
            if (FileVersionInfo.GetVersionInfo(Path.Combine(directory, exe)).ProductVersion != identity.Version) throw new InvalidDataException($"版本不一致：{exe}");
        }
        using (var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "OpenUtau.runtimeconfig.json")))) {
            var runtime = config.RootElement.GetProperty("runtimeOptions");
            if (runtime.TryGetProperty("framework", out _) || runtime.TryGetProperty("frameworks", out _)) throw new InvalidDataException("主程序必须自带 .NET 运行时。");
        }
        foreach (string native in new[] { "OpenUtau.exe", "vst_probe.exe", "UTvTU-Uninstall.exe", "vst_bridge.dll", "worldline.dll" }) {
            using var stream = File.OpenRead(Path.Combine(directory, native)); using var pe = new PEReader(stream);
            if (pe.PEHeaders.CoffHeader.Machine != Machine.Amd64) throw new InvalidDataException($"不是 Windows x64 文件：{native}");
        }
        var exports = Exports(Path.Combine(directory, "vst_bridge.dll"));
        foreach (string name in new[] { "vst_open_editor_window_owned", "vst_setup_export", "vst_process_export" }) if (!exports.Contains(name)) throw new InvalidDataException($"VST 桥缺少接口：{name}");
        return identity;
    }
    static HashSet<string> Exports(string path) {
        using var stream = File.OpenRead(path); using var pe = new PEReader(stream);
        var table = pe.PEHeaders.PEHeader!.ExportTableDirectory;
        if (table.RelativeVirtualAddress == 0) throw new InvalidDataException("原生桥没有导出表。");
        var reader = pe.GetSectionData(table.RelativeVirtualAddress).GetReader(); reader.Offset = 24; int count = reader.ReadInt32(); reader.Offset = 32; int addresses = reader.ReadInt32();
        if (count < 0 || count > 10000) throw new InvalidDataException("原生桥导出表无效。");
        reader = pe.GetSectionData(addresses).GetReader(); var result = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++) {
            var text = pe.GetSectionData(reader.ReadInt32()).GetContent(); int end = 0;
            while (end < text.Length && end < 256 && text[end] != 0) end++;
            result.Add(Encoding.ASCII.GetString(text.AsSpan(0, end)));
        }
        return result;
    }
    public static PackageManifest Create(string directory, string destination) {
        var identity = ValidatePublished(directory);
        if (SetupPaths.Within(directory, destination) || SetupPaths.Same(directory, destination)) throw new IOException("安装载荷不能写入发布源目录。");
        var manifest = new PackageManifest { Version = identity.Version, SourceCommit = identity.SourceCommit, Runtime = identity.Runtime };
        foreach (string path in SetupPaths.EnumerateFiles(directory).OrderBy(path => path, StringComparer.OrdinalIgnoreCase)) {
            SetupPaths.RejectLinks(path); manifest.Files.Add(SetupPackage.Describe(directory, path));
        }
        SetupPackage.ValidateManifest(manifest);
        using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create)) {
            foreach (var file in manifest.Files) zip.CreateEntryFromFile(SetupPaths.Resolve(directory, file.Path), file.Path, CompressionLevel.Optimal);
            using var writer = new StreamWriter(zip.CreateEntry(PackageManifest.FileName).Open()); writer.Write(JsonSerializer.Serialize(manifest, SetupJson.Options));
        }
        Verify(destination); return manifest;
    }
    public static PackageManifest Verify(string path) {
        using var source = File.OpenRead(path); using var package = new SetupPackage(source); package.Verify(); return package.Manifest;
    }
}

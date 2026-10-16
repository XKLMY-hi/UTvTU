using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace UTvTU.Setup;

public sealed class SetupPackage : IDisposable {
    readonly ZipArchive archive;
    readonly Dictionary<string, ZipArchiveEntry> entries = new(StringComparer.OrdinalIgnoreCase);
    public PackageManifest Manifest { get; }
    public SetupPackage(Stream source) {
        try {
            archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: false);
            long total = 0;
            foreach (var entry in archive.Entries) {
                if (entry.FullName.EndsWith('/')) { SetupPaths.Relative(entry.FullName.TrimEnd('/')); continue; }
                string name = SetupPaths.Relative(entry.FullName);
                if ((entry.ExternalAttributes >> 16 & 0xF000) == 0xA000 || !entries.TryAdd(name, entry)) throw new InvalidDataException("安装包包含链接或重复文件。");
                total = checked(total + entry.Length);
                if (entry.Length > 2L * 1024 * 1024 * 1024 || total > 8L * 1024 * 1024 * 1024 || entries.Count > 30000) throw new InvalidDataException("安装包超出允许大小。");
            }
            if (!entries.TryGetValue(PackageManifest.FileName, out var descriptor) || descriptor.Length > 8 * 1024 * 1024) throw new InvalidDataException("缺少有效的安装包清单。");
            using var data = descriptor.Open();
            Manifest = JsonSerializer.Deserialize<PackageManifest>(data, SetupJson.Options) ?? throw new InvalidDataException("清单为空。");
            ValidateManifest(Manifest);
            if (entries.Count != Manifest.Files.Count + 1) throw new InvalidDataException("安装包包含未登记的文件。");
            foreach (var file in Manifest.Files) {
                if (!entries.TryGetValue(file.Path, out var entry) || entry.Length != file.Length) throw new InvalidDataException($"安装包文件缺失或长度不符：{file.Path}");
            }
        } catch { source.Dispose(); throw; }
    }

    public static void ValidateManifest(PackageManifest manifest) {
        if (manifest.Schema != 1 || manifest.Product != "UTvTU" || manifest.Runtime != "win-x64" || string.IsNullOrWhiteSpace(manifest.Version) || manifest.SourceCommit.Length != 40 || !manifest.SourceCommit.All(Uri.IsHexDigit)) throw new InvalidDataException("安装包身份、版本或平台无效。");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files) {
            SetupPaths.Relative(file.Path);
            if (!names.Add(file.Path) || SetupPaths.IsUserData(file.Path) || file.Path.Equals(InstallReceipt.FileName, StringComparison.OrdinalIgnoreCase) || file.Path.Equals(PackageManifest.FileName, StringComparison.OrdinalIgnoreCase) || file.Path.Equals("installed.txt", StringComparison.OrdinalIgnoreCase) || file.Length < 0 || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit)) throw new InvalidDataException($"无效的清单条目：{file.Path}");
        }
        foreach (string required in new[] { "OpenUtau.exe", "UTvTU-Uninstall.exe" }) if (!names.Contains(required)) throw new InvalidDataException($"缺少必需文件：{required}");
    }

    public void ExtractTo(string staging, IProgress<SetupProgress>? progress, CancellationToken token) {
        long total = Math.Max(1, Manifest.Files.Sum(file => file.Length)), written = 0;
        byte[] buffer = new byte[65536];
        foreach (var file in Manifest.Files) {
            token.ThrowIfCancellationRequested();
            string target = SetupPaths.Resolve(staging, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = entries[file.Path].Open();
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long length = 0;
            for (int count; (count = input.Read(buffer)) > 0;) {
                token.ThrowIfCancellationRequested();
                length += count;
                if (length > file.Length) throw new InvalidDataException("安装包实际大小超过清单。");
                output.Write(buffer, 0, count); hash.AppendData(buffer, 0, count);
            }
            if (length != file.Length || !Convert.ToHexString(hash.GetHashAndReset()).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"文件完整性检查失败：{file.Path}");
            written += length;
            progress?.Report(new(5 + (int)(written * 60 / total), "校验并准备程序文件", file.Path));
        }
    }
    public static PackageFile Describe(string root, string path) {
        using var stream = File.OpenRead(path);
        return new(Path.GetRelativePath(root, path).Replace('\\', '/'), stream.Length, Convert.ToHexString(SHA256.HashData(stream)));
    }
    public static bool Matches(string path, PackageFile file) {
        if (!File.Exists(path)) return false;
        using var stream = File.OpenRead(path);
        return stream.Length == file.Length && Convert.ToHexString(SHA256.HashData(stream)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase);
    }
    public void Dispose() => archive.Dispose();
}

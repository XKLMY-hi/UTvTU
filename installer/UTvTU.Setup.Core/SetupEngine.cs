using System.Diagnostics;

namespace UTvTU.Setup;

/// <summary>Only package-owned files cross the transaction boundary. No registry or UI access.</summary>
public sealed class SetupEngine {
    public Func<string, bool> IsLegacyApplication { get; init; } = IsUtvtu;

    public static bool IsUtvtu(string directory) {
        string exe = Path.Combine(directory, "OpenUtau.exe");
        if (!File.Exists(exe)) return false;
        try {
            var version = FileVersionInfo.GetVersionInfo(exe);
            return (version.ProductVersion?.StartsWith("UTvTU-", StringComparison.OrdinalIgnoreCase) == true)
                || new[] { version.ProductName, version.FileDescription }.Any(value => value?.Contains("UTvTU", StringComparison.OrdinalIgnoreCase) == true || value?.Contains("OpenUTAU Plus", StringComparison.OrdinalIgnoreCase) == true || value?.Contains("OpenUTAU-Plus", StringComparison.OrdinalIgnoreCase) == true);
        } catch { return false; }
    }

    public static InstallReceipt? ReadReceipt(string directory) {
        string path = Path.Combine(directory, InstallReceipt.FileName);
        if (!File.Exists(path)) return null;
        SetupPaths.RejectLinks(path);
        if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new InvalidDataException("安装记录过大。");
        var receipt = SetupJson.Read<InstallReceipt>(path);
        if (receipt.Product != "UTvTU" || receipt.Schema != 1 || !SetupPaths.Same(receipt.Directory, directory)) throw new InvalidDataException("安装记录与目标目录不匹配。");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in receipt.Files) {
            SetupPaths.Resolve(directory, file.Path);
            if (!names.Add(file.Path) || SetupPaths.IsUserData(file.Path) || file.Path.Equals(InstallReceipt.FileName, StringComparison.OrdinalIgnoreCase) || file.Path.Equals(PackageManifest.FileName, StringComparison.OrdinalIgnoreCase) || file.Length < 0 || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit)) throw new InvalidDataException("安装文件清单无效。");
        }
        if (!names.Contains("OpenUtau.exe") || !names.Contains("UTvTU-Uninstall.exe")) throw new InvalidDataException("安装记录缺少程序身份。");
        return receipt;
    }

    public SetupResult Install(Stream source, SetupOptions options, IProgress<SetupProgress>? progress, CancellationToken token) {
        string target = SetupPaths.ValidateTarget(options.Directory);
        using var package = new SetupPackage(source);
        using var transaction = new FileTransaction(target);
        var previous = ReadReceipt(target);
        if (previous != null && previous.UserSid != options.UserSid) throw new IOException("此安装属于另一位用户，请使用当前用户的独立安装目录。");
        bool hasApplication = File.Exists(Path.Combine(target, "OpenUtau.exe"));
        if (previous == null && Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any() && !IsLegacyApplication(target)) throw new IOException("此文件夹不是可确认的 UTvTU 安装。请选择空文件夹，以保留现有文件。");
        bool portable = previous?.PortableData ?? (hasApplication && !File.Exists(Path.Combine(target, "installed.txt")));
        var receipt = new InstallReceipt { Directory = target, Version = package.Manifest.Version, SourceCommit = package.Manifest.SourceCommit, UserSid = options.UserSid, PortableData = portable, DataDirectory = portable ? target : SetupPaths.Full(options.DataDirectory), Files = package.Manifest.Files.ToList() };
        if (!portable && !Path.GetFileName(receipt.DataDirectory).Equals("OpenUtau Plus", StringComparison.OrdinalIgnoreCase)) throw new IOException("用户数据目录无效。");
        EnsureNotRunning(target);
        progress?.Report(new(2, "准备安装", target));
        package.ExtractTo(transaction.Stage, progress, token);
        if (!portable) {
            File.WriteAllText(Path.Combine(transaction.Stage, "installed.txt"), "yes");
            receipt.Files.Add(SetupPackage.Describe(transaction.Stage, Path.Combine(transaction.Stage, "installed.txt")));
        }
        SetupJson.Write(Path.Combine(transaction.Stage, InstallReceipt.FileName), receipt);
        var files = receipt.Files.Select(file => file.Path).Append(InstallReceipt.FileName).ToArray();
        var obsolete = previous?.Files.Where(old => !receipt.Files.Any(file => file.Path.Equals(old.Path, StringComparison.OrdinalIgnoreCase)))
            .Where(old => SetupPackage.Matches(SetupPaths.Resolve(target, old.Path), old)).Select(old => old.Path).ToArray() ?? Array.Empty<string>();
        transaction.Preflight(files.Concat(obsolete));
        token.ThrowIfCancellationRequested(); EnsureNotRunning(target);
        try {
            int count = 0;
            foreach (string file in files) {
                token.ThrowIfCancellationRequested(); transaction.Replace(file);
                progress?.Report(new(70 + ++count * 20 / files.Length, "更新程序文件", file));
            }
            foreach (string file in obsolete) { token.ThrowIfCancellationRequested(); transaction.Remove(file); }
            token.ThrowIfCancellationRequested();
            transaction.Commit();
        } catch (Exception error) { transaction.Rollback(error); throw; }
        progress?.Report(new(95, "程序文件已就绪"));
        return SetupResult.Completed("程序文件安装完成");
    }

    public SetupResult Uninstall(SetupOptions options, IProgress<SetupProgress>? progress, CancellationToken token) {
        string target = SetupPaths.ValidateTarget(options.Directory);
        var receipt = ReadReceipt(target) ?? throw new IOException("此目录没有可验证的 UTvTU 安装记录。旧版请先覆盖安装新版，再使用新版卸载器。");
        if (receipt.UserSid != options.UserSid) throw new IOException("不能卸载其他用户的安装。");
        EnsureNotRunning(target);
        var warnings = new List<string>();
        var owned = new List<string>();
        foreach (var file in receipt.Files) {
            string path = SetupPaths.Resolve(target, file.Path);
            if (!File.Exists(path)) continue;
            if (SetupPackage.Matches(path, file)) owned.Add(file.Path);
            else warnings.Add($"保留已修改文件：{file.Path}");
        }
        using var transaction = new FileTransaction(target);
        owned.Add(InstallReceipt.FileName);
        transaction.Preflight(owned);
        token.ThrowIfCancellationRequested(); EnsureNotRunning(target);
        try {
            int count = 0;
            foreach (string file in owned) {
                token.ThrowIfCancellationRequested(); transaction.Remove(file);
                progress?.Report(new(10 + ++count * 75 / owned.Count, "移除程序文件", file));
            }
            token.ThrowIfCancellationRequested(); transaction.Commit();
        } catch (Exception error) { transaction.Rollback(error); throw; }
        if (options.RemoveData) {
            try { DeleteUserData(receipt, options); }
            catch (Exception error) { warnings.Add("程序已卸载，部分用户数据未删除：" + error.Message); }
        }
        // Only remove known, now-empty ancestors. Never traverse unknown user folders.
        foreach (string relative in owned.Select(Path.GetDirectoryName).Where(path => !string.IsNullOrEmpty(path)).Distinct()!) {
            for (string? dir = Path.Combine(target, relative); dir != null && SetupPaths.Within(target, dir); dir = Path.GetDirectoryName(dir)) TryDeleteEmpty(dir);
        }
        TryDeleteEmpty(target);
        progress?.Report(new(95, "程序文件已移除"));
        return new(true, "程序文件卸载完成", warnings);
    }

    static void DeleteUserData(InstallReceipt receipt, SetupOptions options) {
        string expected = receipt.PortableData ? receipt.Directory : SetupPaths.Full(options.DataDirectory);
        if (!SetupPaths.Same(expected, receipt.DataDirectory) || !receipt.PortableData && !Path.GetFileName(expected).Equals("OpenUtau Plus", StringComparison.OrdinalIgnoreCase)) throw new IOException("数据目录与本次安装记录不一致，已保留。");
        SetupPaths.RejectLinks(expected);
        if (!Directory.Exists(expected)) return;
        // Never remove the install root or follow custom singer/sample search paths.
        foreach (string name in SetupPaths.DataFolders) {
            string path = SetupPaths.Resolve(expected, name);
            if (!Directory.Exists(path)) continue;
            foreach (string item in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories)) SetupPaths.RejectLinks(item);
            Directory.Delete(path, true);
        }
        foreach (string path in Directory.EnumerateFiles(expected)) {
            if (SetupPaths.IsUserData(Path.GetFileName(path))) { SetupPaths.RejectLinks(path); File.Delete(path); }
        }
        TryDeleteEmpty(expected);
    }

    public static void EnsureNotRunning(string target) {
        foreach (var process in Process.GetProcessesByName("OpenUtau")) {
            using (process) {
                string? path;
                try { path = process.MainModule?.FileName; } catch { continue; }
                if (path != null && SetupPaths.Same(path, Path.Combine(target, "OpenUtau.exe"))) throw new IOException("请先保存工程并关闭此安装目录中的 UTvTU，然后重试。");
            }
        }
    }

    static void TryDeleteEmpty(string path) { try { if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    sealed class FileTransaction : IDisposable {
        readonly string target, parent, prefix, backup;
        readonly FileStream gate;
        readonly List<(string Relative, bool HadOld)> applied = new();
        bool finished;
        public string Stage { get; }
        public FileTransaction(string target) {
            this.target = target; parent = Path.GetDirectoryName(target)!; prefix = "." + Path.GetFileName(target) + ".utvtu-";
            Directory.CreateDirectory(parent);
            string lockPath = Path.Combine(parent, prefix + "lock"); SetupPaths.RejectLinks(lockPath);
            try { gate = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose); }
            catch (IOException error) { throw new IOException("此目录已有安装或卸载任务，请等待它结束。", error); }
            string id = Guid.NewGuid().ToString("N"); Stage = Path.Combine(parent, prefix + "stage-" + id); backup = Path.Combine(parent, prefix + "backup-" + id);
            Directory.CreateDirectory(Stage); Directory.CreateDirectory(backup);
        }
        public void Preflight(IEnumerable<string> paths) {
            foreach (string relative in paths.Distinct(StringComparer.OrdinalIgnoreCase)) {
                string path = SetupPaths.Resolve(target, relative);
                if (Directory.Exists(path)) throw new IOException($"程序文件位置被文件夹占用：{relative}");
                if (File.Exists(path)) { using var check = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
            }
        }
        public void Replace(string relative) {
            string path = SetupPaths.Resolve(target, relative), old = SetupPaths.Resolve(backup, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            bool existed = File.Exists(path);
            if (existed) { Directory.CreateDirectory(Path.GetDirectoryName(old)!); File.Move(path, old); }
            applied.Add((relative, existed));
            File.Move(SetupPaths.Resolve(Stage, relative), path);
        }
        public void Remove(string relative) {
            string path = SetupPaths.Resolve(target, relative);
            if (!File.Exists(path)) return;
            string old = SetupPaths.Resolve(backup, relative); Directory.CreateDirectory(Path.GetDirectoryName(old)!);
            File.Move(path, old); applied.Add((relative, true));
        }
        public void Commit() { finished = true; }
        public void Rollback(Exception original) {
            try {
                foreach (var item in applied.AsEnumerable().Reverse()) {
                    string path = SetupPaths.Resolve(target, item.Relative);
                    if (File.Exists(path)) File.Delete(path);
                    if (item.HadOld) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.Move(SetupPaths.Resolve(backup, item.Relative), path); }
                }
                finished = true;
            } catch (Exception rollback) {
                throw new AggregateException($"恢复原版本遇到占用；原文件已保留在 {backup}，请关闭占用程序后恢复。", original, rollback);
            }
        }
        public void Dispose() {
            // If rollback failed, its original files must remain available for recovery.
            try { SetupPaths.DeleteTransactionDirectory(Stage, parent, prefix + "stage-"); } catch { }
            if (finished || applied.Count == 0) { try { SetupPaths.DeleteTransactionDirectory(backup, parent, prefix + "backup-"); } catch { } }
            gate.Dispose();
        }
    }
}

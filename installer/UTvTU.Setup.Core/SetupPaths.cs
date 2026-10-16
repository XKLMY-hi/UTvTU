namespace UTvTU.Setup;

public static class SetupPaths {
    public static readonly string[] DataFolders = { "Backups", "Cache", "UCache", "Singers", "Resamplers", "Wavtools", "Plugins", "Dependencies", "Dictionaries", "Templates", "Samples", "Logs", "Themes", "Content", "OpenUtau Plus" };
    public static readonly string[] DataFiles = { "prefs.json", "notepresets.json", "vst-plugins.json", "vst_plugins.json" };
    public static string Full(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    public static bool Same(string left, string right) => string.Equals(Full(left), Full(right), StringComparison.OrdinalIgnoreCase);
    public static bool Within(string root, string path) => Full(path).StartsWith(Full(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public static string UserDataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "OpenUtau Plus");

    public static string Relative(string path) {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(':') || path.Contains('\\') || path.EndsWith('/'))
            throw new InvalidDataException($"不安全的文件路径：{path}");
        var parts = path.Split('/');
        foreach (string part in parts) {
            if (part is "." or ".." || part.Length == 0 || part.EndsWith('.') || part.EndsWith(' ') || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException($"不安全的文件路径：{path}");
            string stem = part.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsAsciiDigit(stem[3])))
                throw new InvalidDataException($"保留文件名：{path}");
        }
        return string.Join('/', parts);
    }

    public static bool IsUserData(string relative) {
        var parts = relative.Replace('\\', '/').Split('/');
        if (parts.Any(part => DataFolders.Contains(part, StringComparer.OrdinalIgnoreCase))) return true;
        if (DataFiles.Contains(parts[^1], StringComparer.OrdinalIgnoreCase)) return true;
        return Path.GetExtension(relative).ToLowerInvariant() is ".ustxp" or ".ustx" or ".ust" or ".wav" or ".flac" or ".mp3";
    }

    public static string Resolve(string root, string relative) {
        relative = Relative(relative);
        string target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!Within(root, target)) throw new InvalidDataException("文件超出安装目录。");
        RejectLinks(target);
        return target;
    }

    public static void RejectLinks(string path) {
        for (string? p = Path.GetFullPath(path); p != null; p = Path.GetDirectoryName(p)) {
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"安装路径不能经过符号链接或目录联接：{p}");
        }
    }

    public static string ValidateTarget(string path) {
        if (!Path.IsPathFullyQualified(path)) throw new IOException("请选择完整的安装路径。");
        string full = Full(path);
        if (string.Equals(full, Full(Path.GetPathRoot(full)!), StringComparison.OrdinalIgnoreCase)) throw new IOException("不能将磁盘根目录作为安装位置。");
        foreach (var special in new[] { Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.Personal, Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.Windows }) {
            string folder = Environment.GetFolderPath(special);
            if (folder.Length > 0 && (Same(full, folder) || special == Environment.SpecialFolder.Windows && Within(folder, full)))
                throw new IOException("请选择专用的 UTvTU 安装文件夹。");
        }
        RejectLinks(full);
        return full;
    }

    internal static void DeleteTransactionDirectory(string path, string parent, string prefix) {
        if (!Within(parent, path) || !Path.GetFileName(path).StartsWith(prefix, StringComparison.Ordinal)) throw new IOException("事务清理范围无效。");
        RejectLinks(path);
        if (!Directory.Exists(path)) return;
        foreach (string entry in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories)) RejectLinks(entry);
        Directory.Delete(path, true);
    }
}

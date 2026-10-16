namespace UTvTU.Setup;

public static class LegacyInstallLocation {
    public static string? Resolve(string? location, string? icon, string? uninstaller, Func<string, bool> exists) {
        if (!string.IsNullOrWhiteSpace(location) && Path.IsPathFullyQualified(location)) return SetupPaths.Full(location);
        foreach (string? value in new[] { icon, uninstaller }) {
            if (string.IsNullOrWhiteSpace(value)) continue;
            string path = value.Trim();
            if (path.StartsWith('"')) { int closing = path.IndexOf('"', 1); if (closing < 0) continue; path = path[1..closing]; }
            else {
                int comma = path.LastIndexOf(',');
                if (comma >= 0 && int.TryParse(path[(comma + 1)..], out _)) path = path[..comma];
            }
            if (Path.IsPathFullyQualified(path) && exists(path)) return Path.GetDirectoryName(Path.GetFullPath(path));
        }
        return null;
    }
}

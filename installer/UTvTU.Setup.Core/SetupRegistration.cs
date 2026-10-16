namespace UTvTU.Setup;

public interface IRegistrationStore {
    object? Read(string key, string name);
    void Write(string key, string name, object value);
    void DeleteValue(string key, string name);
    void DeleteTree(string key);
}

/// <summary>Same registration plan for GUI and silent installs; testable without HKCU.</summary>
public sealed class SetupRegistration(IRegistrationStore store) {
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\UTvTU";
    public const string LegacyKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\OpenUtau";
    public const string ProgId = "UTvTU.UstxFile";
    const string Classes = @"Software\Classes\";
    public void Install(InstallReceipt receipt, bool associations) {
        string exe = Path.Combine(receipt.Directory, "OpenUtau.exe"), uninstaller = Path.Combine(receipt.Directory, "UTvTU-Uninstall.exe");
        var changes = new List<(string Key, string Name, object Value)> {
            (UninstallKey, "DisplayName", "UTvTU"), (UninstallKey, "DisplayVersion", receipt.Version),
            (UninstallKey, "Publisher", "XKLMY-hi"), (UninstallKey, "InstallLocation", receipt.Directory),
            (UninstallKey, "OwnerSid", receipt.UserSid), (UninstallKey, "DisplayIcon", exe),
            (UninstallKey, "UninstallString", $"\"{uninstaller}\" --uninstall --dir \"{receipt.Directory}\""),
            (UninstallKey, "QuietUninstallString", $"\"{uninstaller}\" --uninstall --silent --dir \"{receipt.Directory}\""),
            (UninstallKey, "URLInfoAbout", "https://github.com/XKLMY-hi/UTvTU"),
            (UninstallKey, "NoModify", 1), (UninstallKey, "NoRepair", 1)
        };
        if (associations) {
            changes.Add((Classes + ProgId, "", "UTvTU 工程"));
            changes.Add((Classes + ProgId + @"\DefaultIcon", "", $"\"{exe}\",0"));
            changes.Add((Classes + ProgId + @"\shell\open\command", "", $"\"{exe}\" \"%1\""));
            foreach (string extension in new[] { ".ustx", ".ustxp" }) {
                changes.Add((Classes + extension + @"\OpenWithProgids", ProgId, ""));
                // Respect an existing handler and Windows UserChoice. New extensions can
                // use this ProgID; other applications remain available in Open With.
                if (store.Read(Classes + extension, "") is not string current || current.Length == 0)
                    changes.Add((Classes + extension, "", ProgId));
            }
        }
        var original = new List<(string Key, string Name, object? Value)>();
        try {
            foreach (var change in changes) { original.Add((change.Key, change.Name, store.Read(change.Key, change.Name))); store.Write(change.Key, change.Name, change.Value); }
        } catch {
            foreach (var item in original.AsEnumerable().Reverse()) {
                if (item.Value == null) store.DeleteValue(item.Key, item.Name); else store.Write(item.Key, item.Name, item.Value);
            }
            throw;
        }
    }
    public bool OwnsRegistration(string directory, string sid) => store.Read(UninstallKey, "InstallLocation") is string path && SetupPaths.Same(path, directory) && (store.Read(UninstallKey, "OwnerSid") is not string owner || owner == sid);
    public void Uninstall(string directory, string sid) {
        if (!OwnsRegistration(directory, sid)) return;
        foreach (string extension in new[] { ".ustx", ".ustxp" }) {
            store.DeleteValue(Classes + extension + @"\OpenWithProgids", ProgId);
            if (store.Read(Classes + extension, "") as string == ProgId) store.DeleteValue(Classes + extension, "");
        }
        if (store.Read(Classes + ProgId + @"\shell\open\command", "") as string == $"\"{Path.Combine(directory, "OpenUtau.exe")}\" \"%1\"") store.DeleteTree(Classes + ProgId);
        // Keep the uninstall entry if any previous integration cleanup failed.
        store.DeleteTree(UninstallKey);
    }
}

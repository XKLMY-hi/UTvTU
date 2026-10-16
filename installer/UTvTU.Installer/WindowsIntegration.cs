using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using UTvTU.Setup;

namespace UTvTU.Installer;

sealed class WindowsIntegration {
    sealed class RegistryStore : IRegistrationStore {
        public object? Read(string key, string name) { using var entry = Registry.CurrentUser.OpenSubKey(key); return entry?.GetValue(name); }
        public void Write(string key, string name, object value) { using var entry = Registry.CurrentUser.CreateSubKey(key, true); entry.SetValue(name, value, value is int ? RegistryValueKind.DWord : RegistryValueKind.String); }
        public void DeleteValue(string key, string name) { using var entry = Registry.CurrentUser.OpenSubKey(key, true); entry?.DeleteValue(name, false); }
        public void DeleteTree(string key) => Registry.CurrentUser.DeleteSubKeyTree(key, false);
    }
    readonly SetupRegistration registration = new(new RegistryStore());
    static string StartMenu => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "UTvTU.lnk");
    static string Desktop => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "UTvTU.lnk");
    static string CacheDirectory(string directory) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UTvTU", "Installers", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SetupPaths.Full(directory).ToUpperInvariant())))[..24]);
    static string? RegisteredPath(RegistryKey? entry, bool requireFiles) => entry == null ? null : LegacyInstallLocation.Resolve(entry.GetValue("InstallLocation") as string, entry.GetValue("DisplayIcon") as string, entry.GetValue("UninstallString") as string, path => !requireFiles || File.Exists(path));
    public static bool HasLegacyMachineRegistration(string directory) {
        if (!SetupEngine.IsUtvtu(directory)) return false;
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 }) {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view); using var entry = root.OpenSubKey(SetupRegistration.LegacyKey);
            if (RegisteredPath(entry, true) is string path && SetupPaths.Same(path, directory)) return true;
        }
        return false;
    }
    public static void CleanLegacyMachineRegistration(string directory) {
        if (!InstallerCore.IsElevated()) throw new UnauthorizedAccessException("清理旧版机器级登记需要管理员权限。");
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 }) {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view); using var entry = root.OpenSubKey(SetupRegistration.LegacyKey);
            if (RegisteredPath(entry, false) is string path && SetupPaths.Same(path, directory)) root.DeleteSubKeyTree(SetupRegistration.LegacyKey, false);
        }
    }
    public void Install(SetupOptions options, InstallReceipt receipt, bool verifiedLegacy) {
        if (receipt.UserSid != InstallerCore.UserSid) throw new IOException("系统入口必须由安装所属用户创建。");
        string cache = CacheDirectory(options.Directory); Directory.CreateDirectory(cache); SetupPaths.RejectLinks(cache);
        string source = Path.Combine(options.Directory, InstallerCore.UninstExe), uninstaller = Path.Combine(cache, InstallerCore.UninstExe);
        string temporary = Path.Combine(cache, Guid.NewGuid().ToString("N") + ".tmp");
        File.Copy(source, temporary);
        try { File.Move(temporary, uninstaller, true); } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        SetupJson.Write(Path.Combine(cache, "target.json"), options);
        // Settings/silent uninstall runs outside the target, so waiting for its exit
        // cannot keep the installed uninstaller executable locked.
        registration.Install(receipt, options.FileAssociation, uninstaller);
        string exe = Path.Combine(options.Directory, InstallerCore.AppExe);
        Shortcut(StartMenu, exe); if (options.DesktopShortcut) Shortcut(Desktop, exe);
        if (verifiedLegacy) {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 }) {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
                using var entry = root.OpenSubKey(SetupRegistration.LegacyKey);
                if (RegisteredPath(entry, true) is string path && SetupPaths.Same(path, options.Directory)) root.DeleteSubKeyTree(SetupRegistration.LegacyKey, false);
            }
            foreach (string folder in new[] { Path.GetDirectoryName(StartMenu)!, Path.GetDirectoryName(Desktop)! })
                foreach (string name in new[] { "OpenUtau.lnk", "OpenUTAU Plus.lnk", "OpenUTAU-Plus.lnk" }) RemoveShortcut(Path.Combine(folder, name), exe);
        }
    }
    public void Uninstall(SetupOptions options, InstallReceipt receipt) {
        if (receipt.UserSid != InstallerCore.UserSid) throw new IOException("不能更改其他用户的系统入口。");
        string exe = Path.Combine(options.Directory, InstallerCore.AppExe);
        RemoveShortcut(StartMenu, exe); RemoveShortcut(Desktop, exe); registration.Uninstall(options.Directory, receipt.UserSid);
    }
    static object Shell() => Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException("Windows 快捷方式服务不可用。"))!;
    internal static void Shortcut(string path, string exe) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        dynamic shell = Shell(); dynamic shortcut = shell.CreateShortcut(path);
        shortcut.TargetPath = exe; shortcut.WorkingDirectory = Path.GetDirectoryName(exe); shortcut.IconLocation = exe; shortcut.Description = "UTvTU 人声制作工作站"; shortcut.Save();
    }
    internal static void RemoveShortcut(string path, string expected) {
        if (!File.Exists(path)) return;
        dynamic shell = Shell(); dynamic shortcut = shell.CreateShortcut(path);
        if (shortcut.TargetPath is string target && SetupPaths.Same(target, expected)) File.Delete(path);
    }
}

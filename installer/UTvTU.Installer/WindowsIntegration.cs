using System;
using System.IO;
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
    public void Install(SetupOptions options, InstallReceipt receipt, bool verifiedLegacy) {
        if (receipt.UserSid != InstallerCore.UserSid) throw new IOException("系统入口必须由安装所属用户创建。");
        registration.Install(receipt, options.FileAssociation);
        string exe = Path.Combine(options.Directory, InstallerCore.AppExe);
        Shortcut(StartMenu, exe); if (options.DesktopShortcut) Shortcut(Desktop, exe);
        if (verifiedLegacy) {
            foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine }) {
                using var entry = root.OpenSubKey(SetupRegistration.LegacyKey);
                if (entry?.GetValue("InstallLocation") is string path && SetupPaths.Same(path, options.Directory)) {
                    try { root.DeleteSubKeyTree(SetupRegistration.LegacyKey, false); } catch (UnauthorizedAccessException) { }
                }
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
    static void Shortcut(string path, string exe) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        dynamic shell = Shell(); dynamic shortcut = shell.CreateShortcut(path);
        shortcut.TargetPath = exe; shortcut.WorkingDirectory = Path.GetDirectoryName(exe); shortcut.IconLocation = exe; shortcut.Description = "UTvTU 人声制作工作站"; shortcut.Save();
    }
    static void RemoveShortcut(string path, string expected) {
        if (!File.Exists(path)) return;
        dynamic shell = Shell(); dynamic shortcut = shell.CreateShortcut(path);
        if (shortcut.TargetPath is string target && SetupPaths.Same(target, expected)) File.Delete(path);
    }
}

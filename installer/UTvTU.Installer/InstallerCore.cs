using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using Microsoft.Win32;

namespace UTvTU.Installer {
    /// <summary>
    /// 安装/卸载核心。设计红线（见 .opencode/plans/installer-design.md）：
    ///   ① **绝不删用户数据**（数据目录 / Backups / UCache / Cache / 用户工程）；
    ///   ② 清理旧版注册表键与快捷方式前**必须比对 InstallLocation**
    ///      —— 旧版 OpenUTAU Plus 顶着 "OpenUtau" 名字注册，不比对会误删用户真正的上游 OpenUTAU；
    ///   ③ 只覆盖程序文件；同名覆盖，不做"清理未知文件"。
    /// </summary>
    public static class InstallerCore {
        public const string AppName = "UTvTU";
        public const string AppExe = "OpenUtau.exe";
        public const string UninstExe = "UTvTU-Uninstall.exe";
        public const string PayloadName = "payload.zip";
        public const string UninstKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\UTvTU";
        public const string LegacyUninstKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\OpenUtau";
        public const string HomeUrl = "https://github.com/XKLMY-hi/UTvTU";

        /// <summary>用户数据目录名（与主程序一致；**改名会迁移/丢失用户数据，永不动**）。</summary>
        public const string UserDataFolderName = "OpenUtau Plus";

        private static readonly string[] KeepOnUninstall = { "Backups", "UCache", "Cache" };

        public static bool UninstallMode { get; private set; }
        public static bool Silent { get; private set; }
        public static bool NoDesktopShortcut { get; private set; }
        public static string? CliDir { get; private set; }
        public static bool WasElevated { get; private set; }
        public static string[] RawArgs { get; private set; } = Array.Empty<string>();

        /// <summary>版本号 = 构建时刻版本号（csproj SetBuildStamp ⇒ InformationalVersion），形如 UTvTU-26.10.7-143025。</summary>
        public static string Version { get; } =
            System.Reflection.Assembly.GetExecutingAssembly()
                .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
            ?? "0.0.0";

        public static void ParseArgs(string[] args) {
            RawArgs = args;
            for (int i = 0; i < args.Length; i++) {
                switch (args[i]) {
                    case "--uninstall":
                        UninstallMode = true;
                        break;
                    case "--silent":
                        Silent = true;
                        break;
                    case "--no-desktop":
                        NoDesktopShortcut = true;
                        break;
                    case "--elevated":
                        WasElevated = true;
                        break;
                    case "--dir" when i + 1 < args.Length:
                        CliDir = args[++i];
                        break;
                }
            }
        }

        public static string SelfPath => Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName!;
        public static string SelfDir => AppContext.BaseDirectory;
        public static string PayloadPath => Path.Combine(SelfDir, PayloadName);
        public static bool HasPayload => File.Exists(PayloadPath);

        // ── 目录探测 ────────────────────────────────────────────────────────────

        private static string? ReadInstallLocation(string subKey) {
            foreach (RegistryKey root in new[] { Registry.CurrentUser, Registry.LocalMachine }) {
                using RegistryKey? k = root.OpenSubKey(subKey);
                if (k?.GetValue("InstallLocation") is string s && s.Length > 0) {
                    return s.TrimEnd('\\');
                }
            }
            return null;
        }

        /// <summary>旧版（OpenUTAU Plus，注册成 OpenUtau）的安装位置与显示名；没装过则为 null。</summary>
        public static (string Path, string DisplayName)? DetectLegacy() {
            foreach (RegistryKey root in new[] { Registry.CurrentUser, Registry.LocalMachine }) {
                using RegistryKey? k = root.OpenSubKey(LegacyUninstKey);
                if (k?.GetValue("InstallLocation") is string loc && loc.Length > 0) {
                    string name = k.GetValue("DisplayName") as string ?? "OpenUtau";
                    return (loc.TrimEnd('\\'), name);
                }
            }
            return null;
        }

        /// <summary>默认安装目录：沿用已装版本（避免装两份）→ 旧版位置 → 按用户目录（免 UAC）。</summary>
        public static string DefaultInstallDir() {
            if (!string.IsNullOrWhiteSpace(CliDir)) {
                return CliDir!;
            }
            string? own = ReadInstallLocation(UninstKey);
            if (own != null) {
                return own;
            }
            (string Path, string DisplayName)? legacy = DetectLegacy();
            if (legacy != null && Directory.Exists(legacy.Value.Path)) {
                return legacy.Value.Path;
            }
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);
        }

        public static bool IsElevated() {
            using WindowsIdentity id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }

        /// <summary>目标目录是否需要管理员（Program Files / Windows 目录）。</summary>
        public static bool NeedsElevation(string dir) {
            string full = Path.GetFullPath(dir).TrimEnd('\\');
            foreach (Environment.SpecialFolder f in new[] {
                Environment.SpecialFolder.ProgramFiles,
                Environment.SpecialFolder.ProgramFilesX86,
                Environment.SpecialFolder.Windows,
            }) {
                string root = Environment.GetFolderPath(f);
                if (root.Length > 0 && full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>提权重开自身；失败返回 false（调用方提示用户）。</summary>
        public static bool RelaunchElevated(string dir) {
            var args = new List<string> { "--elevated", "--dir", dir };
            if (UninstallMode) {
                args.Add("--uninstall");
            }
            var psi = new ProcessStartInfo(SelfPath) {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)),
            };
            try {
                Process.Start(psi);
                return true;
            } catch {
                return false;   // 用户取消 UAC
            }
        }

        // ── 安装 ────────────────────────────────────────────────────────────────

        /// <summary>执行安装。progress: (百分比, 阶段文本)。</summary>
        public static void Install(string dir, bool desktopShortcut, bool fileAssociation,
                                   IProgress<(int Percent, string Stage)> progress, CancellationToken ct) {
            dir = Path.GetFullPath(dir);
            progress.Report((0, "准备安装目录…"));
            Directory.CreateDirectory(dir);

            progress.Report((2, "解包程序文件…"));
            ExtractPayload(dir, p => progress.Report((2 + p * 80 / 100, "解包程序文件…")), ct);

            progress.Report((84, "创建快捷方式…"));
            string exe = Path.Combine(dir, AppExe);
            CreateShortcut(StartMenuShortcut(), exe, AppName);
            if (desktopShortcut) {
                CreateShortcut(DesktopShortcut(), exe, AppName);
            }
            RemoveLegacyShortcuts(dir);

            progress.Report((90, "写入注册表…"));
            WriteUninstallEntry(dir);
            if (fileAssociation) {
                WriteFileAssociation(exe);
            }

            progress.Report((95, "生成卸载程序…"));
            string uninst = Path.Combine(dir, UninstExe);
            try {
                File.Copy(SelfPath, uninst, overwrite: true);
            } catch (IOException) {
                // 旧卸载器正在运行等；不致命
            }

            progress.Report((100, "完成"));
        }

        private static void ExtractPayload(string dir, Action<int> onProgress, CancellationToken ct) {
            using ZipArchive zip = ZipFile.OpenRead(PayloadPath);
            int total = Math.Max(1, zip.Entries.Count);
            for (int i = 0; i < zip.Entries.Count; i++) {
                ct.ThrowIfCancellationRequested();
                ZipArchiveEntry e = zip.Entries[i];
                string target = Path.GetFullPath(Path.Combine(dir, e.FullName));
                if (!target.StartsWith(Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase)) {
                    continue;   // 防 zip-slip
                }
                if (e.FullName.EndsWith('/')) {
                    Directory.CreateDirectory(target);
                } else {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    e.ExtractToFile(target, overwrite: true);
                }
                if (i % 20 == 0) {
                    onProgress(i * 100 / total);
                }
            }
            onProgress(100);
        }

        private static void WriteUninstallEntry(string dir) {
            using RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstKey, writable: true)!;
            k.SetValue("DisplayName", AppName);
            k.SetValue("DisplayVersion", Version);
            k.SetValue("Publisher", "XKLMY-hi");
            k.SetValue("DisplayIcon", Path.Combine(dir, AppExe));
            k.SetValue("InstallLocation", dir);
            k.SetValue("UninstallString", $"\"{Path.Combine(dir, UninstExe)}\" --uninstall --dir \"{dir}\"");
            k.SetValue("QuietUninstallString", $"\"{Path.Combine(dir, UninstExe)}\" --uninstall --dir \"{dir}\" --quiet");
            k.SetValue("URLInfoAbout", HomeUrl);
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);

            // ⚠ 旧版注册成 OpenUtau：**仅当它指向同一个安装目录**才清理（否则会误删真正的上游 OpenUTAU）
            foreach (RegistryKey root in new[] { Registry.CurrentUser, Registry.LocalMachine }) {
                using RegistryKey? legacy = root.OpenSubKey(LegacyUninstKey);
                if (legacy?.GetValue("InstallLocation") is string loc &&
                    string.Equals(loc.TrimEnd('\\'), dir, StringComparison.OrdinalIgnoreCase)) {
                    try {
                        root.DeleteSubKeyTree(LegacyUninstKey, throwOnMissingSubKey: false);
                    } catch {
                        // 权限不足时忽略（HKLM 需要管理员，我们可能只装了 HKCU）
                    }
                }
            }
        }

        private static void WriteFileAssociation(string exe) {
            using RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.ustx", writable: true)!;
            k.SetValue(null, "UTvTU.UstxFile");
            using RegistryKey t = Registry.CurrentUser.CreateSubKey(@"Software\Classes\UTvTU.UstxFile", writable: true)!;
            t.SetValue(null, "UTvTU Sequence File");
            using RegistryKey icon = Registry.CurrentUser.CreateSubKey(@"Software\Classes\UTvTU.UstxFile\DefaultIcon", writable: true)!;
            icon.SetValue(null, exe);
            using RegistryKey cmd = Registry.CurrentUser.CreateSubKey(@"Software\Classes\UTvTU.UstxFile\shell\open\command", writable: true)!;
            cmd.SetValue(null, $"\"{exe}\" \"%1\"");
        }

        // ── 快捷方式 ────────────────────────────────────────────────────────────

        public static string StartMenuShortcut() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");

        public static string DesktopShortcut() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");

        /// <summary>用 WScript.Shell 建 .lnk（避免 P/Invoke IShellLink 的一堆胶水）。</summary>
        public static void CreateShortcut(string lnkPath, string targetExe, string displayName) {
            Directory.CreateDirectory(Path.GetDirectoryName(lnkPath)!);
            Type? t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) {
                return;
            }
            dynamic shell = Activator.CreateInstance(t)!;
            dynamic lnk = shell.CreateShortcut(lnkPath);
            lnk.TargetPath = targetExe;
            lnk.WorkingDirectory = Path.GetDirectoryName(targetExe);
            lnk.IconLocation = targetExe;
            lnk.Description = displayName;
            lnk.Save();
        }

        /// <summary>清掉**指向本安装目录**的旧版快捷方式（OpenUtau.lnk / OpenUTAU Plus.lnk）。</summary>
        private static void RemoveLegacyShortcuts(string dir) {
            foreach (string folder in new[] {
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            }) {
                foreach (string name in new[] { "OpenUtau.lnk", "OpenUTAU Plus.lnk", "OpenUTAU-Plus.lnk" }) {
                    string p = Path.Combine(folder, name);
                    try {
                        if (File.Exists(p) && ShortcutTargets(p) is string tgt &&
                            tgt.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) {
                            File.Delete(p);
                        }
                    } catch {
                        // 忽略
                    }
                }
            }
        }

        private static string? ShortcutTargets(string lnkPath) {
            Type? t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) {
                return null;
            }
            dynamic shell = Activator.CreateInstance(t)!;
            dynamic lnk = shell.CreateShortcut(lnkPath);
            return (string?)lnk.TargetPath;
        }

        // ── 卸载 ────────────────────────────────────────────────────────────────

        public static string UserDataDir() {
            string roaming = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), UserDataFolderName);
            if (Directory.Exists(roaming)) {
                return roaming;
            }
            return Path.Combine(SelfDir, UserDataFolderName);   // 便携式：数据在安装目录旁
        }

        public static void Uninstall(string dir, bool removeData,
                                     IProgress<(int Percent, string Stage)> progress) {
            dir = Path.GetFullPath(dir);
            progress.Report((0, "删除程序文件…"));
            if (Directory.Exists(dir)) {
                foreach (string file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) {
                    if (string.Equals(file, SelfPath, StringComparison.OrdinalIgnoreCase)) {
                        continue;   // 自身稍后由 cmd 删除
                    }
                    try {
                        File.Delete(file);
                    } catch {
                        // 占用中的文件跳过
                    }
                }
            }

            progress.Report((40, "删除快捷方式…"));
            foreach (string p in new[] { StartMenuShortcut(), DesktopShortcut() }) {
                try {
                    if (File.Exists(p) && ShortcutTargets(p) is string tgt &&
                        tgt.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) {
                        File.Delete(p);
                    }
                } catch {
                    // 忽略
                }
            }

            progress.Report((60, "清理注册表…"));
            try {
                Registry.CurrentUser.DeleteSubKeyTree(UninstKey, throwOnMissingSubKey: false);
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\UTvTU.UstxFile", throwOnMissingSubKey: false);
                using RegistryKey? k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\.ustx");
                if ((k?.GetValue(null) as string) == "UTvTU.UstxFile") {
                    Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\.ustx", throwOnMissingSubKey: false);
                }
            } catch {
                // 忽略
            }

            if (removeData) {
                progress.Report((75, "删除用户数据…"));
                string data = UserDataDir();
                if (Directory.Exists(data)) {
                    try {
                        Directory.Delete(data, recursive: true);
                    } catch {
                        // 忽略
                    }
                }
            } else {
                // 保留数据目录，但清掉安装目录里属于"缓存"的部分（Backups/工程一律保留）
                foreach (string sub in KeepOnUninstall) {
                    string p = Path.Combine(dir, sub);
                    // 仅当数据目录不在这里时才删缓存（数据在安装目录旁的便携式情形要保留）
                    if (Directory.Exists(p) && !string.Equals(UserDataDir(), dir, StringComparison.OrdinalIgnoreCase)
                        && sub != "Backups") {
                        try {
                            Directory.Delete(p, recursive: true);
                        } catch {
                            // 忽略
                        }
                    }
                }
            }

            progress.Report((90, "收尾…"));
            CleanupEmptyDirs(dir);
            // ⚠ 只在"自身确实位于被卸载目录内"时自删 —— 否则从别处（例如构建输出）跑 --uninstall
            //    会把那个 exe 也删掉（实测踩过：构建产物被自己删了）。
            if (Path.GetFullPath(SelfDir).TrimEnd('\\')
                    .StartsWith(dir, StringComparison.OrdinalIgnoreCase)) {
                ScheduleSelfDelete();
            }
            progress.Report((100, "完成"));
        }

        private static void CleanupEmptyDirs(string dir) {
            if (!Directory.Exists(dir)) {
                return;
            }
            foreach (string d in Directory.GetDirectories(dir, "*", SearchOption.AllDirectories)
                         .OrderByDescending(s => s.Length)) {
                try {
                    if (!Directory.EnumerateFileSystemEntries(d).Any()) {
                        Directory.Delete(d);
                    }
                } catch {
                    // 忽略
                }
            }
            try {
                if (!Directory.EnumerateFileSystemEntries(dir).Any()) {
                    Directory.Delete(dir);
                }
            } catch {
                // 忽略
            }
        }

        /// <summary>正在运行的卸载器删不掉自己 ⇒ 交给 cmd 延迟删除。</summary>
        private static void ScheduleSelfDelete() {
            try {
                Process.Start(new ProcessStartInfo("cmd.exe",
                    $"/c ping -n 3 127.0.0.1 >nul & del /f /q \"{SelfPath}\"") {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                });
            } catch {
                // 忽略
            }
        }
    }
}

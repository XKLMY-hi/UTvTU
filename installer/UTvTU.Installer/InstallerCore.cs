using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using UTvTU.Setup;

namespace UTvTU.Installer;

public static class InstallerCore {
    public const string AppName = "UTvTU", AppExe = "OpenUtau.exe", UninstExe = "UTvTU-Uninstall.exe";
    public static SetupCommandLine Command { get; set; } = new();
    public static string SelfPath => Environment.ProcessPath ?? throw new IOException("无法定位安装程序。");
    public static string Version => typeof(InstallerCore).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "开发预览";
    public static string UserSid => WindowsIdentity.GetCurrent().User?.Value ?? throw new IOException("无法确认当前用户。");
    public static bool HasPayload { get { try { return PayloadContainer.Contains(SelfPath); } catch (IOException) { return false; } } }
    public static Stream OpenPayload() => PayloadContainer.Open(SelfPath);
    public static string LogDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UTvTU", "SetupLogs");
    public static string DefaultInstallDir() {
        if (Command.Directory != null) return Path.GetFullPath(Command.Directory);
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine }) {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 }) {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                foreach (string key in new[] { SetupRegistration.UninstallKey, SetupRegistration.LegacyKey }) {
                    using var entry = root.OpenSubKey(key);
                    if (entry == null) continue;
                    string? path = LegacyInstallLocation.Resolve(entry.GetValue("InstallLocation") as string, entry.GetValue("DisplayIcon") as string, entry.GetValue("UninstallString") as string, File.Exists);
                    if (path == null) continue;
                    try {
                        var receipt = SetupEngine.ReadReceipt(path);
                        if (receipt != null ? receipt.UserSid == UserSid : SetupEngine.IsUtvtu(path)) return SetupPaths.Full(path);
                    } catch { }
                }
            }
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);
    }
    public static bool IsElevated() { using var identity = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); }
    public static bool NeedsElevation(string dir) {
        string target = SetupPaths.ValidateTarget(dir);
        return new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }.Select(Environment.GetFolderPath).Where(path => path.Length > 0).Any(path => SetupPaths.Within(path, target));
    }
    public static SetupOptions Options(string directory, bool desktop, bool associations, bool removeData) => new() { Directory = SetupPaths.ValidateTarget(directory), UserSid = UserSid, DataDirectory = SetupPaths.UserDataDirectory, DesktopShortcut = desktop, FileAssociation = associations, RemoveData = removeData, Uninstall = Command.Uninstall, Silent = Command.Silent };

    public static async Task<SetupResult> RunAsync(SetupOptions options, IProgress<SetupProgress> progress, CancellationToken token) {
        if (Command.Preview) throw new InvalidOperationException("预览模式不能执行安装或卸载。");
        using var lease = new SetupOperationLease(options.Directory, options.UserSid);
        return await RunOperationAsync(options, progress, token);
    }
    static async Task<SetupResult> RunOperationAsync(SetupOptions options, IProgress<SetupProgress> progress, CancellationToken token) {
        options = options with { CleanLegacyMachineRegistration = WindowsIntegration.HasLegacyMachineRegistration(options.Directory) };
        var originalReceipt = options.Uninstall ? SetupEngine.ReadReceipt(options.Directory) : null;
        if (options.Uninstall && originalReceipt == null) throw new IOException("缺少可验证的安装记录。旧版请先覆盖安装新版，再使用新版卸载器。");
        bool legacy = SetupEngine.IsUtvtu(options.Directory);
        SetupResult result;
        if ((NeedsElevation(options.Directory) || options.CleanLegacyMachineRegistration) && !IsElevated()) result = await RunElevatedFiles(options, progress, token);
        else {
            try { result = await Task.Run(() => RunFiles(options, progress, token), token); }
            catch (UnauthorizedAccessException) when (!IsElevated()) { result = await RunElevatedFiles(options, progress, token); }
        }
        // Only the original user process touches HKCU/shortcuts or launches the app.
        progress.Report(new(96, options.Uninstall ? "清理当前用户的系统入口" : "注册当前用户的系统入口"));
        var warnings = result.Warnings.ToList();
        bool integrationPending = false;
        if (options.Uninstall && options.RemoveData && originalReceipt?.PortableData == false) {
            // Profile data is handled with the original user's rights. The elevated
            // worker never follows a caller-provided path into another user's profile.
            try { SetupEngine.DeleteUserData(originalReceipt, options); }
            catch (Exception error) { warnings.Add("部分本机用户数据已保留：" + error.Message); }
        }
        try {
            await OnSta(() => {
                var integration = new WindowsIntegration();
                if (options.Uninstall) integration.Uninstall(options, originalReceipt!);
                else integration.Install(options, SetupEngine.ReadReceipt(options.Directory)!, legacy);
            });
        } catch (Exception error) { integrationPending = true; warnings.Add("程序文件操作已完成，但系统入口未全部更新：" + error.Message); }
        progress.Report(new(100, warnings.Count == 0 ? "完成" : "完成，部分项目需要处理"));
        return new(true, options.Uninstall ? "卸载完成" : "安装完成", warnings, integrationPending);
    }
    public static SetupResult RunFiles(SetupOptions options, IProgress<SetupProgress>? progress, CancellationToken token) {
        var engine = new SetupEngine();
        bool legacy = options.CleanLegacyMachineRegistration && WindowsIntegration.HasLegacyMachineRegistration(options.Directory);
        var fileOptions = options;
        if (options.Uninstall && options.RemoveData && SetupEngine.ReadReceipt(options.Directory)?.PortableData != true) fileOptions = options with { RemoveData = false };
        var result = options.Uninstall ? engine.Uninstall(fileOptions, progress, token) : engine.Install(OpenPayload(), options, progress, token);
        if (legacy) {
            try { WindowsIntegration.CleanLegacyMachineRegistration(options.Directory); }
            catch (Exception error) { return result with { Warnings = result.Warnings.Append("旧版系统登记未清理：" + error.Message).ToArray() }; }
        }
        return result;
    }
    static Task OnSta(Action action) {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { action(); completion.SetResult(); } catch (Exception error) { completion.SetException(error); } }) { IsBackground = true, Name = "Setup shell integration" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
    sealed class WorkerRequest { public SetupOptions Options { get; set; } = new(); }
    static async Task<SetupResult> RunElevatedFiles(SetupOptions options, IProgress<SetupProgress> progress, CancellationToken token) {
        string job = Path.Combine(Path.GetTempPath(), "UTvTU-Setup-Jobs", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(job);
        SetupPaths.RejectLinks(job); SetupJson.Write(Path.Combine(job, "request.json"), new WorkerRequest { Options = options });
        var start = new ProcessStartInfo(SelfPath) { UseShellExecute = true, Verb = "runas" };
        start.ArgumentList.Add("--worker-job"); start.ArgumentList.Add(job);
        progress.Report(new(1, "等待 Windows 授权", "授权后继续当前任务，已选选项保持不变。"));
        Process process;
        try { process = Process.Start(start) ?? throw new IOException("无法启动授权进程。"); }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223) { throw new OperationCanceledException("已取消管理员授权。", error, token); }
        using (process) {
            using var cancel = token.Register(() => { try { File.WriteAllText(Path.Combine(job, "cancel"), "cancel"); } catch { } });
            while (!process.HasExited) {
                string update = Path.Combine(job, "progress.json");
                try { if (File.Exists(update)) progress.Report(SetupJson.Read<SetupProgress>(update)); } catch (IOException) { } catch (System.Text.Json.JsonException) { }
                await Task.Delay(120); process.Refresh();
            }
            string response = Path.Combine(job, "result.json");
            if (!File.Exists(response)) throw new IOException("授权进程未返回操作结果。日志目录：" + LogDirectory);
            var result = SetupJson.Read<SetupResult>(response);
            if (!result.Success) {
                if (File.Exists(Path.Combine(job, "cancel"))) throw new OperationCanceledException(token);
                throw new IOException(result.Message);
            }
            return result;
        }
    }
    public static int RunWorker(string job) {
        if (Path.GetFileName(Path.GetDirectoryName(SetupPaths.Full(job))) != "UTvTU-Setup-Jobs" || !Guid.TryParseExact(Path.GetFileName(SetupPaths.Full(job)), "N", out _)) throw new IOException("授权任务目录无效。");
        SetupPaths.RejectLinks(job);
        var request = SetupJson.Read<WorkerRequest>(Path.Combine(job, "request.json"));
        string? owner = new DirectoryInfo(job).GetAccessControl().GetOwner(typeof(SecurityIdentifier))?.Value;
        bool ownAdminJob = owner == new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value && request.Options.UserSid == UserSid && IsElevated();
        if (owner != request.Options.UserSid && !ownAdminJob) throw new IOException("授权任务与原用户不匹配。");
        using var stop = new CancellationTokenSource();
        using var poll = new Timer(_ => { try { if (File.Exists(Path.Combine(job, "cancel"))) stop.Cancel(); } catch (ObjectDisposedException) { } }, null, 0, 100);
        try {
            var result = RunFiles(request.Options, new DirectProgress(p => { try { SetupJson.Write(Path.Combine(job, "progress.json"), p); } catch (IOException) { } }), stop.Token);
            SetupJson.Write(Path.Combine(job, "result.json"), result); return 0;
        } catch (Exception error) {
            SetupJson.Write(Path.Combine(job, "result.json"), new SetupResult(false, error.Message, Array.Empty<string>())); return 1;
        }
    }
    public sealed class DirectProgress(Action<SetupProgress> action) : IProgress<SetupProgress> { public void Report(SetupProgress value) => action(value); }

    public static async Task RetryIntegrationAsync(SetupOptions options, InstallReceipt receipt, bool legacy) {
        using var lease = new SetupOperationLease(options.Directory, options.UserSid);
        var current = SetupEngine.ReadReceipt(options.Directory);
        if (options.Uninstall ? current != null : current == null || current.Version != receipt.Version || current.SourceCommit != receipt.SourceCommit) throw new IOException("此目录的安装状态已变化，请使用当前版本的管理入口。");
        await OnSta(() => { var integration = new WindowsIntegration(); if (options.Uninstall) integration.Uninstall(options, receipt); else integration.Install(options, receipt, legacy); });
    }
}

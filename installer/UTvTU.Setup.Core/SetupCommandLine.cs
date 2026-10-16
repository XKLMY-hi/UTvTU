namespace UTvTU.Setup;

public sealed record SetupCommandLine {
    public bool Uninstall { get; init; }
    public bool Silent { get; init; }
    public bool DesktopShortcut { get; init; } = true;
    public bool FileAssociation { get; init; } = true;
    public bool RemoveData { get; init; }
    public bool Preview { get; init; }
    public bool Relocated { get; init; }
    public string? Directory { get; init; }
    public string? WorkerJob { get; init; }
    public static SetupCommandLine Parse(string[] args) {
        var result = new SetupCommandLine();
        for (int i = 0; i < args.Length; i++) {
            string Value() => ++i < args.Length && !args[i].StartsWith("--") ? args[i] : throw new ArgumentException($"{args[i - 1]} 缺少参数。");
            result = args[i] switch {
                "--uninstall" => result with { Uninstall = true },
                "--silent" or "--quiet" => result with { Silent = true },
                "--no-desktop" => result with { DesktopShortcut = false },
                "--no-association" => result with { FileAssociation = false },
                "--remove-data" => result with { RemoveData = true },
                "--preview" => result with { Preview = true },
                "--relocated" => result with { Relocated = true },
                "--dir" => result with { Directory = Value() },
                "--worker-job" => result with { WorkerJob = Value(), Silent = true },
                _ => throw new ArgumentException($"无法识别的参数：{args[i]}")
            };
        }
        if (result.Preview && (result.Silent || result.WorkerJob != null)) throw new ArgumentException("预览不能执行安装任务。");
        return result;
    }
    public IReadOnlyList<string> Arguments() {
        var args = new List<string>();
        if (Uninstall) args.Add("--uninstall");
        if (Silent) args.Add("--silent");
        if (!DesktopShortcut) args.Add("--no-desktop");
        if (!FileAssociation) args.Add("--no-association");
        if (RemoveData) args.Add("--remove-data");
        if (Preview) args.Add("--preview");
        if (Relocated) args.Add("--relocated");
        if (Directory != null) { args.Add("--dir"); args.Add(Directory); }
        if (WorkerJob != null) { args.Add("--worker-job"); args.Add(WorkerJob); }
        return args;
    }
}

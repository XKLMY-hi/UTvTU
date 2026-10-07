using System.Reflection;

namespace OpenUtau.Core {
    /// <summary>
    /// Plus 版本信息 —— 独立于上游主线版本递增。
    ///
    /// 版本号规则（2026-10 用户裁决）：**构建时刻版本号** `UTvTU-&lt;yy.M.d&gt;-&lt;HHmmss&gt;`，
    /// 由 csproj 的 `SetBuildStamp` 目标写进 `InformationalVersion`（每次构建刷新）。
    /// 取不到 stamp 时回落到旧的 `UTvTU v{上游版本} p{Plus版本}` 形式，保证任何非标准构建也有可读版本串。
    /// </summary>
    public static class PlusInfo {
        /// <summary>Plus 版本号（上游兼容口径，仍随功能迭代手工递增；显示层已改用构建时刻版本号）。</summary>
        public const string PlusVersion = "0.0.3";

        /// <summary>产品显示名。</summary>
        public const string DisplayName = "UTvTU";

        /// <summary>完整版本串（窗口标题 / 状态条 / 偏好设置版本行 / 日志共用一处，避免多份格式串漂移）。</summary>
        public static string VersionString {
            get {
                string? stamp = Assembly.GetEntryAssembly()
                    ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(stamp) && stamp.StartsWith("UTvTU-", System.StringComparison.Ordinal)) {
                    return stamp;
                }
                return $"{DisplayName} v{Assembly.GetEntryAssembly()?.GetName().Version} p{PlusVersion}";
            }
        }
    }
}

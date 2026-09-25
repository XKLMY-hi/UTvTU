namespace OpenUtau.Core {
    /// <summary>
    /// Plus 版本信息——独立于上游主线版本递增。
    /// 版本号规则：UTvTU v{上游主线版本} p{Plus版本}
    /// （上游版本来自 csproj Version，与 openutau/OpenUtau 同步时更新；
    /// Plus 版本在此手工递增）。
    /// </summary>
    public static class PlusInfo {
        /// <summary>Plus 版本号。</summary>
        public const string PlusVersion = "0.0.3";

        /// <summary>产品显示名。</summary>
        public const string DisplayName = "UTvTU";

        /// <summary>完整版本串（窗口标题 / 状态条 / 偏好设置版本行共用一处，避免多份格式串漂移）。</summary>
        public static string VersionString =>
            $"{DisplayName} v{System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version} p{PlusVersion}";
    }
}

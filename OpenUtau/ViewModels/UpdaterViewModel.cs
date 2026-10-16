using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media;
using Newtonsoft.Json;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace OpenUtau.App.ViewModels {
    public class UpdaterViewModel : ViewModelBase {
        internal class GithubRelease {
#pragma warning disable 0649
            public string html_url = string.Empty;
            public long id = long.MaxValue;
            public bool draft;
            public bool prerelease;
            public string tag_name = string.Empty;
            public string name = string.Empty;
#pragma warning restore 0649
        }

        // 版本号规则：v{上游主线版本} p{Plus版本}
        public string AppVersion => PlusInfo.VersionString;
        public bool IsDarkMode => ThemeManager.IsDarkMode;
        [Reactive] public string UpdaterStatus { get; set; }
        [Reactive] public bool UpdateAvailable { get; set; }
        [Reactive] public FontWeight UpdateButtonFontWeight { get; set; }
        public Action? CloseApplication { get; set; }

        // Cached result from startup check, consumed by dialog if available.
        private static string? s_latestVersion;
        private static string? s_releaseUrl;
        private string? latestVersion;
        private string? releaseUrl;

        public UpdaterViewModel() {
            UpdaterStatus = string.Empty;
            UpdateAvailable = false;
            UpdateButtonFontWeight = FontWeight.Normal;
        }

        static readonly SemaphoreSlim checkGate = new(1);
        static bool checkFailed;

        internal static bool TryParseBuildTag(string tag, out DateTime stamp) {
            stamp = default;
            var match = Regex.Match(tag ?? "", @"\AUTvTU-(\d{2})\.(\d{1,2})\.(\d{1,2})-(\d{2})(\d{2})(\d{2})(?:\+[^\s]+)?\z", RegexOptions.CultureInvariant);
            if (!match.Success) return false;
            try {
                int N(int index) => int.Parse(match.Groups[index].Value, CultureInfo.InvariantCulture);
                stamp = new DateTime(2000 + N(1), N(2), N(3), N(4), N(5), N(6));
                return true;
            } catch (ArgumentOutOfRangeException) { return false; }
        }

        internal static GithubRelease? FindNewerRelease(IEnumerable<GithubRelease> releases, string currentTag, Version? legacyVersion = null) {
            var eligible = releases.Where(r => !r.draft && !r.prerelease &&
                Uri.TryCreate(r.html_url, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
                uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
                uri.AbsolutePath.StartsWith("/XKLMY-hi/UTvTU/releases/tag/", StringComparison.OrdinalIgnoreCase));
            if (TryParseBuildTag(currentTag, out var current)) {
                return eligible.Select(r => (release: r, valid: TryParseBuildTag(r.tag_name, out var time), time))
                    .Where(r => r.valid && r.time > current).OrderByDescending(r => r.time).Select(r => r.release).FirstOrDefault();
            }
            // Unstamped developer builds cannot be ordered against dated release builds.
            if (legacyVersion == null) return null;
            return eligible.Where(r => TryParsePlusVersion(r.tag_name.TrimStart('v'), out var version) && version > legacyVersion)
                .OrderByDescending(r => { TryParsePlusVersion(r.tag_name.TrimStart('v'), out var version); return version; }).FirstOrDefault();
        }

        public static async Task<bool> CheckForUpdateAsync() {
            await checkGate.WaitAsync();
            try {
                s_latestVersion = null; s_releaseUrl = null; checkFailed = false;
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                client.DefaultRequestHeaders.Add("User-Agent", "UTvTU");
                using var response = await client.GetAsync("https://api.github.com/repos/XKLMY-hi/UTvTU/releases?per_page=100");
                response.EnsureSuccessStatusCode();
                var releases = JsonConvert.DeserializeObject<List<GithubRelease>>(await response.Content.ReadAsStringAsync());
                var latest = FindNewerRelease(releases ?? new(), PlusInfo.VersionString,
                    System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version);
                if (latest == null) return false;
                s_latestVersion = latest.tag_name; s_releaseUrl = latest.html_url;
                return true;
            } catch (Exception e) {
                checkFailed = true;
                Log.Warning(e, "[Updater] Failed to check for update.");
                return false;
            } finally { checkGate.Release(); }
        }

        /// <summary>
        /// Called when the dialog is shown. If the startup check already
        /// found an update, use that; otherwise run a fresh check.
        /// </summary>
        public async void Init() {
            UpdateAvailable = false;
            UpdateButtonFontWeight = FontWeight.Normal;
            UpdaterStatus = ThemeManager.GetString("updater.status.checking");

            // If startup check already cached a result, use it
            if (s_latestVersion != null) {
                latestVersion = s_latestVersion;
                releaseUrl = s_releaseUrl;
                UpdaterStatus = string.Format(ThemeManager.GetString("updater.status.available"), latestVersion);
                UpdateAvailable = true;
                UpdateButtonFontWeight = FontWeight.Bold;
                return;
            }

            // Run a fresh check
            bool hasUpdate = await CheckForUpdateAsync();
            if (hasUpdate && s_latestVersion != null) {
                latestVersion = s_latestVersion;
                releaseUrl = s_releaseUrl;
                UpdaterStatus = string.Format(ThemeManager.GetString("updater.status.available"), latestVersion);
                UpdateAvailable = true;
                UpdateButtonFontWeight = FontWeight.Bold;
            } else if (!hasUpdate) {
                UpdaterStatus = ThemeManager.GetString(checkFailed ? "updater.status.unknown" : "updater.status.notavailable");
            } else {
                UpdaterStatus = ThemeManager.GetString("updater.status.unknown");
            }
        }

        public void OnGithub() {
            try {
                string url = releaseUrl ?? "https://github.com/XKLMY-hi/UTvTU/releases";
                OS.OpenWeb(url);
            } catch (Exception e) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        public void OnUpdate() {
            // Open the releases page — user downloads manually
            OnGithub();
        }

        public void OnClosing() {
            // No-op: we don't track skipped versions for simplicity
        }

        /// <summary>
        /// Parse a Plus version tag like "0.1.568-plus.0.0.1-beta" or "0.1.568"
        /// into a .NET Version (major.minor.build.revision).
        /// </summary>
        static bool TryParsePlusVersion(string tag, out Version ver) {
            ver = new Version(0, 0);
            // Split off suffix labels (everything after the first alpha segment)
            // "0.1.568-plus.0.0.1-beta" → parts before "-plus" = "0.1.568", then "-plus.0.0.1" → revision=1
            int plusIdx = tag.IndexOf("-plus", StringComparison.OrdinalIgnoreCase);
            string basePart = plusIdx >= 0 ? tag[..plusIdx] : tag;
            int revision = 0;

            if (plusIdx >= 0) {
                string afterPlus = tag[(plusIdx + 5)..]; // skip "-plus"
                if (afterPlus.StartsWith(".")) afterPlus = afterPlus[1..];
                // Extract the last numeric segment as revision
                // "0.0.1-beta" → split by non-digit, last numeric is 1
                int dash = afterPlus.IndexOf('-');
                string numPart = dash >= 0 ? afterPlus[..dash] : afterPlus;
                var segments = numPart.Split('.');
                if (segments.Length > 0 && int.TryParse(segments[^1], out int r))
                    revision = r;
            }

            // Parse the base ("0.1.568") and add revision
            if (!Version.TryParse(basePart, out var baseVer)) return false;
            ver = new Version(
                Math.Max(0, baseVer.Major),
                Math.Max(0, baseVer.Minor),
                Math.Max(0, baseVer.Build >= 0 ? baseVer.Build : 0),
                revision);
            return true;
        }
    }
}

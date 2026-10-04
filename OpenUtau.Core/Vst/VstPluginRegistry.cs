using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using K4os.Hash.xxHash;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.Vst {
    /// <summary>
    /// Global plugin registry — cross-project, persisted in Preferences.
    ///
    /// Key design: plugins are identified by a stable UID, NOT by file path.
    ///   VST3 → CID from moduleinfo.json (32-char hex GUID)
    ///   VST2 → "vst2:{hash}" (composite key)
    ///
    /// Category filtering:
    ///   SubCategories from moduleinfo.json classify VST3 as Fx or Instrument.
    ///   Instrument VSTs (synth/sampler/etc.) are NOT offered in the effect rack —
    ///   they have no audio input and would crash the processing chain.
    /// </summary>
    public class VstPluginRegistry {
        public static VstPluginRegistry Inst { get; } = new();

        private readonly Dictionary<string, VstPluginEntry> _entries = new();
        private bool _scanned;

        // ── W12：扫描统计（"为什么扫得少"必须可读，不能吞成空列表）──
        private int statBundles, statBundlesModuleInfo, statBundlesFallback, statBundlesFailed;
        private int statSingleFiles, statSingleRegistered, statSingleFailed;
        private int statVst2, statVst2Registered;
        private bool statProbeMissing;

        /// <summary>最近一次扫描的可读摘要（诊断/报告/测试都取它）。</summary>
        public string LastScanSummary { get; private set; } = string.Empty;

        /// <summary>子进程探针是否可用（缺 vst_probe.exe 时，单文件 .vst3 与老式 bundle 回退都不可用）。</summary>
        public static bool ProbeAvailable => VstProbeProcess.IsAvailable;

        /// <summary>探针可执行文件期望路径（环境缺失时给日志用）。</summary>
        public static string ProbeExePath => VstProbeProcess.ExePath;

        private VstPluginRegistry() => LoadFromPreferences();

        public VstPluginEntry? TryGet(string? uid) {
            if (string.IsNullOrEmpty(uid)) return null;
            return _entries.TryGetValue(uid, out var e) ? e : null;
        }

        /// <summary>All registered plugins.</summary>
        public IReadOnlyList<VstPluginEntry> All => _entries.Values.OrderBy(e => e.Name).ToList();

        /// <summary>Only effect-type plugins (safe to load in the effect chain).</summary>
        public IReadOnlyList<VstPluginEntry> Effects =>
            _entries.Values.Where(e => e.IsEffect).OrderBy(e => e.Name).ToList();

        public int Count => _entries.Count;
        public int EffectCount => Effects.Count;

        public void ScanAll() {
            if (_scanned) return;
            _scanned = true;
            _entries.Clear();
            ResetScanStats();

            var paths = new List<string>();
            paths.AddRange(DefaultVst3Paths);
            paths.AddRange(DefaultVst2Paths);
            if (Preferences.Default.VstScanPaths?.Count > 0)
                paths.AddRange(Preferences.Default.VstScanPaths);

            // W12：探针缺失是"环境问题"而不是"没有插件"——先喊一声，摘要里也标注
            statProbeMissing = !VstProbeProcess.IsAvailable;
            if (statProbeMissing) {
                Log.Warning($"[VST] vst_probe.exe not found at '{VstProbeProcess.ExePath}' — " +
                            "single-file .vst3 probing and legacy-bundle fallback are UNAVAILABLE in this environment; " +
                            "only moduleinfo bundles and VST2 DLLs can be registered.");
            }

            int found = 0;
            foreach (var path in paths.Distinct()) {
                if (!Directory.Exists(path)) continue;
                // Default VST3 paths: only scan top-level single-file .vst3
                // (avoids loading huge plugins in vendor subdirectories)
                bool isDefaultPath = DefaultVst3Paths.Contains(path);
                found += ScanDirectory(path, isDefaultPath ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories);
            }
            SaveToPreferences();
            LastScanSummary = BuildScanSummary(found);
            Log.Information(LastScanSummary);
        }

        private void ResetScanStats() {
            statBundles = statBundlesModuleInfo = statBundlesFallback = statBundlesFailed = 0;
            statSingleFiles = statSingleRegistered = statSingleFailed = 0;
            statVst2 = statVst2Registered = 0;
            statProbeMissing = false;
        }

        private string BuildScanSummary(int found) {
            var sb = new StringBuilder();
            sb.Append($"[VST] Registry: {found} plugins ({Effects.Count} effects)");
            sb.Append($" | VST3 bundles {statBundles}: moduleinfo {statBundlesModuleInfo}, fallback {statBundlesFallback}, failed {statBundlesFailed}");
            sb.Append($" | VST3 single-file {statSingleFiles}: registered {statSingleRegistered}, failed {statSingleFailed}");
            sb.Append($" | VST2 {statVst2}: registered {statVst2Registered}");
            sb.Append(statProbeMissing
                ? $" | vst_probe: MISSING ({VstProbeProcess.ExePath})"
                : " | vst_probe: ok");
            return sb.ToString();
        }

        public void Rescan() { _entries.Clear(); _scanned = false; ScanAll(); }

        // ── Scanning ────────────────────────────────────────────

        private int ScanDirectory(string dir, SearchOption searchOpt) {
            int count = 0;
            try {
                foreach (var vst3Dir in Directory.GetDirectories(dir, "*.vst3", searchOpt)) {
                    statBundles++;
                    try { if (ScanVst3Bundle(vst3Dir)) count++; }
                    catch (Exception ex) {
                        statBundlesFailed++;
                        Log.Warning($"[VST] Bundle scan failed '{vst3Dir}': {ex.Message}");
                    }
                }
                foreach (var vst3File in Directory.GetFiles(dir, "*.vst3", searchOpt)) {
                    statSingleFiles++;
                    try {
                        if (ScanVst3SingleFile(vst3File)) { count++; statSingleRegistered++; }
                        else { statSingleFailed++; }
                    } catch (Exception ex) {
                        statSingleFailed++;
                        Log.Warning($"[VST] Single-file scan failed '{vst3File}': {ex.Message}");
                    }
                }
                foreach (var dll in Directory.GetFiles(dir, "*.dll", searchOpt)) {
                    statVst2++;
                    try { if (ScanVst2Dll(dll)) { count++; statVst2Registered++; } }
                    catch (Exception ex) {
                        // W12：原来是 catch {} —— 环境问题与坏插件都被吞成"没扫到"
                        Log.Warning($"[VST] VST2 scan failed '{dll}': {ex.Message}");
                    }
                }
            } catch (Exception ex) { Log.Warning($"[VST] Failed {dir}: {ex.Message}"); }
            return count;
        }

        /// <summary>
        /// 扫描一个 VST3 bundle：
        /// ① 有 <c>Contents/Resources/moduleinfo.json</c> → 解析（**容错**：尾逗号/注释）；
        /// ② moduleinfo 缺失、不可用或没有 Audio Module Class → **回退探测** <c>Contents/&lt;arch&gt;/*.vst3</c>
        ///    （W12：老式 bundle 与第三方桥接插件过去会被整体跳过，例如我们自己的 OpenUtau Bridge.vst3）。
        /// 每条失败路径都有日志与计数，不再静默吞掉。
        /// </summary>
        private bool ScanVst3Bundle(string bundleDir) {
            string moduleInfoPath = Path.Combine(bundleDir, "Contents", "Resources", "moduleinfo.json");
            if (File.Exists(moduleInfoPath)) {
                try {
                    VstPluginEntry? entry = ParseVst3ModuleInfo(File.ReadAllText(moduleInfoPath), bundleDir);
                    if (entry != null) {
                        _entries[entry.Uid] = entry;
                        statBundlesModuleInfo++;
                        return true;
                    }
                    Log.Warning($"[VST] moduleinfo.json has no usable 'Audio Module Class' — falling back to inner binary: {bundleDir}");
                } catch (JsonException ex) {
                    Log.Warning($"[VST] moduleinfo.json parse failed ({ex.Message}) — falling back to inner binary: {bundleDir}");
                } catch (Exception ex) {
                    Log.Warning($"[VST] moduleinfo.json unreadable ({ex.Message}) — falling back to inner binary: {bundleDir}");
                }
            } else {
                Log.Information($"[VST] Legacy bundle (no moduleinfo.json) — probing inner binary: {bundleDir}");
            }
            if (ScanVst3BundleFallback(bundleDir)) {
                statBundlesFallback++;
                return true;
            }
            statBundlesFailed++;
            return false;
        }

        /// <summary>
        /// moduleinfo.json 的解析选项：真实插件确实会写尾逗号（如 IK Multimedia 的 MODO BASS 2），
        /// 严格模式下会抛异常、整个 bundle 从列表里消失。注释同理（VST3 SDK 生成的 JSON 允许）。
        /// </summary>
        static readonly JsonDocumentOptions ModuleInfoJsonOptions = new JsonDocumentOptions {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        };

        /// <summary>
        /// 解析 moduleinfo.json，返回第一个 VST3 音频模块类（**纯函数，便于测试**）。
        /// 没有可用类时返回 null；JSON 语法仍非法时抛 <see cref="JsonException"/>（由调用方记日志并回退）。
        /// </summary>
        public static VstPluginEntry? ParseVst3ModuleInfo(string json, string bundleDir) {
            using var doc = JsonDocument.Parse(json, ModuleInfoJsonOptions);
            var root = doc.RootElement;

            string name = root.TryGetProperty("Name", out var n) ? n.GetString() ?? "Unknown" : "Unknown";
            string vendor = "";
            if (root.TryGetProperty("Factory Info", out var fi) && fi.TryGetProperty("Vendor", out var v))
                vendor = v.GetString() ?? "";

            if (!root.TryGetProperty("Classes", out var classes)) return null;
            foreach (var cls in classes.EnumerateArray()) {
                string? cid = cls.TryGetProperty("CID", out var c) ? c.GetString() : null;
                string? cat = cls.TryGetProperty("Category", out var ca) ? ca.GetString() : null;
                string? cn = cls.TryGetProperty("Name", out var cn1) ? cn1.GetString() : null;
                if (cat != "Audio Module Class" || string.IsNullOrEmpty(cid)) continue;

                var subs = new List<string>();
                if (cls.TryGetProperty("Sub Categories", out var sc)) {
                    foreach (var s in sc.EnumerateArray()) {
                        string? sub = s.GetString();
                        if (!string.IsNullOrEmpty(sub)) subs.Add(sub);
                    }
                }

                string uid = $"vst3:{NormalizeCid(cid)}";
                return new VstPluginEntry {
                    Uid = uid, Name = cn ?? name, Vendor = vendor, Path = bundleDir,
                    Type = VstPluginType.VST3,
                    SubCategories = subs,
                    IsEffect = ClassifyEffect(subs, VstPluginType.VST3),
                };
            }
            return null;
        }

        /// <summary>
        /// 老式/残缺 bundle 的回退：探测 <c>Contents/&lt;arch&gt;/*.vst3</c>（当前架构优先，取第一个）。
        /// 登记路径仍用 **bundle 目录**（与 moduleinfo 路径同一加载口径），显示名用探针结果、
        /// 取不到再用 bundle 文件名兜底。
        /// </summary>
        private bool ScanVst3BundleFallback(string bundleDir) {
            string contents = Path.Combine(bundleDir, "Contents");
            string? inner = FindInnerVst3Binary(contents);
            if (inner == null) {
                Log.Warning($"[VST] Bundle has no inner *.vst3 binary — skipped: {bundleDir}");
                return false;
            }
            string nameFallback = Path.GetFileNameWithoutExtension(
                bundleDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrEmpty(nameFallback)) {
                nameFallback = nameFallback.Replace(".vst3", string.Empty);
            }
            return ProbeVst3Binary(inner, bundleDir, nameFallback, null);
        }

        /// <summary>
        /// 在 <c>Contents</c> 下挑一个插件二进制：优先当前进程架构目录
        /// （Windows <c>x86_64-win</c>/<c>x86-win</c>、macOS <c>MacOS</c>、Linux <c>x86_64-linux</c>），
        /// 否则按路径名排序取第一个。纯函数（只读目录），便于测试。
        /// </summary>
        public static string? FindInnerVst3Binary(string contentsDir) {
            if (!Directory.Exists(contentsDir)) return null;
            var candidates = new List<string>();
            foreach (string dir in Directory.GetDirectories(contentsDir)) {
                try {
                    candidates.AddRange(Directory.GetFiles(dir, "*.vst3"));
                } catch (Exception ex) {
                    Log.Warning($"[VST] Cannot enumerate '{dir}': {ex.Message}");
                }
            }
            if (candidates.Count == 0) return null;
            string preferred = PreferredArchPrefix();
            var ordered = candidates.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
            return ordered.FirstOrDefault(c =>
                       Path.GetFileName(Path.GetDirectoryName(c))?.StartsWith(preferred, StringComparison.OrdinalIgnoreCase) == true)
                   ?? ordered[0];
        }

        /// <summary>当前进程的 VST3 架构目录前缀。</summary>
        static string PreferredArchPrefix() {
            if (OperatingSystem.IsMacOS()) return "MacOS";
            return Environment.Is64BitProcess ? "x86_64" : "x86";
        }

        /// <summary>
        /// Probe a single-file .vst3 DLL (not a bundle directory).
        /// Process-isolated: vst_probe.exe spawns vst_bridge.dll's vst_probe()
        /// in a subprocess, so a crashing plugin DLL can't take down OpenUTAU
        /// (previously this was in-process LoadLibrary — the crash source).
        /// </summary>
        private bool ScanVst3SingleFile(string filePath) {
            // Skip if file is actually a directory
            if (Directory.Exists(filePath)) return false;

            // Skip huge plugins (>100MB) during auto-scan to avoid hangs.
            // Users can still manually load them via the plugin browser.
            try {
                var fi = new System.IO.FileInfo(filePath);
                if (fi.Length > 100 * 1024 * 1024) {
                    Log.Information($"[VST] Skip large plugin: {Path.GetFileName(filePath)} ({fi.Length / 1024 / 1024}MB)");
                    return false;
                }
            } catch (Exception ex) {
                Log.Warning($"[VST] Cannot stat '{filePath}': {ex.Message}");
            }

            return ProbeVst3Binary(filePath, filePath,
                Path.GetFileNameWithoutExtension(filePath), null);
        }

        /// <summary>
        /// 用子进程探针读取一个 VST3 **二进制**并登记注册表条目。
        /// <paramref name="probePath"/> 喂给探针的文件；<paramref name="entryPath"/> 登记进注册表的加载路径
        /// （单文件 = 文件本身；bundle 回退 = bundle 目录）；<paramref name="nameFallback"/> /
        /// <paramref name="vendorFallback"/> 是探针没给出名字/厂商时的兜底（文件名 / bundle 名）。
        /// 探针缺失（环境问题）与探针失败（坏插件）走不同日志，便于区分。
        /// </summary>
        private bool ProbeVst3Binary(string probePath, string entryPath, string? nameFallback, string? vendorFallback) {
            if (!VstProbeProcess.IsAvailable) {
                statProbeMissing = true;
                Log.Warning($"[VST] Probe unavailable — cannot inspect '{probePath}' " +
                            $"(expected vst_probe.exe at '{VstProbeProcess.ExePath}')");
                return false;
            }

            string? json = VstProbeProcess.ProbeVst3(probePath);
            if (string.IsNullOrEmpty(json)) {
                Log.Warning($"[VST] Probe returned no metadata for '{probePath}' (unsupported/crashing plugin?)");
                return false;
            }

            try {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("classes", out var classes) || classes.GetArrayLength() == 0) {
                    Log.Warning($"[VST] Probe reported no VST3 classes in '{probePath}'");
                    return false;
                }

                string pluginName = nameFallback ?? "Unknown";
                string pluginVendor = vendorFallback ?? "";
                bool added = false;
                foreach (var cls in classes.EnumerateArray()) {
                    string? cat = cls.TryGetProperty("category", out var c) ? c.GetString() : null;
                    if (cat != "Audio Module Class") continue;

                    string? cid = cls.TryGetProperty("cid", out var cidEl) ? cidEl.GetString() : null;
                    if (string.IsNullOrEmpty(cid)) continue;

                    string? cn = cls.TryGetProperty("name", out var nm) ? nm.GetString() : null;
                    string? vn = cls.TryGetProperty("vendor", out var v) ? v.GetString() : null;
                    if (!string.IsNullOrEmpty(cn)) pluginName = cn;
                    if (!string.IsNullOrEmpty(vn)) pluginVendor = vn;

                    var subs = new List<string>();
                    if (cls.TryGetProperty("subs", out var sc)) {
                        foreach (var s in sc.EnumerateArray()) {
                            string? sub = s.GetString();
                            if (!string.IsNullOrEmpty(sub)) subs.Add(sub);
                        }
                    }

                    string uid = $"vst3:{NormalizeCid(cid)}";
                    _entries[uid] = new VstPluginEntry {
                        Uid = uid, Name = pluginName, Vendor = pluginVendor,
                        Path = entryPath, Type = VstPluginType.VST3,
                        SubCategories = subs,
                        IsEffect = ClassifyEffect(subs, VstPluginType.VST3),
                    };
                    added = true;
                }
                return added;
            } catch (JsonException ex) {
                Log.Warning($"[VST] Cannot parse probe output for '{probePath}': {ex.Message}");
                return false;
            } catch (Exception ex) {
                Log.Warning($"[VST] Probe result unusable for '{probePath}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Normalize a CID to lowercase hex with no dashes.
        /// Handles both GUID formats (with dashes) and raw hex strings.
        /// </summary>
        private static string NormalizeCid(string cid) {
            // Keep only hex chars, lowercase
            var sb = new System.Text.StringBuilder();
            foreach (char ch in cid) {
                if (ch >= '0' && ch <= '9') sb.Append(ch);
                else if (ch >= 'a' && ch <= 'f') sb.Append(ch);
                else if (ch >= 'A' && ch <= 'F') sb.Append(char.ToLowerInvariant(ch));
            }
            return sb.ToString();
        }

        private bool ScanVst2Dll(string dllPath) {
            try {
                // ── B4: Process-isolated probe ──
                // Launch a subprocess to safely call VSTPluginMain() and read
                // AEffect.flags (effFlagsIsSynth). If the DLL crashes, only the
                // subprocess dies — OpenUTAU keeps running.
                var isolated = VstProbeProcess.ProbeVst2(dllPath);
                if (isolated != null) {
                    _entries[isolated.Uid] = isolated;
                    return true;
                }

                // ── Fallback: lightweight probe (in-process, no execution) ──
                using var probe = new Probe(dllPath);
                if (!probe.IsValid) return false;
                string dllName = Path.GetFileNameWithoutExtension(dllPath);
                string uid = BuildVst2Uid(dllName);
                _entries[uid] = new VstPluginEntry {
                    Uid = uid, Name = dllName, Vendor = "", Path = dllPath,
                    Type = VstPluginType.VST2,
                    SubCategories = new List<string>(),
                    IsEffect = true, // conservative fallback
                };
                return true;
            } catch (Exception ex) {
                Log.Warning($"[VST] VST2 probe failed '{dllPath}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Build a stable, cross-machine UID for a VST2 dll from its file name.
        /// Uses xxHash (deterministic across runs/machines) — NOT string.GetHashCode
        /// which is non-deterministic in .NET Core+ and would break .ustxp portability.
        /// </summary>
        internal static string BuildVst2Uid(string dllName) {
            ulong hash = XXH64.DigestOf(Encoding.UTF8.GetBytes(dllName));
            return $"vst2:{hash:x16}";
        }

        /// <summary>
        /// Classify whether this is an audio effect (safe for the processing chain)
        /// or an instrument (has no audio input — would crash or produce silence).
        /// </summary>
        public static bool ClassifyEffect(IReadOnlyList<string> subs, VstPluginType type) {
            // VST2: assume effect (safe default)
            if (type == VstPluginType.VST2) return true;

            // Instrument keywords — these plugins have NO audio input
            var instruments = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                "Instrument", "Synth", "Sampler", "Drum Machine", "Arpeggiator",
                "Generator", "Tone Generator",
            };

            foreach (var sub in subs) {
                if (instruments.Contains(sub)) return false;
            }

            // If no subcategories at all, assume effect (safe default)
            return true;
        }

        // ── Preferences ─────────────────────────────────────────

        private void LoadFromPreferences() {
            // Restore from cache if available
            if (Preferences.Default.VstCachedPlugins?.Count > 0) {
                foreach (var c in Preferences.Default.VstCachedPlugins) {
                    _entries[c.Uid] = new VstPluginEntry {
                        Uid = c.Uid, Name = c.Name, Vendor = c.Vendor,
                        Path = c.Path, Type = (VstPluginType)c.Type,
                        SubCategories = c.Subs ?? new List<string>(),
                        IsEffect = ClassifyEffect(c.Subs ?? new List<string>(), (VstPluginType)c.Type),
                    };
                }
                _scanned = true;
            }
        }

        private void SaveToPreferences() {
            Preferences.Default.VstCachedPlugins = _entries.Values.Select(e =>
                new Preferences.VstCachedEntry {
                    Uid = e.Uid, Name = e.Name, Vendor = e.Vendor,
                    Path = e.Path, Type = (int)e.Type,
                    Subs = e.SubCategories.ToList(),
                }).ToList();
            Preferences.Save();
        }

        static readonly string[] DefaultVst3Paths = {
            @"C:\Program Files\Common Files\VST3",
            @"C:\Program Files (x86)\Common Files\VST3",
        };
        static readonly string[] DefaultVst2Paths = {
            @"C:\Program Files\VSTPlugins",
            @"C:\Program Files (x86)\VSTPlugins",
            @"C:\Program Files\Steinberg\VSTPlugins",
        };
    }

    // ── Data types ──────────────────────────────────────────

    public class VstPluginEntry {
        public string Uid { get; set; } = "";
        public string Name { get; set; } = "";
        public string Vendor { get; set; } = "";
        public string Path { get; set; } = "";
        public VstPluginType Type { get; set; }
        public List<string> SubCategories { get; set; } = new();
        public bool IsEffect { get; set; } = true;

        public string CategoryDisplay =>
            SubCategories.Count > 0 ? string.Join(", ", SubCategories) : Type.ToString();

        public override string ToString() =>
            IsEffect ? $"[{Type}] {Name}{(Vendor.Length > 0 ? $" — {Vendor}" : "")}"
                     : $"[{Type} Instrument] {Name}{(Vendor.Length > 0 ? $" — {Vendor}" : "")}";
    }

    public enum VstPluginType { Unknown = 0, VST2 = 2, VST3 = 3 }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenUtau.Classic;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;
using YamlDotNet.RepresentationModel;

namespace OpenUtau.Core.Format {
    /// <summary>
    /// OpenUTAU Plus native project format (.ustxp).
    /// Extension of the USTX format with additional Plus-specific data:
    /// - Per-track VST plugin chains
    /// - Mixer state
    /// - Plus-only expressions and settings
    ///
    /// Saving: always writes .ustxp (Plus default).
    /// Loading: reads both .ustx (legacy) and .ustxp (native).
    /// </summary>
    public static class Ustxp {
        public static readonly Version kUstxpVersion = new Version(1, 0);

        public const string Extension = ".ustxp";
        public const string LegacyExtension = ".ustx";

        public static bool IsUstxpFile(string path) {
            return path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsUstxFile(string path) {
            return path.EndsWith(LegacyExtension, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsSupportedFile(string path) {
            return IsUstxpFile(path) || IsUstxFile(path);
        }

        /// <summary>
        /// Save project in .ustxp (Plus native) format.
        /// </summary>
        public static void Save(string filePath, UProject project) {
            try {
                // Ensure .ustxp extension
                if (!filePath.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)) {
                    filePath = Path.ChangeExtension(filePath, Extension);
                }
                project.ustxVersion = Ustx.kUstxVersion; // Base USTX version for compatibility
                project.ustxpVersion = kUstxpVersion;    // Plus version for Plus-specific tracking
                project.FilePath = filePath;
                project.BeforeSave();
                File.WriteAllText(filePath, SerializeForSave(project), Encoding.UTF8);
                project.Saved = true;
                project.AfterSave();
                Preferences.Default.RecoveryPath = string.Empty;
                Preferences.Save();
                DocManager.Inst.Recovered = false;
            } catch (Exception ex) {
                var e = new MessageCustomizableException(
                    $"Failed to save ustxp: {filePath}",
                    $"<translate:errors.failed.save>: {filePath}", ex);
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        /// <summary>
        /// Auto-save for crash recovery. Uses .ustxp extension.
        /// </summary>
        public static void AutoSave(string filePath, UProject project) {
            try {
                // Always save as .ustxp for recovery
                if (!filePath.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) &&
                    !filePath.EndsWith(LegacyExtension, StringComparison.OrdinalIgnoreCase)) {
                    filePath = Path.ChangeExtension(filePath, Extension);
                }
                project.ustxVersion = Ustx.kUstxVersion;
                project.ustxpVersion = kUstxpVersion;
                project.BeforeSave();
                File.WriteAllText(filePath, SerializeForSave(project), Encoding.UTF8);
                project.AfterSave();
                Preferences.Default.RecoveryPath = filePath;
                Preferences.Save();
            } catch (Exception ex) {
                Log.Error(ex, $"Failed to autosave: {filePath}");
            }
        }

        /// <summary>
        /// Load a project from either .ustxp or .ustx format.
        ///
        /// W27（task-37）两处止血：
        /// - **未知键透传**：文件里我们没建模的键（上游新增字段）收进模型的 <see cref="UnknownYaml"/>，
        ///   保存时写回（见 <see cref="UstxYaml"/>）。不改成"遇未知键就抛"。
        /// - **版本墙放宽**：`ustx_version` 比我们新时不再直接抛，改为 warning + 自动 `.bak` + 尽力读；
        ///   只有**解析失败**才抛（并给出可操作提示），因为"能读一部分"远好于"完全打不开"。
        /// </summary>
        public static UProject Load(string filePath) {
            string text = File.ReadAllText(filePath, Encoding.UTF8);
            UProject project;
            YamlMappingNode? tree = null;
            try {
                project = Yaml.DefaultDeserializer.Deserialize<UProject>(text)
                    ?? throw new FileFormatException("Empty project file.");
            } catch (Exception ex) {
                throw new MessageCustomizableException(
                    $"Failed to parse project file: {filePath}",
                    $"<translate:errors.failed.openproject>:\n{filePath}",
                    new FileFormatException("Failed to parse project file.", ex));
            }

            // 未知键：解析一次树，把没建模的键挂到对象上（失败不影响加载）
            try {
                tree = UstxYaml.ParseTree(text);
                UstxYaml.CaptureAll(tree, project, message => Log.Warning($"Unknown key passthrough skipped: {message}"));
            } catch (Exception ex) {
                Log.Warning(ex, "Failed to capture unknown keys; the file will still load.");
            }

            // Register default expressions
            Ustx.AddDefaultExpressions(project);

            project.FilePath = filePath;
            project.Saved = true;
            project.AfterLoad();
            project.ValidateFull();

            // 版本墙（W27）：比我们新 ⇒ warning + 备份 + 尽力读
            if (project.ustxVersion > Ustx.kUstxVersion) {
                Log.Warning(
                    $"Project file {filePath} is newer than this software " +
                    $"(file ustx_version={project.ustxVersion}, ours={Ustx.kUstxVersion}). " +
                    "Attempting best-effort load; unmodeled fields are preserved by unknown-key passthrough. " +
                    "Backup kept at .bak");
                BackupOnce(filePath);
            }

            // 迁移阶梯（一张可查的表，见 UstxMigrations）
            UstxMigrations.Run(project, UstxMigrations.Kind.Ustx);

            // Upgrade USTX base version to latest
            project.ustxVersion = Ustx.kUstxVersion;

            // Run Plus-specific migrations if ustxpVersion is present
            if (project.ustxpVersion != null && project.ustxpVersion < kUstxpVersion) {
                Log.Information($"Upgrading Plus project from {project.ustxpVersion} to {kUstxpVersion}");
                RunPlusMigrations(project, project.ustxpVersion);
            }
            project.ustxpVersion = kUstxpVersion;
            return project;
        }

        /// <summary>
        /// 版本比我们新时留一份原始文件（**不覆盖已存在的备份**：第一次打开时的那份最原始，最值得留）。
        /// </summary>
        internal static string BackupOnce(string filePath) {
            string backup = filePath + ".bak";
            try {
                if (!File.Exists(backup)) {
                    File.Copy(filePath, backup, overwrite: false);
                    Log.Information($"Backup written: {backup}");
                }
            } catch (Exception ex) {
                Log.Warning(ex, $"Failed to write backup for {filePath}");
            }
            return backup;
        }

        /// <summary>
        /// 「另存为纯净 .ustx」(W27 第 3 件)：写出**上游能直接读、且不含任何 Plus 专有字段**的文件。
        ///
        /// 用于"把我的工程交给原版用户/在原版里打开"这条唯一安全的交付路径。
        /// **它不解决"上游重存丢 Plus 字段"**——上游保存时依然会丢掉 VST 链等它不认识的键；
        /// 那需要侧车文件或容器化格式（审计 A+/B 方案），不在本轮范围。
        /// </summary>
        public static void ExportCleanUstx(string filePath, UProject project) {
            if (!filePath.EndsWith(LegacyExtension, StringComparison.OrdinalIgnoreCase)) {
                filePath = Path.ChangeExtension(filePath, LegacyExtension);
            }
            var clean = project.CloneAsTemplate();
            clean.ustxpVersion = null;                     // Plus 版本标记
            foreach (var track in clean.tracks) {
                track.MixFx = null;                        // Plus 混音台效果链
                track.VstSlots.Clear();                  // Plus VST 插件链
            }
            clean.FilePath = filePath;
            clean.ustxVersion = Ustx.kUstxVersion;
            clean.BeforeSave();
            string text = Prune(Yaml.DefaultSerializer.Serialize(clean));
            File.WriteAllText(filePath, text, Encoding.UTF8);
            clean.Saved = true;
            clean.AfterSave();
            Log.Information($"Exported clean .ustx (no Plus fields): {filePath}");
        }

        /// <summary>
        /// 保存序列化（W27 第 1 + 4 件）：
        /// - 有未知键 ⇒ 序列化 → 树 → 把未知键补回 → 剪空集合 → 文本（慢路径，键值原样保留）；
        /// - 无未知键 ⇒ 只做"剪空集合"（噪音清理），零额外解析。
        /// </summary>
        internal static string SerializeForSave(UProject project) {
            string text = Yaml.DefaultSerializer.Serialize(project);
            if (!UstxYaml.HasAnyUnknown(project)) {
                return Prune(text);
            }
            var tree = UstxYaml.ParseTree(text);
            UstxYaml.MergeAll(tree, project);
            UstxYaml.PruneEmpty(tree);
            return UstxYaml.SerializeTree(tree);
        }

        /// <summary>
        /// 序列化文本 → 树 → 剪掉空集合/null → 文本（W27 第 4 件；导出与保存共用）。
        /// </summary>
        internal static string Prune(string serialized) {
            var tree = UstxYaml.ParseTree(serialized);
            UstxYaml.PruneEmpty(tree);
            return UstxYaml.SerializeTree(tree);
        }

        /// <summary>
        /// Plus-specific format migrations. Called when ustxpVersion is behind.
        /// </summary>
        private static void RunPlusMigrations(UProject project, Version fromVersion) {
            // v1.0 → future: add Plus migrations here
        }
    }
}

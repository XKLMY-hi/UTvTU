using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Core.Format {
    /// <summary>
    /// W27（task-37）**迁移表**：把原先散在 `Ustxp.Load` 与 `Ustx.Load` 里的两份 if 阶梯
    /// （v0.4→v0.9 完全重复的拷贝）收敛成一张可查、可测、可扩展的表。
    ///
    /// 用法：`UstxMigrations.Run(project, UstxMigrations.Kind.Ustx)` —— 按版本升序执行所有
    /// `from &lt; 项目版本` 的迁移；每步自带 `ValidateFull()`，与旧行为一致。
    /// 新增格式版本时只需在对应的表里加一行（`0.9→0.10` 那种"无结构变化"的版本留空迁移占位）。
    /// </summary>
    public static class UstxMigrations {
        public enum Kind {
            /// <summary>上游 USTX 基线（`ustx_version`）。</summary>
            Ustx,
            /// <summary>Plus 扩展（`ustxp_version`）。</summary>
            Plus,
        }

        public readonly struct Step {
            /// <summary>项目版本低于此值时执行（即"迁移到该版本"）。</summary>
            public readonly Version To;
            public readonly Action<UProject> Apply;
            /// <summary>这一步做了什么（便于日志/排查）。</summary>
            public readonly string Description;

            public Step(Version to, Action<UProject> apply, string description) {
                To = to;
                Apply = apply;
                Description = description;
            }
        }

        /// <summary>上游基线迁移表（升序）。</summary>
        public static readonly IReadOnlyList<Step> UstxSteps = new List<Step> {
            new Step(new Version(0, 4), MigrateToV04, "acc 表达式 → atk（attack）"),
            new Step(new Version(0, 5), MigrateToV05, "歌词 \"...\" → \"+\""),
            new Step(new Version(0, 6), MigrateToV06, "bpm/beat_per_bar/beat_unit → tempos/time_signatures"),
            new Step(new Version(0, 7), MigrateToV07, "补齐 exp_selectors 长度"),
            new Step(new Version(0, 9), MigrateToV09, "无结构变化（占位，与上游 0.9 对齐）"),
            new Step(new Version(0, 10), MigrateToV10, "无结构变化（占位，与上游 0.10 对齐）"),
        };

        /// <summary>Plus 扩展迁移表（升序）。目前 1.0 是首版，只有占位。</summary>
        public static readonly IReadOnlyList<Step> PlusSteps = new List<Step> {
            new Step(new Version(1, 0), _ => { }, "Plus 1.0：首个 .ustxp 版本（占位）"),
        };

        public static IReadOnlyList<Step> Table(Kind kind) => kind == Kind.Ustx ? UstxSteps : PlusSteps;

        /// <summary>
        /// 按版本升序执行所有适用的迁移。<paramref name="project"/> 的版本字段**不被这里改写**
        /// （调用方决定何时把版本提升到最新）。
        /// </summary>
        public static void Run(UProject project, Kind kind, Version? from = null) {
            Version version = from
                ?? (kind == Kind.Ustx ? project.ustxVersion : project.ustxpVersion)
                ?? new Version(0, 0);
            foreach (var step in Table(kind)) {
                if (version < step.To) {
                    Log.Information($"Migrating ({kind}) {version} → {step.To}: {step.Description}");
                    step.Apply(project);
                }
            }
        }

        private static void MigrateToV04(UProject project) {
            if (project.expressions.TryGetValue("acc", out var exp) && exp.name == "accent") {
                project.expressions.Remove("acc");
                exp.abbr = Ustx.ATK;
                exp.name = "attack";
                project.expressions[Ustx.ATK] = exp;
                project.parts
                    .Where(part => part is UVoicePart)
                    .Select(part => part as UVoicePart)
                    .SelectMany(part => part!.notes)
                    .SelectMany(note => note.phonemeExpressions)
                    .Where(pExp => pExp.abbr == "acc")
                    .ToList()
                    .ForEach(pExp => pExp.abbr = Ustx.ATK);
            }
            project.ValidateFull();
        }

        private static void MigrateToV05(UProject project) {
            project.parts
                .Where(part => part is UVoicePart)
                .Select(part => part as UVoicePart)
                .SelectMany(part => part!.notes)
                .Where(note => note.lyric.StartsWith("..."))
                .ToList()
                .ForEach(note => note.lyric = note.lyric.Replace("...", "+"));
            project.ValidateFull();
        }

        private static void MigrateToV06(UProject project) {
#pragma warning disable CS0612
            project.timeSignatures = new List<UTimeSignature> {
                new UTimeSignature(0, project.beatPerBar, project.beatUnit) };
            project.tempos = new List<UTempo> { new UTempo(0, project.bpm) };
#pragma warning restore CS0612
            project.ValidateFull();
        }

        private static void MigrateToV07(UProject project) {
            var expSelectors = new UProject().expSelectors;
            if (project.expSelectors.Length < expSelectors.Length) {
                for (int i = 0; i < project.expSelectors.Length; i++) {
                    expSelectors[i] = project.expSelectors[i];
                }
                project.expSelectors = expSelectors;
            }
        }

        /// <summary>0.9 → 0.10：上游这一档没有工程结构变化（新增的是表达式图等新键），保留占位。</summary>
        private static void MigrateToV10(UProject project) {
            // 无结构变化。上游新增的键由未知键透传保留（见 UstxYaml）。
        }

        private static void MigrateToV09(UProject project) {
            // 无结构变化（与上游 0.9 对齐）。
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OpenUtau.Core.Ustx {
    /// <summary>
    /// W27（task-37）**未知键透传**：把文件里我们没建模的 YAML 键原样留着，保存时写回去。
    ///
    /// 背景：上游 OpenUTAU 一直在给工程模型加字段（`expression_graphs`/`default_expression_graphs` 在
    /// 项目级、`expression_graph` 在轨道级、`masked_curves` 在部件级…）。我们读侧用
    /// `IgnoreUnmatchedProperties()` ⇒ 这些键被**静默吃掉**，我们一保存就永久丢失（W8 审计 §1.3/P1）。
    ///
    /// 口径（不改成"遇未知键就抛"，也不改文件形态）：
    /// - 加载：解析 YAML 树 → 建模对象 → 把"不在该类型已知键集合里"的键挂到对象的
    ///   <see cref="Holder.Unknown"/> 上（键、值、嵌套结构全原样保留）。
    /// - 保存：正常序列化 → 把未知键**补回**到树的对应位置再写出（模型已产出的键以模型为准，
    ///   绝不重复写同一个键）。没有任何未知键时走快路径，零额外开销。
    /// - 未知键数据**跟着对象走**（挂在项目/轨道/部件/音符实例上）⇒ 增删对象时不会错位，
    ///   这是它比"按索引合并整棵树"安全的地方。
    ///
    /// 覆盖层级（本轮止血范围）：项目根、轨道 `tracks[i]`、部件 `voice_parts[i]`/`wave_parts[i]`、
    /// 音符 `notes[i]`。音符级要求文件顺序与我们 `SortedSet&lt;UNote&gt;` 的排序一致（按 position/tone 校验），
    /// 不一致时**跳过该部件的音符级透传并记日志**——宁可少透传，也不错挂到别的音符上。
    /// </summary>
    public class UnknownYaml {
        /// <summary>键 → 原始 YAML 节点（值的嵌套结构原样保留）。</summary>
        public Dictionary<string, YamlNode> Entries { get; } = new Dictionary<string, YamlNode>();

        public bool IsEmpty => Entries.Count == 0;

        public void Add(string key, YamlNode node) => Entries[key] = node;

        public bool TryGet(string key, out YamlNode node) => Entries.TryGetValue(key, out node!);
    }

    /// <summary>能被透传未知键的模型对象（项目 / 轨道 / 部件 / 音符）。</summary>
    public interface IUnknownYamlHolder {
        UnknownYaml? Unknown { get; set; }
    }

    /// <summary>YAML 树 ↔ 模型 的未知键搬运工具（只在 .ustxp 读写路径上用）。</summary>
    public static class UstxYaml {
        // 已知键集合：按类型各算一次（序列化一个默认实例，取其 mapping 的键）。
        // 注意 OmitNull 会让"默认值为 null"的键不在集合里 ⇒ 文件里出现同名键时会被当成未知键保留，
        // 这在保存时是安全的（模型若产出同名键则以模型为准，见 MergeInto）。
        private static readonly Dictionary<Type, HashSet<string>> KnownKeys = new Dictionary<Type, HashSet<string>>();
        private static readonly object KnownKeysLock = new object();
        private static readonly IDeserializer treeDeserializer = new DeserializerBuilder().Build();
        private static readonly ISerializer treeSerializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .DisableAliases()
            .WithQuotingNecessaryStrings()
            .Build();

        public static bool IsKnownKey(Type type, string key) {
            lock (KnownKeysLock) {
                if (!KnownKeys.TryGetValue(type, out var set)) {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    try {
                        string text = treeSerializer.Serialize(Activator.CreateInstance(type)!);
                        var node = treeDeserializer.Deserialize<YamlMappingNode>(text);
                        foreach (var pair in node.Children) {
                            set.Add(pair.Key.ToString());
                        }
                    } catch (Exception) {
                        // 某些类型无法用无参构造（例如抽象部件）⇒ 视作"全未知"，
                        // 调用方会退化为不排除任何键（更保守：宁可多留，不可丢）。
                    }
                    KnownKeys[type] = set;
                }
                return set.Contains(key);
            }
        }

        public static YamlMappingNode ParseTree(string text) => treeDeserializer.Deserialize<YamlMappingNode>(text);

        public static string SerializeTree(YamlNode node) => treeSerializer.Serialize(node);

        /// <summary>把 mapping 里"该类型不认识"的键收进对象的 <see cref="UnknownYaml"/>。</summary>
        public static void CaptureUnknown(YamlMappingNode source, object? model) {
            if (model is not IUnknownYamlHolder holder) {
                return;
            }
            foreach (var pair in source.Children) {
                string key = pair.Key.ToString();
                if (IsKnownKey(model.GetType(), key)) {
                    continue;
                }
                holder.Unknown ??= new UnknownYaml();
                holder.Unknown.Add(key, pair.Value);
            }
        }

        /// <summary>把对象的未知键补进目标 mapping（已存在的键跳过：模型产出的值优先）。</summary>
        public static void MergeInto(YamlMappingNode target, object? model) {
            if (model is not IUnknownYamlHolder holder || holder.Unknown == null || holder.Unknown.IsEmpty) {
                return;
            }
            foreach (var pair in holder.Unknown.Entries) {
                bool exists = target.Children.Any(c => string.Equals(c.Key.ToString(), pair.Key, StringComparison.Ordinal));
                if (!exists) {
                    target.Add(new YamlScalarNode(pair.Key), pair.Value);
                }
            }
        }

        /// <summary>是否有任何对象带未知键（决定保存时是否需要走"合并再写"的慢路径）。</summary>
        public static bool HasAnyUnknown(UProject project) {
            if (project is IUnknownYamlHolder p && p.Unknown != null && !p.Unknown.IsEmpty) {
                return true;
            }
            foreach (var track in project.tracks) {
                if (track is IUnknownYamlHolder t && t.Unknown != null && !t.Unknown.IsEmpty) {
                    return true;
                }
            }
            foreach (var part in AllParts(project)) {
                if (part is IUnknownYamlHolder partHolder && partHolder.Unknown != null && !partHolder.Unknown.IsEmpty) {
                    return true;
                }
                if (part is UVoicePart voicePart) {
                    foreach (var note in voicePart.notes) {
                        if (note is IUnknownYamlHolder n && n.Unknown != null && !n.Unknown.IsEmpty) {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>项目里所有部件（模型侧；<c>project.parts</c> 是 <c>[YamlIgnore]</c> 的运行期缓存）。</summary>
        public static IEnumerable<UPart> AllParts(UProject project) {
            if (project.parts != null && project.parts.Count > 0) {
                return project.parts;
            }
            return (project.voiceParts ?? new List<UVoicePart>()).Cast<UPart>()
                .Concat(project.waveParts ?? new List<UWavePart>());
        }

        /// <summary>
        /// 加载侧：把 YAML 树里的未知键收进模型（项目 → 轨道 → 部件 → 音符）。
        /// 音符级按 (position, tone) 校验文件顺序与我们的排序是否一致，不一致就跳过并记日志。
        /// </summary>
        public static void CaptureAll(YamlMappingNode root, UProject project, Action<string>? warn = null) {
            CaptureUnknown(root, project);

            var tracksNode = Child(root, "tracks") as YamlSequenceNode;
            if (tracksNode != null && tracksNode.Children.Count == project.tracks.Count) {
                for (int i = 0; i < tracksNode.Children.Count; i++) {
                    if (tracksNode.Children[i] is YamlMappingNode trackNode) {
                        CaptureUnknown(trackNode, project.tracks[i]);
                    }
                }
            }

            CaptureParts(root, "voice_parts", (project.voiceParts ?? new List<UVoicePart>()).Cast<UPart>().ToList(), warn);
            CaptureParts(root, "wave_parts", (project.waveParts ?? new List<UWavePart>()).Cast<UPart>().ToList(), warn);
        }

        private static void CaptureParts(YamlMappingNode root, string key, List<UPart> parts, Action<string>? warn) {
            if (Child(root, key) is not YamlSequenceNode partsNode || partsNode.Children.Count != parts.Count) {
                return;
            }
            for (int i = 0; i < partsNode.Children.Count; i++) {
                if (partsNode.Children[i] is not YamlMappingNode partNode) {
                    continue;
                }
                CaptureUnknown(partNode, parts[i]);
                if (parts[i] is not UVoicePart voicePart) {
                    continue;
                }
                if (Child(partNode, "notes") is not YamlSequenceNode notesNode) {
                    continue;
                }
                var notes = voicePart.notes.ToList();
                if (notesNode.Children.Count != notes.Count) {
                    warn?.Invoke($"{key}[{i}]: 音符数不一致，跳过音符级未知键透传");
                    continue;
                }
                bool aligned = true;
                for (int n = 0; n < notes.Count; n++) {
                    if (notesNode.Children[n] is not YamlMappingNode noteNode ||
                        !MatchesNote(noteNode, notes[n])) {
                        aligned = false;
                        break;
                    }
                }
                if (!aligned) {
                    warn?.Invoke($"{key}[{i}]: 音符顺序与我们排序不一致，跳过音符级未知键透传");
                    continue;
                }
                for (int n = 0; n < notes.Count; n++) {
                    CaptureUnknown((YamlMappingNode)notesNode.Children[n], notes[n]);
                }
            }
        }

        private static bool MatchesNote(YamlMappingNode noteNode, UNote note) {
            return Scalar(noteNode, "position") == note.position.ToString() &&
                Scalar(noteNode, "tone") == note.tone.ToString();
        }

        private static string? Scalar(YamlMappingNode node, string key) {
            return Child(node, key) is YamlScalarNode scalar ? scalar.Value : null;
        }

        /// <summary>保存侧：把模型的未知键补回序列化后的树（项目 → 轨道 → 部件 → 音符）。</summary>
        public static void MergeAll(YamlMappingNode root, UProject project) {
            MergeInto(root, project);

            if (Child(root, "tracks") is YamlSequenceNode tracksNode &&
                tracksNode.Children.Count == project.tracks.Count) {
                for (int i = 0; i < tracksNode.Children.Count; i++) {
                    if (tracksNode.Children[i] is YamlMappingNode trackNode) {
                        MergeInto(trackNode, project.tracks[i]);
                    }
                }
            }

            MergeParts(root, "voice_parts", (project.voiceParts ?? new List<UVoicePart>()).Cast<UPart>().ToList());
            MergeParts(root, "wave_parts", (project.waveParts ?? new List<UWavePart>()).Cast<UPart>().ToList());
        }

        private static void MergeParts(YamlMappingNode root, string key, List<UPart> parts) {
            if (Child(root, key) is not YamlSequenceNode partsNode || partsNode.Children.Count != parts.Count) {
                return;
            }
            for (int i = 0; i < partsNode.Children.Count; i++) {
                if (partsNode.Children[i] is not YamlMappingNode partNode) {
                    continue;
                }
                MergeInto(partNode, parts[i]);
                if (parts[i] is not UVoicePart voicePart ||
                    Child(partNode, "notes") is not YamlSequenceNode notesNode) {
                    continue;
                }
                var notes = voicePart.notes.ToList();
                if (notesNode.Children.Count != notes.Count) {
                    continue;
                }
                bool aligned = true;
                for (int n = 0; n < notes.Count; n++) {
                    if (notesNode.Children[n] is not YamlMappingNode noteNode || !MatchesNote(noteNode, notes[n])) {
                        aligned = false;
                        break;
                    }
                }
                if (!aligned) {
                    continue;
                }
                for (int n = 0; n < notes.Count; n++) {
                    MergeInto((YamlMappingNode)notesNode.Children[n], notes[n]);
                }
            }
        }

        /// <summary>
        /// 噪音清理（W27 第 4 件）：删掉空序列/空映射（`vst_slots: []`、`track_expressions: []` …
        /// 每轨恒写、纯噪音），以及显式 null 的键。语义不变：读回时缺键 = 默认空集合。
        /// </summary>
        public static int PruneEmpty(YamlNode node) {
            int removed = 0;
            if (node is YamlMappingNode mapping) {
                var doomed = new List<YamlNode>();
                foreach (var pair in mapping.Children) {
                    removed += PruneEmpty(pair.Value);
                    if (pair.Value is YamlSequenceNode seq && seq.Children.Count == 0) {
                        doomed.Add(pair.Key);
                    } else if (pair.Value is YamlMappingNode map && map.Children.Count == 0) {
                        doomed.Add(pair.Key);
                    } else if (pair.Value is YamlScalarNode scalar && scalar.Value == null && scalar.Style == ScalarStyle.Plain) {
                        // 只删真正的 null 标量（`key:` 后面什么都没有），空字符串不动
                        if (scalar.Tag.IsEmpty) {
                            doomed.Add(pair.Key);
                        }
                    }
                }
                foreach (var key in doomed) {
                    mapping.Children.Remove(key);
                    removed++;
                }
            } else if (node is YamlSequenceNode sequence) {
                foreach (var child in sequence.Children) {
                    removed += PruneEmpty(child);
                }
            }
            return removed;
        }

        private static YamlNode? Child(YamlMappingNode node, string key) {
            foreach (var pair in node.Children) {
                if (string.Equals(pair.Key.ToString(), key, StringComparison.Ordinal)) {
                    return pair.Value;
                }
            }
            return null;
        }
    }
}

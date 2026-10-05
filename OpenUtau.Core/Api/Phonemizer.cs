using System;
using System.Collections.Generic;
using System.Globalization;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Api {
    /// <summary>
    /// Mark your Phonemizer class with this attribute for OpenUtau to load it.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class PhonemizerAttribute : Attribute {
        public string Name { get; private set; }
        public string Tag { get; private set; }
        public string Author { get; private set; }
        public string Language { get; private set; }

        /// <param name="name">Name of phonemizer. Required.</param>
        /// <param name="tag">Use IETF language code + phonetic type as tag, e.g., "EN ARPA", "JA VCV", etc. Required.</param>
        /// <param name="author">Author of this phonemizer.</param>
        /// <param name="language">IETF language code of this phonemizer's singing language, e.g., "EN", "JA"</param>
        public PhonemizerAttribute(string name, string tag, string author = null, string language = null) {
            Name = name;
            Tag = tag;
            Author = author;
            Language = language;
        }
    }

    /// <summary>
    /// Phonemizer plugin interface.
    /// </summary>
    public abstract class Phonemizer {
        /// <summary>
        /// The input struct that represents a note.
        /// </summary>
        public struct Note {
            /// <summary>
            /// Lyric of the note, the part of lyric that is not enclosed in "[]".
            /// Example: if lyric on note is "read". The lyric is "read".
            /// Example: if lyric on note is "read[r iy d]". The lyric is "read".
            /// </summary>
            public string lyric;

            /// <summary>
            /// Phonetic hint,
            /// Example: if lyric on note is "read". The hint is null.
            /// Example: if lyric on note is "read[r iy d]". The hint is "r iy d".
            /// </summary>
            public string phoneticHint;

            /// <summary>
            /// Music tone of note. C4 = 60.
            /// </summary>
            public int tone;

            /// <summary>
            /// Position of note in project, measured in ticks.
            /// Use timeAxis to convert between ticks and milliseconds .
            /// </summary>
            public int position;

            /// <summary>
            /// Duration of note measured in ticks.
            /// Use timeAxis to convert between ticks and milliseconds .
            /// </summary>
            public int duration;

            /// <summary>
            /// Phoneme overrides. Not guaranteed to exist or be ordered.
            /// </summary>
            public PhonemeAttributes[] phonemeAttributes;

            public override string ToString() => $"\"{lyric}\" pos:{position}";
        }

        public struct PhonemeAttributes {
            /// <summary>
            /// Index of phoneme.
            /// </summary>
            public int index;
            /// <summary>
            /// Consonant stretch ratio computed from velocity.
            /// </summary>
            public double? consonantStretchRatio;
            /// <summary>
            /// Tone shift. Shifts the note tone used for oto lookup.
            /// </summary>
            public int? toneShift;
            /// <summary>
            /// Alternate index. The number suffix of duplicate aliases.
            /// </summary>
            public int? alternate;
            /// <summary>
            /// Voice color.
            /// </summary>
            public string? voiceColor;
        }

        public struct PhonemeExpression {
            public string abbr;
            public float value;
        }

        /// <summary>
        /// The output struct that represents a phoneme.
        /// </summary>
        public struct Phoneme {
            /// <summary>
            /// Number to manage phonemes in note.
            /// Optional. Whether to specify an index or not should be consistent within Phonemizer (All phonemes should be indexed, or all should be unindexed).
            /// </summary>
            public int? index;

            /// <summary>
            /// Phoneme name. Should match one of oto alias.
            /// Note that you don't have to return tone-mapped phonemes. OpenUtau will do it afterwards.
            /// I.e., you can simply return "あ" even when best match is actually "あC5".
            /// OpenUtau will try to find the most suitable tone for this phoneme, and tone-map based on it.
            /// </summary>
            public string phoneme;

            /// <summary>
            /// Position of phoneme in note. Measured in ticks.
            /// Use TickToMs() and MsToTick() to convert between ticks and milliseconds.
            /// </summary>
            public int position;

            /// <summary>
            /// Error from the phonemizer, if this phoneme represents an error.
            /// </summary>
            public Exception? error;

            /// <summary>
            /// Suggested attributes. It may later be overwritten with a user-specified value.
            /// </summary>
            public List<PhonemeExpression> expressions;

            public override string ToString() => $"\"{phoneme}\" pos:{position}";
        }

        /// <summary>
        /// Result returned by Process().
        /// </summary>
        public struct Result {
            /// <summary>
            /// An array of phonemes that are corresponding to input notes.
            /// </summary>
            public Phoneme[] phonemes;
        }

        public string Name { get; set; }
        public string Tag { get; set; }
        public string Language { get; set; }
        internal Exception? SetUpException { get; set; }

        protected double bpm;
        protected TimeAxis timeAxis;

        /// <summary>
        /// Sets the current singer. Called by OpenUtau when user changes the singer.
        ///
        /// In addition to using data in the USinger class,
        /// a phonemizer can also use this method to load singer-specific resource,
        /// such as a custom dictionary file in the singer directory.
        /// Use singer.Location to access the singer directory.
        ///
        /// This method, SetUp, Process and CleanUp are always called from a single
        /// background thread and never concurrently, so it is fine to block here
        /// while loading. Do not load on another thread: Process may otherwise run
        /// against half-initialized state. OpenUtau shows a progress indicator if
        /// this call takes long.
        ///
        /// Do not modify the singer.
        /// </summary>
        /// <param name="singer"></param>
        public abstract void SetSinger(USinger singer);

        /// <summary>
        /// Uses the legacy bahaviour of further mapping phonemizer outputs.
        /// Do not override for new phonemizers.
        /// </summary>
        public virtual bool LegacyMapping => false;

        public UProject? project;
        public UTrack? track;

        /// <summary>
        /// 测试模式开关：由单元测试置 true，让音素化器走**确定性**分支（不做运行时自适应/
        /// 依赖真实声库状态的取舍），使断言稳定。来自上游 `a8ddc510`（"Obsolete
        /// OnAsyncInitStarted/Finished; runner reports init progress"）—— 我们当年只摘了该
        /// 提交的 runner 侧（初始化进度上报），漏了这个成员；上游的 `SyllableBasedPhonemizer`
        /// 与 `EnVCCVTest` 等都要读它，缺了会 CS0115/CS1061。纯增量成员，无签名破坏。
        /// </summary>
        public bool Testing { get; set; } = false;

        public virtual void SetUp(Note[][] notes, UProject project, UTrack track) {
            this.project = project;
            this.track = track;
        }

        /// <summary>
        /// Phonemize a consecutive sequence of notes. This is the main logic of a phonemizer.
        /// </summary>
        /// <param name="notes">A note and its extender notes. Always one or more.</param>
        /// <param name="prev">The note before the leading note, if exists.</param>
        /// <param name="next">The note after the last extender note, if exists.</param>
        /// <param name="prevNeighbour">Same as prev if is immediate neighbour, otherwise null.</param>
        /// <param name="nextNeighbour">Same as next if is immediate neighbour, otherwise null.</param>
        /// <param name="prevs">Prev note neighbour with all extended notes. May be emtpy, not null</param>
        public abstract Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevs);

        public virtual void CleanUp() { }

        public override string ToString() => $"[{Tag}] {Name}";

        /// <summary>
        /// Used by OpenUtau to set timing info for TickToMs() and MsToTick().
        /// Not need to call this method from within a phonemizer.
        /// </summary>
        public void SetTiming(TimeAxis timeAxis) {
            this.timeAxis = timeAxis;
            bpm = timeAxis.GetBpmAtTick(0);
        }

        public string DictionariesPath => PathManager.Inst.DictionariesPath;
        public string PluginDir => PathManager.Inst.PluginsPath;

        /// <summary>
        /// Utility method to convert tick position to millisecond position.
        /// </summary>
        [Obsolete] // TODO: update usages
        protected double TickToMs(int tick) {
            return timeAxis.TickPosToMsPos(tick);
        }

        /// <summary>
        /// Utility method to convert millisecond position to tick position.
        /// </summary>
        [Obsolete] // TODO: update usages
        protected int MsToTick(double ms) {
            return timeAxis.MsPosToTickPos(ms);
        }

        /// <summary>
        /// Utility method to convert plain string to Unicode elements.
        /// </summary>
        public static IList<string> ToUnicodeElements(string lyric) {
            var result = new List<string>();
            var etor = StringInfo.GetTextElementEnumerator(lyric);
            while (etor.MoveNext()) {
                result.Add(etor.GetTextElement());
            }
            return result;
        }

        [Obsolete("No-op. Load synchronously in SetSinger; phonemizer methods run on a single thread and OpenUtau reports progress itself.")]
        protected void OnAsyncInitStarted() { }

        [Obsolete("No-op. Load synchronously in SetSinger; phonemizer methods run on a single thread and OpenUtau reports progress itself.")]
        protected void OnAsyncInitFinished() { }

        protected Result MakeSimpleResult(string phoneme) {
            return new Result() {
                phonemes = new Phoneme[] {
                    new Phoneme() {
                        phoneme = phoneme
                    }
                }
            };
        }

        public double GetParentConsonantStretchRatio() {
            if (project != null && track != null) {
                if (track.TryGetExpDescriptor(project, Core.Format.Ustx.VEL, out var trackVEL)) {
                    return Math.Pow(2, 1.0 - trackVEL.CustomDefaultValue / 100.0);
                }
            }
            return 1;
        }

        /// <summary>
        /// 轨道级 Tone Shift（SHFT）默认值。上游 2c283d2b 的口径：`project`/`track` 任一为空
        /// 直接返回 0；描述符解析失败要**记日志后重抛**（此前静默吞掉 ⇒ 音素化器拿到 0 却
        /// 无从知道为什么，用户看到的只是"变调没生效"）。
        /// </summary>
        public int GetParentToneShift() {
            if (project == null || track == null) {
                return 0;
            }
            try {
                if (track.TryGetExpDescriptor(project, Core.Format.Ustx.SHFT, out var trackTS) && trackTS != null) {
                    return (int)trackTS.CustomDefaultValue;
                }
            } catch (Exception ex) {
                Log.Error(ex, "Failed to resolve track Tone Shift (SHFT) descriptor for track {TrackName} ({TrackNo}).",
                    track.TrackName, track.TrackNo);
                throw;
            }
            return 0;
        }

        /// <summary>轨道级 Alternate（ALT）默认值；0 视为"未设置"（返回 null）。同上游 2c283d2b。</summary>
        public int? GetParentAlternate() {
            if (project == null || track == null) {
                return null;
            }
            try {
                if (track.TryGetExpDescriptor(project, Core.Format.Ustx.ALT, out var trackAlt) && trackAlt != null) {
                    if (trackAlt.CustomDefaultValue != 0) {
                        return (int)trackAlt.CustomDefaultValue;
                    }
                }
            } catch (Exception ex) {
                Log.Error(ex, "Failed to resolve track Alternate (ALT) descriptor for track {TrackName} ({TrackNo}).",
                    track.TrackName, track.TrackNo);
                throw;
            }
            return null;
        }

        /// <summary>
        /// 轨道级 Voice Color（CLR）默认值。
        ///
        /// 取值必须走 <c>track.VoiceColorExp.options</c>：那才是**按声库 subbank 颜色列表**
        /// 在 <c>UTrack.Validate</c> 里建出来的运行期描述符（UNote/UPhoneme 也都用它），
        /// 而 <c>TryGetExpDescriptor(CLR)</c> 在 <c>VoiceColorExp != null</c> 时返回的正是同一个
        /// 对象、否则回落到工程里的 CLR 描述符（其 options 可能为空 ⇒ 取到的颜色与
        /// UNote/UPhoneme 不一致）。`VoiceColorExp` 为空或下标越界时记 warning 并返回空串，
        /// 与 UPhoneme 的行为保持一致（上游 2c283d2b + 我们此前的 7a083786 边界检查）。
        /// </summary>
        public string GetParentVoiceColor() {
            if (project == null || track == null) {
                return string.Empty;
            }
            try {
                if (!track.TryGetExpDescriptor(project, Core.Format.Ustx.CLR, out var trackCLR) || trackCLR == null) {
                    return string.Empty;
                }
                int index = (int)trackCLR.CustomDefaultValue;
                if (track.VoiceColorExp == null || track.VoiceColorExp.options == null) {
                    Log.Warning("Track {TrackName} ({TrackNo}) defines CLR expression index {ColorIndex}, but VoiceColorExp options are null or uninitialized.",
                        track.TrackName, track.TrackNo, index);
                    return string.Empty;
                }
                if (index < 0 || index >= track.VoiceColorExp.options.Length) {
                    Log.Warning("Track {TrackName} ({TrackNo}) VoiceColor index {ColorIndex} is out of bounds (options count: {OptionCount}).",
                        track.TrackName, track.TrackNo, index, track.VoiceColorExp.options.Length);
                    return string.Empty;
                }
                return track.VoiceColorExp.options[index] ?? string.Empty;
            } catch (Exception ex) {
                Log.Error(ex, "Failed to resolve Voice Color (CLR) on track {TrackName} ({TrackNo}).",
                    track.TrackName, track.TrackNo);
                throw;
            }
        }

        /// <summary>
        /// Utility method to map a phoneme alias to proper pitch using prefixmap.
        /// For example, MapPhoneme("あ", 72, singer) may return "あC5".
        /// </summary>
        /// <param name="phoneme">Alias before pitch mapping.</param>
        /// <param name="tone">Music tone of note. C4 = 60.</param>
        /// <param name="singer">The singer.</param>
        /// <returns>Mapped alias.</returns>
        public static string MapPhoneme(string phoneme, int tone, string color, string alt, USinger singer) {
            if (singer.TryGetMappedOto(phoneme + alt, tone, color, out var otoAlt)) {
                return otoAlt.Alias;
            }
            if (singer.TryGetMappedOto(phoneme, tone, color, out var oto)) {
                return oto.Alias;
            }
            return phoneme;
        }
    }
}

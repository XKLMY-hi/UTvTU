using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenUtau.Core.Ustx;
using Serilog;
using WanaKanaNet;

namespace OpenUtau.Classic {
    public class ClassicSinger : USinger, IDisposable {
        public override string Id => voicebank.Id;
        public override string Name => voicebank.Name;
        public override Dictionary<string, string> LocalizedNames => voicebank.LocalizedNames;
        public override USingerType SingerType => voicebank.SingerType;
        public override string BasePath => voicebank.BasePath;
        public override string Author => voicebank.Author;
        public override string Voice => voicebank.Voice;
        public override string Location => Path.GetDirectoryName(voicebank.File);
        public override string Web => voicebank.Web;
        public override string Version => voicebank.Version;
        public override string OtherInfo => voicebank.OtherInfo;
        public override IList<string> Errors => data.errors;
        // image 缺失时用 portrait 兜底（很多声库只声明 portrait）
        public override string Avatar {
            get {
                if (!string.IsNullOrEmpty(voicebank.Image)) {
                    return Path.Combine(Location, voicebank.Image);
                }
                if (!string.IsNullOrEmpty(voicebank.Portrait)) {
                    return Path.Combine(Location, voicebank.Portrait);
                }
                return null;
            }
        }
        public override byte[] AvatarData => avatarData;
        public override string Portrait => voicebank.Portrait == null ? null : Path.Combine(Location, voicebank.Portrait);
        public override float PortraitOpacity => voicebank.PortraitOpacity;
        public override int PortraitHeight => voicebank.PortraitHeight;
        public override string DefaultPhonemizer => voicebank.DefaultPhonemizer;
        public override string Sample => voicebank.Sample == null ? null : Path.Combine(Location, voicebank.Sample);
        public override Encoding TextFileEncoding => voicebank.TextFileEncoding;
        public override IList<USubbank> Subbanks => data.subbanks;
        public override IList<UOto> Otos => data.otos;

        /// <summary>释放/重载声库数据时与 FreeMemory 互斥（上游 bfb01058）。</summary>
        public object SessionLock { get; } = new object();

        /// <summary>
        /// Load() 产出的一切，作为**一个不可变单元**整体发布：其它线程上的读者
        /// （音素化器 / 渲染器 / UI）在一次调用里只取一次引用，重载期间永远看不到
        /// "建了一半"的 oto 映射（上游 83e02c7e）。
        /// </summary>
        sealed class OtoData {
            public static readonly OtoData Empty = new OtoData();
            public readonly List<UOtoSet> otoSets = new List<UOtoSet>();
            public readonly List<USubbank> subbanks = new List<USubbank>();
            public readonly List<UOto> otos = new List<UOto>();
            public readonly Dictionary<string, UOto> otoMap = new Dictionary<string, UOto>();
            public readonly List<string> errors = new List<string>();
        }

        Voicebank voicebank;
        byte[] avatarData;
        volatile OtoData data = OtoData.Empty;
        OtoWatcher otoWatcher;

        public bool? UseFilenameAsAlias { get => voicebank.UseFilenameAsAlias; set => voicebank.UseFilenameAsAlias = value; }
        public Dictionary<string, IFrqFiles> Frqs { get; set; } = new Dictionary<string, IFrqFiles>();

        public ClassicSinger(Voicebank voicebank) {
            this.voicebank = voicebank;
            found = true;
        }

        public override void EnsureLoaded() {
            if (Loaded) {
                return;
            }
            Reload();
        }

        public override void Reload() {
            if (!Found) {
                return;
            }
            try {
                voicebank.Reload();
                Load();
                loaded = true;
                if (otoWatcher == null) {
                    otoWatcher = new OtoWatcher(this, Location);
                }
                OtoDirty = false;
            } catch (Exception e) {
                Log.Error(e, $"Failed to load {voicebank.File}");
            }
        }

        void Load() {
            if (Avatar != null && File.Exists(Avatar)) {
                try {
                    using (var stream = new FileStream(Avatar, FileMode.Open, FileAccess.Read)) {
                        using (var memoryStream = new MemoryStream()) {
                            stream.CopyTo(memoryStream);
                            avatarData = memoryStream.ToArray();
                        }
                    }
                } catch (Exception e) {
                    avatarData = null;
                    Log.Error(e, "Failed to load avatar data.");
                }
            } else {
                avatarData = null;
                Log.Error("Avatar can't be found");
            }

            var d = new OtoData();
            d.subbanks.AddRange(voicebank.Subbanks
                .OrderByDescending(subbank => subbank.Prefix.Length + subbank.Suffix.Length)
                .Select(subbank => new USubbank(subbank)));
            var groups = d.subbanks.GroupBy(subbank => $"^{Regex.Escape(subbank.Prefix)}(.*){Regex.Escape(subbank.Suffix)}$")
                .Select(group => new KeyValuePair<Regex, USubbank[]>(new Regex(group.Key), group.ToArray()));

            var dummy = new USubbank[] { new USubbank(new Subbank()) };
            foreach (var otoSet in voicebank.OtoSets) {
                var uSet = new UOtoSet(otoSet, voicebank.BasePath);
                d.otoSets.Add(uSet);
                foreach (var oto in otoSet.Otos) {
                    if (!oto.IsValid) {
                        if (!string.IsNullOrEmpty(oto.Error)) {
                            d.errors.Add(oto.Error);
                        }
                        continue;
                    }
                    UOto? uOto = null;
                    foreach (var group in groups) {
                        var m = group.Key.Match(oto.Alias);
                        if (m.Success) {
                            oto.Phonetic = m.Groups[1].Value;
                            uOto = new UOto(oto, uSet, group.Value);
                            break;
                        }
                    }
                    if (uOto == null) {
                        uOto = new UOto(oto, uSet, dummy);
                    }
                    d.otos.Add(uOto);
                    if (!d.otoMap.ContainsKey(oto.Alias)) {
                        d.otoMap.Add(oto.Alias, uOto);
                    } else {
                        //Errors.Add($"oto conflict {Otos[oto.Alias].Set}/{oto.Alias} and {otoSet.Name}/{oto.Alias}");
                    }
                }
            }

            // 搜索词必须在**发布之前**填好：此前这里 Task.Run 到后台线程去改 otoMap.Values，
            // 与读者（音素化/UI 搜索）并发写 List（上游 83e02c7e 一并修掉）。
            foreach (var oto in d.otoMap.Values) {
                oto.SearchTerms.Add(oto.Alias.ToLowerInvariant().Replace(" ", ""));
                try {
                    oto.SearchTerms.Add(WanaKana.ToRomaji(oto.Alias).ToLowerInvariant().Replace(" ", ""));
                } catch { }
            }

            // 单次原子发布：读者要么看到旧快照，要么看到这个新快照。
            data = d;
        }

        public override void Save() {
            // FreeMemory/Dispose 之后 watcher 可能已被释放；Save 由 UI 触发，
            // 这里按需重建，避免空引用/已释放对象（上游 bfb01058 引入 Dispose 后的边角）。
            otoWatcher ??= new OtoWatcher(this, Location);
            try {
                otoWatcher.Paused = true;
                foreach (var oto in Otos) {
                    oto.WriteBack();
                }
                VoicebankLoader.WriteOtoSets(voicebank);
            } finally {
                otoWatcher.Paused = false;
            }
        }

        /// <summary>断开 oto 目录监视（可重入）。</summary>
        public void Dispose() {
            otoWatcher?.Dispose();
            otoWatcher = null;
        }

        /// <summary>
        /// 释放声库常驻数据。此前**完全未覆写**（基类空实现），于是
        /// <c>SingerManager.ReleaseSingersNotInUse</c> 对经典歌手是空操作：内存只涨不落。
        /// 上一轮我们只敢复位 loaded（清空集合会与并发渲染线程的遍历相撞）；现在
        /// oto 数据以不可变快照发布（83e02c7e），**换快照即可真释放**：读者手里那份
        /// 引用继续有效，新读者看到 Empty，无需任何写锁。
        /// </summary>
        public override void FreeMemory() {
            Log.Information($"Freeing memory for singer {Id}");
            lock (SessionLock) {
                Dispose();
                data = OtoData.Empty;
                loaded = false;
            }
        }

        public override bool TryGetOto(string phoneme, out UOto oto) {
            // 一次取引用：重载/释放只会换掉整个快照，不会改这份数据
            return data.otoMap.TryGetValue(phoneme, out oto);
        }

        public override bool TryGetMappedOto(string phoneme, int tone, out UOto oto) {
            return TryGetMappedOto(data, phoneme, tone, out oto);
        }

        public override bool TryGetMappedOto(string phoneme, int tone, string color, out UOto oto) {
            var d = data;
            var subbank = d.subbanks.Find(subbank => subbank.Color == color && subbank.toneSet.Contains(tone));
            if (subbank != null && d.otoMap.TryGetValue($"{subbank.Prefix}{phoneme}{subbank.Suffix}", out oto)) {
                return true;
            }
            return TryGetMappedOto(d, phoneme, tone, out oto);
        }

        static bool TryGetMappedOto(OtoData d, string phoneme, int tone, out UOto oto) {
            var subbank = d.subbanks.Find(subbank => string.IsNullOrEmpty(subbank.Color) && subbank.toneSet.Contains(tone));
            if (subbank != null && d.otoMap.TryGetValue($"{subbank.Prefix}{phoneme}{subbank.Suffix}", out oto)) {
                return true;
            }
            return d.otoMap.TryGetValue(phoneme, out oto);
        }

        public override IEnumerable<UOto> GetSuggestions(string text) {
            if (text != null) {
                text = text.ToLowerInvariant().Replace(" ", "");
            }
            bool all = string.IsNullOrEmpty(text);
            return data.otoMap.Values
                .Where(oto => all || oto.SearchTerms.Exists(term => term.Contains(text)));
        }

        public override byte[] LoadPortrait() {
            return string.IsNullOrEmpty(Portrait)
                ? null
                : File.ReadAllBytes(Portrait);
        }
    }
}

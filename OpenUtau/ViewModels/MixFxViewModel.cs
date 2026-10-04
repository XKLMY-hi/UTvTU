using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using OpenUtau.Core;
using OpenUtau.Core.SignalChain.Effects;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtau.App.ViewModels {
    /// <summary>
    /// Backing view-model for the per-track Track Polish dialog.
    /// Operates on a single <see cref="UTrack"/>'s <see cref="UMixFx"/> instance.
    /// User presets (full-rack snapshots) live in <see cref="Preferences"/>.
    ///
    /// 每一次编辑都**立即写回轨道**，所以播放时旋钮是即时可听的；
    /// <see cref="Revert"/> 回到窗口打开时的快照（取消 / ESC），
    /// <see cref="Apply"/> 保留当前状态（确定 / 直接关窗）。
    /// </summary>
    public class MixFxViewModel : ViewModelBase {
        public class PresetOption {
            public string Key { get; }
            public string Label { get; }
            public PresetOption(string key, string label) {
                Key = key;
                Label = label;
            }
            public override string ToString() => Label;
        }

        [Reactive] public string TrackName { get; set; }

        public List<PresetOption> EqPresets { get; }
        public List<PresetOption> CompPresets { get; }
        public List<PresetOption> ReverbPresets { get; }

        public ObservableCollection<Preferences.MixFxUserPreset> UserPresets { get; }

        // The synthetic, always-present "Default" library entry.  Cannot be
        // deleted or overwritten and never persisted into Preferences.
        private readonly Preferences.MixFxUserPreset defaultPreset;

        [Reactive] public bool Enabled { get; set; }
        // 模块电源开关（冻结契约：EqEnabled/CompEnabled/ReverbEnabled）。
        // 旧的 EqBypassed 等是反向别名，仅供旧 ustx 迁移，不做绑定。
        [Reactive] public bool EqEnabled { get; set; }
        [Reactive] public bool CompEnabled { get; set; }
        [Reactive] public bool ReverbEnabled { get; set; }
        [Reactive] public PresetOption? SelectedEq { get; set; }
        [Reactive] public PresetOption? SelectedComp { get; set; }
        [Reactive] public PresetOption? SelectedReverb { get; set; }
        [Reactive] public Preferences.MixFxUserPreset? SelectedUserPreset { get; set; }

        [Reactive] public double EqLowDb { get; set; }
        [Reactive] public double EqMidFreq { get; set; }
        [Reactive] public double EqMidDb { get; set; }
        [Reactive] public double EqHighDb { get; set; }

        [Reactive] public double CompThresholdDb { get; set; }
        [Reactive] public double CompRatio { get; set; }
        [Reactive] public double CompMakeupDb { get; set; }

        [Reactive] public double ReverbSize { get; set; }
        [Reactive] public double ReverbDamp { get; set; }
        [Reactive] public double ReverbWet { get; set; }
        [Reactive] public double ReverbPreDelayMs { get; set; }

        [Reactive] public bool ApplyOnExportMixdown { get; set; }

        // True when the currently-selected library entry is user-deletable
        // (anything except the protected Default entry).
        public bool CanDeleteSelectedPreset => canDeleteSelectedPreset.Value;
        private readonly ObservableAsPropertyHelper<bool> canDeleteSelectedPreset;

        public ReactiveCommand<Unit, Unit> ApplyRecommendedCommand { get; }
        public ReactiveCommand<Unit, Unit> SaveUserPresetCommand { get; }
        public ReactiveCommand<Unit, Unit> DeleteUserPresetCommand { get; }

        public Func<Task<string?>>? AskForName;

        private readonly UTrack track;
        // 窗口打开时轨道的 MixFx 快照；Revert（取消 / ESC）时放回。
        private readonly UMixFx? original;
        private bool suspendBindings;
        // 是否真的动过参数（决定关窗时要不要把工程标记为已修改）。
        private bool dirty;

        public MixFxViewModel() : this(null) { }

        public MixFxViewModel(UTrack? track) {
            this.track = track!;
            TrackName = track?.TrackName ?? "Preview";

            EqPresets = FxPresets.EqPresetNames
                .Select(k => new PresetOption(k, PrettyLabel(k)))
                .ToList();
            CompPresets = FxPresets.CompPresetNames
                .Select(k => new PresetOption(k, PrettyLabel(k)))
                .ToList();
            ReverbPresets = FxPresets.ReverbPresetNames
                .Select(k => new PresetOption(k, PrettyLabel(k)))
                .ToList();

            defaultPreset = new Preferences.MixFxUserPreset {
                Name = ThemeManager.GetString("mixfx.library.default"),
                Fx = BuildDefaultFx(),
            };
            UserPresets = new ObservableCollection<Preferences.MixFxUserPreset> { defaultPreset };
            foreach (var p in Preferences.Default.MixFxUserPresets ?? new List<Preferences.MixFxUserPreset>()) {
                UserPresets.Add(p);
            }

            // Seed dialog state from track's existing FX, or sensible defaults.
            original = track?.MixFx;
            var fx = track?.MixFx ?? new UMixFx();
            suspendBindings = true;
            try {
                Enabled = track?.MixFx?.Enabled ?? false;
                EqEnabled = fx.EqEnabled;
                CompEnabled = fx.CompEnabled;
                ReverbEnabled = fx.ReverbEnabled;
                SelectedEq = FindOrFirst(EqPresets, fx.EqPreset);
                SelectedComp = FindOrFirst(CompPresets, fx.CompPreset);
                SelectedReverb = FindOrFirst(ReverbPresets, fx.ReverbPreset);
                EqLowDb = fx.EqLowDb;
                EqMidFreq = fx.EqMidFreq;
                EqMidDb = fx.EqMidDb;
                EqHighDb = fx.EqHighDb;
                CompThresholdDb = fx.CompThresholdDb;
                CompRatio = fx.CompRatio;
                CompMakeupDb = fx.CompMakeupDb;
                ReverbSize = fx.ReverbSize;
                ReverbDamp = fx.ReverbDamp;
                ReverbWet = fx.ReverbWet;
                ReverbPreDelayMs = fx.ReverbPreDelayMs;
                ApplyOnExportMixdown = Preferences.Default.MixFxApplyOnExportMixdown;
            } finally {
                suspendBindings = false;
            }

            // Picking a preset reloads its parameters into the knobs.
            // WhenAnyValue 订阅时会立刻发一次当前值 → 期间挂起绑定，
            // 否则会把轨道已存的值覆盖成预设值（打开即失真）。
            suspendBindings = true;
            try {
                this.WhenAnyValue(x => x.SelectedEq).Subscribe(opt => { if (opt != null) LoadEqPreset(opt.Key); });
                this.WhenAnyValue(x => x.SelectedComp).Subscribe(opt => { if (opt != null) LoadCompPreset(opt.Key); });
                this.WhenAnyValue(x => x.SelectedReverb).Subscribe(opt => { if (opt != null) LoadReverbPreset(opt.Key); });
                this.WhenAnyValue(x => x.SelectedUserPreset).Subscribe(p => { if (p != null) LoadUserPreset(p); });
            } finally {
                suspendBindings = false;
            }

            this.WhenAnyValue(x => x.SelectedUserPreset)
                .Select(p => p != null && !ReferenceEquals(p, defaultPreset))
                .ToProperty(this, x => x.CanDeleteSelectedPreset, out canDeleteSelectedPreset);

            // Live preview: any edit replaces the track's MixFx with a fresh
            // snapshot.  Swapping the reference (rather than mutating it)
            // means the audio thread never sees a half-updated set.
            // TrackName 只是窗口标题的同步（轨道重命名），不是音色参数，不参与回写。
            Changed.Where(e => e.PropertyName != nameof(TrackName)).Subscribe(_ => {
                if (track != null) {
                    track.MixFx = BuildUMixFx();
                }
                dirty = true;
            });

            ApplyRecommendedCommand = ReactiveCommand.Create(ApplyRecommended);
            SaveUserPresetCommand = ReactiveCommand.CreateFromTask(SaveUserPresetAsync);
            DeleteUserPresetCommand = ReactiveCommand.Create(DeleteUserPreset);
        }

        /// <summary>
        /// Routed through the library so that selecting Default → 111 → Default
        /// always fires a change event, fixing the "same item re-selected does
        /// nothing" ComboBox quirk.
        /// </summary>
        public void ApplyRecommended() {
            // If Default is already selected, force a change to re-fire load.
            if (ReferenceEquals(SelectedUserPreset, defaultPreset)) {
                SelectedUserPreset = null;
            }
            SelectedUserPreset = defaultPreset;
        }

        private static UMixFx BuildDefaultFx() {
            var e = FxPresets.Eq["vocal_air"];
            var c = FxPresets.Comp["gentle"];
            var r = FxPresets.Reverb["small_room"];
            return new UMixFx {
                Enabled = true,
                EqEnabled = true, CompEnabled = true, ReverbEnabled = true,
                EqPreset = "vocal_air",
                EqLowDb = e.LowDb, EqMidFreq = e.MidFreq, EqMidDb = e.MidDb, EqHighDb = e.HighDb,
                CompPreset = "gentle",
                CompThresholdDb = c.ThresholdDb, CompRatio = c.Ratio, CompMakeupDb = c.MakeupDb,
                ReverbPreset = "small_room",
                ReverbSize = r.RoomSize, ReverbDamp = r.Damp, ReverbWet = 1.0,
                ReverbPreDelayMs = r.PreDelayMs,
            };
        }

        private void LoadEqPreset(string key) {
            if (suspendBindings) return;
            if (!FxPresets.Eq.TryGetValue(key, out var e)) return;
            suspendBindings = true;
            try {
                EqLowDb = e.LowDb;
                EqMidFreq = e.MidFreq;
                EqMidDb = e.MidDb;
                EqHighDb = e.HighDb;
            } finally {
                suspendBindings = false;
            }
        }
        private void LoadCompPreset(string key) {
            if (suspendBindings) return;
            if (!FxPresets.Comp.TryGetValue(key, out var c)) return;
            suspendBindings = true;
            try {
                CompThresholdDb = c.ThresholdDb;
                CompRatio = c.Ratio;
                CompMakeupDb = c.MakeupDb;
            } finally {
                suspendBindings = false;
            }
        }
        private void LoadReverbPreset(string key) {
            if (suspendBindings) return;
            if (!FxPresets.Reverb.TryGetValue(key, out var r)) return;
            suspendBindings = true;
            try {
                ReverbSize = r.RoomSize;
                ReverbDamp = r.Damp;
                ReverbWet = 1.0;
                ReverbPreDelayMs = r.PreDelayMs;
            } finally {
                suspendBindings = false;
            }
        }

        private void LoadUserPreset(Preferences.MixFxUserPreset p) {
            if (p == null || p.Fx == null) return;
            var fx = p.Fx;
            suspendBindings = true;
            try {
                Enabled = fx.Enabled || Enabled;
                EqEnabled = fx.EqEnabled;
                CompEnabled = fx.CompEnabled;
                ReverbEnabled = fx.ReverbEnabled;
                SelectedEq = FindOrFirst(EqPresets, fx.EqPreset);
                SelectedComp = FindOrFirst(CompPresets, fx.CompPreset);
                SelectedReverb = FindOrFirst(ReverbPresets, fx.ReverbPreset);
                EqLowDb = fx.EqLowDb; EqMidFreq = fx.EqMidFreq; EqMidDb = fx.EqMidDb; EqHighDb = fx.EqHighDb;
                CompThresholdDb = fx.CompThresholdDb; CompRatio = fx.CompRatio; CompMakeupDb = fx.CompMakeupDb;
                ReverbSize = fx.ReverbSize; ReverbDamp = fx.ReverbDamp; ReverbWet = fx.ReverbWet;
                ReverbPreDelayMs = fx.ReverbPreDelayMs;
            } finally {
                suspendBindings = false;
            }
        }

        public async Task SaveUserPresetAsync() {
            if (AskForName == null) return;
            string? name = await AskForName();
            if (string.IsNullOrWhiteSpace(name)) return;
            // Don't allow overwriting the synthetic Default entry by name.
            if (name == defaultPreset.Name) return;
            var snapshot = new Preferences.MixFxUserPreset { Name = name!, Fx = BuildUMixFx() };
            var existing = UserPresets.FirstOrDefault(p => p.Name == name && !ReferenceEquals(p, defaultPreset));
            if (existing != null) {
                int idx = UserPresets.IndexOf(existing);
                UserPresets[idx] = snapshot;
            } else {
                UserPresets.Add(snapshot);
            }
            PersistUserPresets();
            SelectedUserPreset = snapshot;
        }

        public void DeleteUserPreset() {
            if (SelectedUserPreset == null) return;
            if (ReferenceEquals(SelectedUserPreset, defaultPreset)) return;
            UserPresets.Remove(SelectedUserPreset);
            PersistUserPresets();
            SelectedUserPreset = null;
        }

        private void PersistUserPresets() {
            Preferences.Default.MixFxUserPresets = UserPresets
                .Where(p => !ReferenceEquals(p, defaultPreset))
                .ToList();
            Preferences.Save();
        }

        private static string PrettyLabel(string key) {
            if (string.IsNullOrEmpty(key)) return key;
            var parts = key.Split('_');
            for (int i = 0; i < parts.Length; i++) {
                if (parts[i].Length == 0) continue;
                parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1);
            }
            return string.Join(" ", parts);
        }

        private PresetOption FindOrFirst(List<PresetOption> list, string key) {
            return list.FirstOrDefault(o => o.Key == key) ?? list[0];
        }

        public UMixFx BuildUMixFx() {
            return new UMixFx {
                Enabled = Enabled,
                EqEnabled = EqEnabled,
                CompEnabled = CompEnabled,
                ReverbEnabled = ReverbEnabled,
                EqPreset = SelectedEq?.Key ?? FxPresets.Off,
                CompPreset = SelectedComp?.Key ?? FxPresets.Off,
                ReverbPreset = SelectedReverb?.Key ?? FxPresets.Off,
                EqLowDb = EqLowDb, EqMidFreq = EqMidFreq, EqMidDb = EqMidDb, EqHighDb = EqHighDb,
                CompThresholdDb = CompThresholdDb, CompRatio = CompRatio, CompMakeupDb = CompMakeupDb,
                ReverbSize = ReverbSize, ReverbDamp = ReverbDamp, ReverbWet = ReverbWet,
                ReverbPreDelayMs = ReverbPreDelayMs,
            };
        }

        /// <summary>撤销实时预览：把轨道恢复成窗口打开时的 <see cref="UMixFx"/>（取消 / ESC）。</summary>
        public void Revert() {
            if (track != null) {
                // UTrack.MixFx 的注解是非空，但语义上 null 就是"无效果"（旧 ustx 即 null），
                // 所以这里按可空还原打开时的状态。
                track.MixFx = original!;
            }
        }

        /// <summary>保留改动并把工程标记为已修改（确定）。</summary>
        public void Apply() {
            if (track != null) {
                track.MixFx = BuildUMixFx();
            }
            Preferences.Default.MixFxApplyOnExportMixdown = ApplyOnExportMixdown;
            Preferences.Save();
            // 确定会具现化 track.MixFx（原本可能为 null），属于模型变更，无条件标记。
            DocManager.Inst.MarkProjectModified();
        }

        /// <summary>直接关窗（保留改动）：只有真的动过参数才标记工程已修改。</summary>
        public void MarkModifiedIfDirty() {
            if (dirty) {
                DocManager.Inst.MarkProjectModified();
            }
        }
    }
}

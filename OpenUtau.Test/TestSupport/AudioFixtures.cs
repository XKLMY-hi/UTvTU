using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Vst;

namespace OpenUtau.Test.TestSupport {
    /// <summary>
    /// W7 验收装置的夹具层：确定性测试信号 + "波形轨工程"（不依赖伪声库/resampler）+
    /// 走**产品渲染路径**（<see cref="RenderEngine.RenderMixdown"/> → MasterAdapter/ExportSession）
    /// 的取样读取，以及一个可注入的假 VST 桥（证明内置↔VST 链序）。
    ///
    /// 为什么用 UWavePart 而不是 UVoicePart：波形轨的样本直接进混音链
    /// （WaveMix → Fader → MixFxSource → EffectChain(VST) → LevelTracker），
    /// 既覆盖"整条音频链"，又完全不依赖 resampler/原生库/声库——离线沙箱可跑。
    /// 伪声库端到端在 <c>DummyVoicebank</c> 里，条件满足时另行启用。
    /// </summary>
    internal static class AudioFixtures {
        public static int Channels => AudioSettings.Channels;
        public static int Rate => AudioSettings.SampleRate;

        // ── 确定性测试信号（全部交织立体声） ─────────────────────────────

        public static float[] Sine(double freq, double seconds, double amp = 0.2, int channels = 0) {
            int ch = channels == 0 ? Channels : channels;
            int frames = (int)Math.Round(seconds * Rate);
            var data = new float[frames * ch];
            for (int i = 0; i < frames; i++) {
                float v = (float)(amp * Math.Sin(2 * Math.PI * freq * i / Rate));
                for (int c = 0; c < ch; c++) {
                    data[i * ch + c] = v;
                }
            }
            return data;
        }

        public static float[] Silence(double seconds, int channels = 0) {
            int ch = channels == 0 ? Channels : channels;
            return new float[(int)Math.Round(seconds * Rate) * ch];
        }

        /// <summary>确定性伪随机噪声（自写 LCG，跨平台/跨运行一致）。</summary>
        public static float[] Noise(int seed, double seconds, double amp = 0.2, int channels = 0) {
            int ch = channels == 0 ? Channels : channels;
            int frames = (int)Math.Round(seconds * Rate);
            var data = new float[frames * ch];
            uint state = (uint)seed * 2654435761u + 1u;
            for (int i = 0; i < frames; i++) {
                state = state * 1664525u + 1013904223u;
                float v = (float)((state / (double)uint.MaxValue * 2 - 1) * amp);
                for (int c = 0; c < ch; c++) {
                    data[i * ch + c] = v;
                }
            }
            return data;
        }

        public static float[] Concat(params float[][] parts) {
            var result = new float[parts.Sum(p => (long)p.Length)];
            int at = 0;
            foreach (var p in parts) {
                Array.Copy(p, 0, result, at, p.Length);
                at += p.Length;
            }
            return result;
        }

        /// <summary>带淡入淡出的纯音（避免首尾边界本身产生宽频瞬变，污染频谱测量）。</summary>
        public static float[] Tone(double freq, double seconds, double amp = 0.2, double fadeMs = 10) {
            var data = Sine(freq, seconds, amp);
            ApplyFade(data, fadeMs);
            return data;
        }

        public static void ApplyFade(float[] interleaved, double fadeMs) {
            int ch = Channels;
            int fade = Math.Max(1, (int)(fadeMs / 1000.0 * Rate));
            int frames = interleaved.Length / ch;
            for (int i = 0; i < fade && i < frames; i++) {
                float g = (float)i / fade;
                for (int c = 0; c < ch; c++) {
                    interleaved[i * ch + c] *= g;
                    interleaved[(frames - 1 - i) * ch + c] *= g;
                }
            }
        }

        // ── 工程夹具 ─────────────────────────────────────────────────────

        public sealed class TrackSpec {
            /// <summary>交织立体声样本（长度决定器件时长）。</summary>
            public float[] Samples = Array.Empty<float>();
            public double VolumeDb;
            public double Pan;
            public bool Muted;
            /// <summary>false = <c>track.MixFx == null</c>（干轨）。</summary>
            public bool AttachMixFx = true;
            public UMixFx? MixFx;
            public List<VstPluginSlot>? VstSlots;
        }

        public static UProject BuildWaveProject(params TrackSpec[] specs) {
            var project = new UProject();
            OpenUtau.Core.Format.Ustx.AddDefaultExpressions(project);
            project.tracks.Clear();
            for (int i = 0; i < specs.Length; i++) {
                var spec = specs[i];
                var track = new UTrack($"T{i + 1}") {
                    TrackNo = i,
                    Volume = spec.VolumeDb,
                    Pan = spec.Pan,
                    Mute = spec.Muted,
                    Muted = spec.Muted,
                };
                if (spec.AttachMixFx) {
                    track.MixFx = spec.MixFx;
                }
                if (spec.VstSlots != null) {
                    track.VstSlots = spec.VstSlots;
                }
                track.RendererSettings.Validate(track);
                project.tracks.Add(track);
            }
            for (int i = 0; i < specs.Length; i++) {
                var samples = specs[i].Samples;
                var part = new UWavePart {
                    trackNo = i,
                    position = 0,
                    channels = Channels,
                    sampleRate = Rate,
                    fileDurationMs = samples.Length * 1000.0 / (Channels * Rate),
                };
                SetSamples(part, samples);
                project.parts.Add(part);
            }
            project.ValidateFull();
            return project;
        }

        /// <summary><c>UWavePart.Samples</c> 是 private set——测试用反射注入内存样本（不落盘）。</summary>
        static void SetSamples(UWavePart part, float[] samples) {
            var prop = typeof(UWavePart).GetProperty(nameof(UWavePart.Samples))!;
            prop.SetValue(part, samples);
        }

        // ── 渲染取样（产品路径） ─────────────────────────────────────────

        /// <summary>
        /// 走 <see cref="RenderEngine.RenderMixdown"/>（= 导出路径：MixFxMode.Snapshot），
        /// 再用 <see cref="ExportAdapter"/> 逐块读到 <paramref name="samples"/> 个样本。
        /// 数据提前结束时返回已读到的部分（其余为 0）。
        /// </summary>
        public static float[] RenderMixdown(UProject project, int samples, bool applyMixFx = true) {
            EnsureSchedulers();
            var cts = new System.Threading.CancellationTokenSource();
            try {
                var mix = RenderEngine.RenderMixdown(
                    project, System.Threading.Tasks.TaskScheduler.Default, ref cts,
                    wait: true, applyMixFx: applyMixFx).Item1;
                return ReadAll(mix, samples);
            } finally {
                cts.Dispose();
            }
        }

        /// <summary>再套一层 <see cref="MasterAdapter"/>（主推子/主静音的实际位置）。</summary>
        public static float[] RenderThroughMaster(UProject project, int samples, double masterVolumeDb = 0, bool masterMuted = false) {
            EnsureSchedulers();
            var cts = new System.Threading.CancellationTokenSource();
            try {
                var mix = RenderEngine.RenderMixdown(
                    project, System.Threading.Tasks.TaskScheduler.Default, ref cts,
                    wait: true, applyMixFx: true).Item1;
                var master = new MasterAdapter(mix) {
                    // 与 PlaybackManager 主推子完全同一条转换（含 < -16 dB 的曲线段）
                    Scale = masterMuted ? 0 : PlaybackManager.DecibelToVolume(masterVolumeDb),
                };
                return ReadAll(new AdapterProvider(master), samples);
            } finally {
                cts.Dispose();
            }
        }

        public static float[] ReadAll(ISignalSource source, int samples) {
            var adapter = new ExportAdapter(source);
            var buffer = new float[samples];
            int read = 0;
            while (read < samples) {
                int n = adapter.Read(buffer, read, samples - read);
                if (n <= 0) {
                    break;
                }
                read += n;
            }
            return buffer;
        }

        /// <summary>循环读取一段内存样本的 <see cref="ISignalSource"/>（测试用信号源，加法混音约定）。</summary>
        public static ISignalSource LoopingSource(float[] interleaved) => new LoopSource(interleaved);

        sealed class LoopSource : ISignalSource {
            readonly float[] data;
            public LoopSource(float[] data) => this.data = data;
            public bool IsReady(int position, int count) => true;
            public int Mix(int position, float[] buffer, int index, int count) {
                for (int i = 0; i < count; i++) {
                    buffer[index + i] += data[(position + i) % data.Length];
                }
                return position + count;
            }
        }

        sealed class AdapterProvider : ISignalSource {
            readonly NAudio.Wave.ISampleProvider provider;
            public AdapterProvider(NAudio.Wave.ISampleProvider provider) => this.provider = provider;
            public bool IsReady(int position, int count) => true;
            public int Mix(int position, float[] buffer, int index, int count) {
                int n = provider.Read(buffer, index, count);
                return position + n;
            }
        }

        // ── 假 VST（证明内置↔VST 链序；不加载真实插件） ───────────────────

        /// <summary>把信号按 gain 放大的假 VST 桥（线性）。</summary>
        internal sealed class GainVstBridge : IVstBridge {
            readonly float gain;
            public GainVstBridge(float gain) => this.gain = gain;
            public IntPtr Load(string bundlePath) => new(1);
            public void Unload(IntPtr handle) { }
            public string? LastError() => null;
            public bool Setup(IntPtr handle, double sr, int block) => true;
            public bool Activate(IntPtr handle, bool enable) => true;
            public void Process(IntPtr handle, float[] buf, int frames) {
                for (int i = 0; i < frames * 2 && i < buf.Length; i++) {
                    buf[i] *= gain;
                }
            }
            public void Reset(IntPtr handle) { }
            public int GetNumParams(IntPtr handle) => 0;
            public float GetParam(IntPtr handle, int id) => 0f;
            public void SetParam(IntPtr handle, int id, float v) { }
            public string GetParamName(IntPtr handle, int id) => $"P{id}";
            public int GetNumInputs(IntPtr handle) => 2;
            public int GetNumOutputs(IntPtr handle) => 2;
            public IntPtr OpenEditor(IntPtr handle, IntPtr hwnd) => IntPtr.Zero;
            public bool OpenEditorWindow(IntPtr handle) => false;
            public void CloseEditor(IntPtr handle) { }
            public byte[]? SaveState(IntPtr handle) => new byte[] { 1 };
            public bool RestoreState(IntPtr handle, byte[] data) => true;
            public string? Probe(string path) => null;
        }

        /// <summary>把信号硬限幅到 ±threshold 的假 VST 桥（非线性——可判别链序）。</summary>
        internal sealed class ClipVstBridge : IVstBridge {
            readonly float threshold;
            public ClipVstBridge(float threshold) => this.threshold = threshold;
            public IntPtr Load(string bundlePath) => new(1);
            public void Unload(IntPtr handle) { }
            public string? LastError() => null;
            public bool Setup(IntPtr handle, double sr, int block) => true;
            public bool Activate(IntPtr handle, bool enable) => true;
            public void Process(IntPtr handle, float[] buf, int frames) {
                for (int i = 0; i < frames * 2 && i < buf.Length; i++) {
                    buf[i] = Math.Clamp(buf[i], -threshold, threshold);
                }
            }
            public void Reset(IntPtr handle) { }
            public int GetNumParams(IntPtr handle) => 0;
            public float GetParam(IntPtr handle, int id) => 0f;
            public void SetParam(IntPtr handle, int id, float v) { }
            public string GetParamName(IntPtr handle, int id) => $"P{id}";
            public int GetNumInputs(IntPtr handle) => 2;
            public int GetNumOutputs(IntPtr handle) => 2;
            public IntPtr OpenEditor(IntPtr handle, IntPtr hwnd) => IntPtr.Zero;
            public bool OpenEditorWindow(IntPtr handle) => false;
            public void CloseEditor(IntPtr handle) { }
            public byte[]? SaveState(IntPtr handle) => new byte[] { 1 };
            public bool RestoreState(IntPtr handle, byte[] data) => true;
            public string? Probe(string path) => null;
        }

        /// <summary>
        /// 在指定轨道装一个"假 VST"：注册假插件条目 → 换桥 → 加载槽位实例。
        /// 返回句柄：<see cref="FakeVstHandle.Slot"/> 必须放进工程的
        /// <c>track.VstSlots</c>（VstEffect 判旁通用的是**加载时那个 slot 实例**，
        /// 另建一个 slot 对象改 Bypassed 不生效）；Dispose 时卸载实例并恢复真桥。
        /// </summary>
        internal static FakeVstHandle InstallFakeVst(int trackNo, string uid, IVstBridge bridge) {
            VstTestSetup.Register(VstTestSetup.MakeEntry(uid));
            var manager = VstPluginManager.Inst;
            // 取 VST 互斥闸并在句柄 Dispose 时释放（同一线程）：假 VST 的
            // 「安装 → 渲染 → 卸载」整段独占进程级 VstPluginManager（Bridge + (trackNo,slot)
            // 实例表），否则并行 collection 的 VST 用例会顶掉本用例的实例或换掉 Bridge
            // （W23 #3：TrackMixCommandsTest 偶发红正是这一机制）。
            VstTestSetup.EnterGate();
            var saved = manager.Bridge;
            manager.Bridge = bridge;
            var slot = VstTestSetup.CreateSlot(uid);
            var effect = manager.LoadEffect(trackNo, slot);
            if (effect == null) {
                manager.Bridge = saved;
                VstTestSetup.ExitGate();
                throw new InvalidOperationException($"假 VST 加载失败：{uid}");
            }
            return new FakeVstHandle(manager, saved, trackNo, slot);
        }

        internal sealed class FakeVstHandle : IDisposable {
            readonly VstPluginManager manager;
            readonly IVstBridge savedBridge;
            readonly int trackNo;
            public VstPluginSlot Slot { get; }

            public FakeVstHandle(VstPluginManager manager, IVstBridge savedBridge, int trackNo, VstPluginSlot slot) {
                this.manager = manager;
                this.savedBridge = savedBridge;
                this.trackNo = trackNo;
                Slot = slot;
            }

            public void Dispose() {
                manager.UnloadEffect(trackNo, Slot.SlotIndex);
                manager.Bridge = savedBridge;
                VstTestSetup.ExitGate();   // 与 InstallFakeVst 的 EnterGate 配对（同一线程）
            }
        }

        // ── DocManager 调度器（ExportSession 需要 MainScheduler） ─────────

        static bool schedulersReady;
        static bool schedulerSnapshotTaken;
        static System.Threading.Thread? savedMainThread;
        static System.Threading.Tasks.TaskScheduler? savedMainScheduler;

        static FieldInfo MainThreadField => typeof(DocManager)
            .GetField("mainThread", BindingFlags.NonPublic | BindingFlags.Instance)!;
        static FieldInfo MainSchedulerField => typeof(DocManager)
            .GetField("mainScheduler", BindingFlags.NonPublic | BindingFlags.Instance)!;

        /// <summary>
        /// <c>RenderEngine</c> 的 fault continuation 需要非 null 的
        /// <c>DocManager.MainScheduler</c>；<c>ExportSession</c> 也走这条路。
        /// 测试里把它接到线程池，并把 <c>mainThread</c> 指到当前线程
        /// （<c>ExecuteCmd</c> 才会就地执行而不是抛 NRE）。
        ///
        /// **全局态纪律**：改动前先快照原值，集合结束时由
        /// <see cref="RestoreSchedulers"/>（经 <c>AudioFixtureCollection</c> 的 collection
        /// fixture）还原——否则这些进程级字段会泄漏到并行执行的其它集合，
        /// 让它们的命令投递/就地执行行为随机改变（W14 修复的正是这一类 flake）。
        /// </summary>
        public static void EnsureSchedulers() {
            if (schedulersReady) {
                return;
            }
            if (!schedulerSnapshotTaken) {
                savedMainThread = (System.Threading.Thread?)MainThreadField.GetValue(DocManager.Inst);
                savedMainScheduler = (System.Threading.Tasks.TaskScheduler?)MainSchedulerField.GetValue(DocManager.Inst);
                schedulerSnapshotTaken = true;
            }
            MainThreadField.SetValue(DocManager.Inst, System.Threading.Thread.CurrentThread);
            MainSchedulerField.SetValue(DocManager.Inst, System.Threading.Tasks.TaskScheduler.Default);
            schedulersReady = true;
        }

        /// <summary>还原 <c>mainThread</c>/<c>mainScheduler</c>（幂等，可重复调用）。</summary>
        public static void RestoreSchedulers() {
            if (!schedulerSnapshotTaken) {
                return;
            }
            MainThreadField.SetValue(DocManager.Inst, savedMainThread);
            MainSchedulerField.SetValue(DocManager.Inst, savedMainScheduler);
            schedulersReady = false;
            schedulerSnapshotTaken = false;
            savedMainThread = null;
            savedMainScheduler = null;
        }
    }
}

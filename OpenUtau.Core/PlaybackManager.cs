using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using OpenUtau.Audio;
using OpenUtau.Core.Render;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtau.Core.Format;
using Serilog;

namespace OpenUtau.Core {
    public class SineGenerator : ISampleProvider {
        public WaveFormat WaveFormat => waveFormat;
        private WaveFormat waveFormat;

        private readonly double attackSampleCount;
        private readonly double releaseSampleCount;
        private int startSampleOffset;

        public double freq { get; set; }

        private int position;
        private int releasePosition = 0;
        private float gain = 1;

        public bool isActive { get; private set; } = true;
        public bool isPlaying { get; private set; } = true;

        public SineGenerator(double freq, float gain, int attackMs = 25, int releaseMs = 25) {
            waveFormat = SignalChain.AudioSettings.CreateIeeeFloatWaveFormat();
            this.freq = freq;
            this.gain = gain;
            position = 0;

            // Number of samples the attack & release fades take
            attackSampleCount = (attackMs / 1000.0f) * waveFormat.SampleRate;
            releaseSampleCount = (releaseMs / 1000.0f) * waveFormat.SampleRate;
        }

        public SineGenerator(double freq, float gain, int attackMs, int releaseMs, int startSampleOffset)
            : this(freq, gain, attackMs, releaseMs) {
            this.startSampleOffset = Math.Max(0, startSampleOffset);
        }

        public void SetGain(float gain) {
            this.gain = gain;
        }

        public int Read(float[] buffer, int offset, int count) {
            // Duplicate sample across two channels
            for (int i = 0; i < count / 2; i++) {
                float sample = i < startSampleOffset ? 0 : GetNextSample();
                buffer[offset + (i * 2)] += (float)sample * gain;
                buffer[offset + (i * 2) + 1] += (float)sample * gain;
            }
            startSampleOffset = Math.Max(0, startSampleOffset - count / 2);
            return count;
        }

        private float GetNextSample() {
            double delta = 2 * Math.PI * freq / waveFormat.SampleRate;
            double sample = Math.Sin(position * delta);

            // Calculate attack envelope
            sample *= Math.Clamp(position / attackSampleCount, 0, 1);

            // Calculate release envelope
            double releaseEnvelope = 1;
            if (!isActive) {
                releaseEnvelope = Math.Clamp(1.0f - ((position - releasePosition) / releaseSampleCount), 0, 1);
            }
            sample *= releaseEnvelope;

            if (releaseEnvelope < double.Epsilon) {
                // Stop sampling this generator if release is completed
                // Instance will be cleaned up later
                isPlaying = false;
            }

            position++;
            return (float)sample * gain;
        }

        public void Stop() {
            if (!isActive) return;

            isActive = false;
            releasePosition = position;
        }
    }

    public class ToneGenerator : ISignalSource {
        private Dictionary<double, SineGenerator> activeFrequencies = new Dictionary<double, SineGenerator>();
        private List<SineGenerator> inactiveFrequencies = new List<SineGenerator>();
        private float gain = 0.4f;

        private readonly object _lockObj = new object();

        public ToneGenerator() {}

        public ToneGenerator(float gain) {
            this.gain = gain;
        }

        public void SetGain(float gain) {
            this.gain = gain;
            lock (_lockObj) {
                foreach (var generator in activeFrequencies.Values) {
                    generator.SetGain(gain);
                }
                foreach (var generator in inactiveFrequencies) {
                    generator.SetGain(gain);
                }
            }
        }

        public bool IsReady(int position, int count) {
            return true;
        }

        public int Mix(int position, float[] buffer, int offset, int count) {
            lock (_lockObj) {
                foreach (var freqEntry in activeFrequencies) {
                    if (freqEntry.Value.isPlaying) {
                        freqEntry.Value.Read(buffer, offset, count);
                    }
                }
                foreach (var generator in inactiveFrequencies) {
                    if (generator.isPlaying) {
                        generator.Read(buffer, offset, count);
                    }
                }
            }

            return position + count;
        }
        public void StartTone(double freq) {
            lock (_lockObj) {
                if (activeFrequencies.ContainsKey(freq)) {
                    if (activeFrequencies[freq].isActive) {
                        // Don't cut off tone to replace with the same frequency
                        // Should never happen
                        return;
                    }
                }
                activeFrequencies[freq] = new SineGenerator(freq, gain);
            }
        }

        public void StartTone(double freq, int attackMs, int releaseMs, int startSampleOffset) {
            lock (_lockObj) {
                if (activeFrequencies.ContainsKey(freq)) {
                    if (activeFrequencies[freq].isActive) {
                        return;
                    }
                }
                activeFrequencies[freq] = new SineGenerator(freq, gain, attackMs, releaseMs, startSampleOffset);
            }
        }

        public void StartTones(int startSampleOffset, params (double freq, int attackMs, int releaseMs)[] tones) {
            foreach (var tone in tones) {
                StartTone(tone.freq, tone.attackMs, tone.releaseMs, startSampleOffset);
            }
        }

        public void EndTones(params double[] freqs) {
            foreach (var freq in freqs) {
                EndTone(freq);
            }
        }

        public void EndTone(double freq) {
            lock (_lockObj) {
                if (activeFrequencies.ContainsKey(freq)) {
                    activeFrequencies[freq].Stop();
                    // Move to inactive frequencies list
                    inactiveFrequencies.Add(activeFrequencies[freq]);
                    activeFrequencies.Remove(freq);
                }
            }
            CleanupTones();
        }

        public void EndAllTones() {
            lock (_lockObj) {
                foreach (var tone in activeFrequencies.Values) {
                    tone.Stop();
                    inactiveFrequencies.Add(tone);
                }
                activeFrequencies.Clear();
            }
            CleanupTones();
        }

        private void CleanupTones() {
            lock (_lockObj) {
                inactiveFrequencies.RemoveAll(gen => !gen.isPlaying);
            }
        }
    }

    /// <summary>
    /// 节拍器叠加层：包住主混音（MasterAdapter），先混主输出，再按有效播放位置
    /// （扣除渲染等待静音）把节拍器 click 叠加进同一 buffer，返回 master 的读取数。
    /// master 耗尽（n=0）即输出自然停止——无需上游 MasterExhausted/playbackEndTick 逻辑。
    /// </summary>
    class PlaybackOverlay : ISampleProvider {
        private readonly MasterAdapter master;
        private readonly MetronomeEngine metronome;
        // 播放起点对应的全局样本位置（float 数），供节拍器换算全局时间轴毫秒
        private readonly int startOffsetSamples;
        private int position;

        public WaveFormat WaveFormat => master.WaveFormat;

        public PlaybackOverlay(MasterAdapter master, MetronomeEngine metronome, double startMs) {
            this.master = master;
            this.metronome = metronome;
            startOffsetSamples = (int)Math.Round(startMs * master.WaveFormat.SampleRate / 1000.0) * master.WaveFormat.Channels;
        }

        public int Read(float[] buffer, int offset, int count) {
            int n = master.Read(buffer, offset, count);
            // 有效全局位置 = 起点偏移 + 已读样本数 - 渲染等待静音（等待期间播放时间冻结）
            int effective = startOffsetSamples + position - master.Waited;
            metronome.Mix(effective, buffer, offset, n);
            position += n;
            return n;
        }
    }

    public class PlaybackManager : SingletonBase<PlaybackManager>, ICmdSubscriber {
        private PlaybackManager() {
            DocManager.Inst.AddSubscriber(this);
            try {
                Directory.CreateDirectory(PathManager.Inst.CachePath);
                RenderEngine.ReleaseSourceTemp();
            } catch (Exception e) {
                Log.Error(e, "Failed to release source temp.");
            }

            toneGenerator = new ToneGenerator();
            metronomeEngine = new MetronomeEngine();
            metronomeEngine.SetEnabled(Preferences.Default.Metronome);
            editingMix = new MasterAdapter(toneGenerator);
        }

        public readonly ToneGenerator toneGenerator;
        private readonly MetronomeEngine metronomeEngine;
        // 短语渲染缓存由合成层（RenderEngine）自己持有——播放层只通过静态门面触发
        // 整体失效（RenderEngine.InvalidatePhraseCache），不再持有合成内部状态。
        List<Fader> faders;
        MasterAdapter masterMix;
        MasterAdapter editingMix;
        
        double startMs;
        public int StartTick => DocManager.Inst.Project.timeAxis.MsPosToTickPos(startMs);
        CancellationTokenSource renderCancellation;
        // 播放渲染周期号：旧周期完成时校验是否已被更新请求取代（TOCTOU 防护）
        int renderEpoch;

        // Loop playback state
        private int loopStartTick = 0;
        private int loopEndTick = -1;

        public Audio.IAudioOutput AudioOutput { get; set; } = new Audio.DummyAudioOutput();
        public bool OutputActive => AudioOutput.PlaybackState == PlaybackState.Playing;
        public bool StartingToPlay { get; private set; }
        public bool PlayingMaster { get; private set; }
        public bool Metronome {
            get => metronomeEngine.Enabled;
            set {
                if (metronomeEngine.Enabled == value) {
                    return;
                }
                Preferences.Default.Metronome = value;
                Preferences.Save();
                metronomeEngine.SetEnabled(
                    value,
                    PlayingMaster ? DocManager.Inst.Project.timeAxis : null,
                    PlayingMaster ? DocManager.Inst.playPosTick : -1);
            }
        }

        // 当前 Init 的 provider 包装层——Stop 后靠它确认音频回调线程已退出在飞 Read（B1 竞态屏障）
        private Audio.CallbackTrackedSampleProvider? trackedProvider;

        /// <summary>统一换源入口：包装 provider 供 drain 追踪。</summary>
        private void InitOutput(ISampleProvider provider) {
            var tracked = new Audio.CallbackTrackedSampleProvider(provider);
            trackedProvider = tracked;
            AudioOutput.Init(tracked);
        }

        /// <summary>
        /// 阻塞直到音频回调线程退出在飞 Read（须在 AudioOutput.Stop() 之后调用）。
        /// Stop 后设备不再拉取新数据，在飞的 Read 会在毫秒级完成退出；
        /// 超时保护（回调卡死时不阻塞 UI），超时记日志继续。
        /// </summary>
        private void WaitForCallbackDrain() {
            var provider = trackedProvider;
            if (provider == null) return;
            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            var spin = new System.Threading.SpinWait();
            while (provider.InCallback) {
                if (sw.ElapsedMilliseconds > 2000) {
                    Log.Warning("WaitForCallbackDrain timed out — audio callback stuck in Read.");
                    return;
                }
                spin.SpinOnce();
            }
        }

        // 混音台主推子（masterMix 创建前暂存，StartPlayback 时应用）
        private double masterVolumeDb = 0;
        public bool MasterMuted { get; private set; }

        public void ApplyMasterVolume(double db) {
            masterVolumeDb = db;
            if (masterMix != null) {
                masterMix.Scale = MasterMuted ? 0 : DecibelToVolume(db);
            }
        }

        public void SetMasterMuted(bool muted) {
            MasterMuted = muted;
            if (masterMix != null) {
                masterMix.Scale = muted ? 0 : DecibelToVolume(masterVolumeDb);
            }
        }

        /// <summary>主输出峰值 dB（-60..0，主推子后实际输出），混音台主电平表用。</summary>
        public float ReadMasterLevelDb() {
            return masterMix?.ReadAndResetPeakDb() ?? -60f;
        }

        public void PlayTestSound() {
            masterMix = null;
            PlayingMaster = false;
            AudioOutput.Stop();
            InitOutput(new SignalGenerator(SignalChain.AudioSettings.SampleRate, 1).Take(TimeSpan.FromSeconds(1)));
            AudioOutput.Play();
        }

        public void PlayMetronomeClick() {
            masterMix = null;
            PlayingMaster = false;
            toneGenerator.EndAllTones();
            AudioOutput.Stop();
            InitOutput(new MixingSampleProvider(new ISampleProvider[] {
                CreateMetronomePreviewTone(Preferences.Default.MetronomeHighFrequency, TimeSpan.Zero),
                CreateMetronomePreviewTone(Preferences.Default.MetronomeLowFrequency, TimeSpan.FromMilliseconds(300)),
            }) {
                ReadFully = true,
            });
            AudioOutput.Play();
        }

        private static ISampleProvider CreateMetronomePreviewTone(double frequency, TimeSpan delay) {
            return new OffsetSampleProvider(new SineGenerator(frequency, GetMetronomePreviewGain(), 5, 80)) {
                DelayBy = delay,
                Take = TimeSpan.FromMilliseconds(120),
            };
        }

        private static float GetMetronomePreviewGain() {
            return MathF.Sqrt(Math.Clamp(Preferences.Default.MetronomeVolume / 100f, 0f, 1f));
        }

        public static float GetMetronomeGain() {
            return GetMetronomePreviewGain();
        }

        /// <summary>
        /// Windows 自带钢琴音源（MIDI 合成器 GM 钢琴）——可用时优先；无 MIDI 设备
        /// 时回退默认正弦波（ToneGenerator）。
        /// </summary>
        private readonly MidiTonePlayer _midiTone = new();

        public void PlayTone(double freq) {
            if (_midiTone.IsAvailable) {
                // MIDI 路径独立发声，无需 editingMix 音频链
                _midiTone.PlayTone(FreqToNote(freq));
                return;
            }
            toneGenerator.StartTone(freq);

            // If nothing is playing, start editing mix
            if (!OutputActive) {
                AudioOutput.Stop();
                InitOutput(editingMix);
                AudioOutput.Play();
            }
        }

        public void EndTone(double freq) {
            if (_midiTone.IsAvailable) {
                _midiTone.EndTone(FreqToNote(freq));
                return;
            }
            toneGenerator.EndTone(freq);
        }

        public void EndAllTones() {
            if (_midiTone.IsAvailable) {
                _midiTone.EndAllTones();
                return;
            }
            toneGenerator.EndAllTones();
        }

        static int FreqToNote(double freq) {
            // MIDI note = 69 + 12*log2(freq/440)
            return (int)Math.Clamp(Math.Round(69 + 12 * Math.Log2(freq / 440.0)), 0, 127);
        }

        public void PlayFile(string file) {
            masterMix = null;
            if (AudioOutput.PlaybackState == PlaybackState.Playing) {
                AudioOutput.Stop();
            }
            try{
                var playSound = Wave.OpenFile(file);
                InitOutput(playSound.ToSampleProvider());
            } catch (Exception ex) {
                Log.Error(ex, $"Failed to load sample {file}.");
                return;
            }
            AudioOutput.Play();
        }

        // ── 独立试听通道（NAudio WaveOutEvent，不影响主 AudioOutput/工程播放）──

        private WaveOutEvent? previewOutput;
        private string? previewPath;
        /// <summary>当前试听文件（null = 无试听）。</summary>
        public string? PreviewPath => previewPath;
        /// <summary>试听状态变化（开始/停止/播完）——参数为当前试听文件或 null。</summary>
        public event Action<string?>? PreviewChanged;

        public void PlayPreview(string file) {
            StopPreview();
            try {
                var playSound = Wave.OpenFile(file);
                var output = new WaveOutEvent();
                output.Init(playSound);
                output.PlaybackStopped += (_, _) => {
                    if (ReferenceEquals(previewOutput, output)) {
                        previewOutput = null;
                        previewPath = null;
                        PreviewChanged?.Invoke(null);
                    }
                    output.Dispose();
                };
                previewOutput = output;
                previewPath = file;
                output.Play();
                PreviewChanged?.Invoke(previewPath);
            } catch (Exception ex) {
                Log.Error(ex, $"Failed to play preview {file}.");
                StopPreview();
            }
        }

        public void StopPreview() {
            if (previewOutput is { } output) {
                previewOutput = null;
                previewPath = null;
                output.Stop();
                output.Dispose();
                PreviewChanged?.Invoke(null);
            }
        }

        public void PlayOrPause(int tick = -1, int endTick = -1, int trackNo = -1) {
            // 导出录制中：禁止播放操作（防止打断录制通道/双渲染并发）
            if (IsRecording) return;
            if (PlayingMaster) {
                PausePlayback();
            } else {
                int rangeStart = DocManager.Inst.rangeStartTick;
                int rangeEnd = DocManager.Inst.rangeEndTick;
                if (rangeEnd > rangeStart) {
                    int playPos = DocManager.Inst.playPosTick;
                    loopStartTick = rangeStart;
                    loopEndTick = rangeEnd;
                    Play(
                        DocManager.Inst.Project,
                        tick: tick == -1 ? ((playPos >= rangeStart && playPos < rangeEnd) ? playPos : rangeStart) : tick,
                        endTick: endTick == -1 ? rangeEnd : endTick,
                        trackNo: trackNo);
                } else {
                    loopEndTick = -1;
                    Play(
                        DocManager.Inst.Project,
                        tick: tick == -1 ? DocManager.Inst.playPosTick : tick,
                        endTick: endTick,
                        trackNo: trackNo);
                }
            }
        }

        public void Play(UProject project, int tick, int endTick = -1, int trackNo = -1) {
            if (AudioOutput.PlaybackState == PlaybackState.Paused) {
                PlayingMaster = true;
                metronomeEngine.StartPlayback(project.timeAxis, DocManager.Inst.playPosTick);
                AudioOutput.Play();
                return;
            }
            AudioOutput.Stop();
            Render(project, tick, endTick, trackNo);
            StartingToPlay = true;
            PlayingMaster = true;
        }

        public void StopPlayback() {
            AudioOutput.Stop();
            // 旧回调线程退出后才允许释放其引用的资源（VST 延迟销毁的 Flush 安全点之一）
            WaitForCallbackDrain();
            // 频谱总线清零（drain 后安全——无在飞 AddSamples）
            SignalChain.SpectrumBus.Inst.Reset();
            PlayingMaster = false;
            metronomeEngine.Stop();
            loopEndTick = -1;
            TrackLevels.Clear();
            // 失效旧渲染的 faders 引用（OnNext 已有 null 检查——seek/停止后音量/声像通知不再写入过期链）
            faders = null;
            // 安全点：输出已停 + 回调已 drain，可释放延迟销毁的 VST handle
            Vst.VstPluginManager.Inst.TryFlushAllPendingDispose();
        }

        public void PausePlayback() {
            AudioOutput.Pause();
            // 暂停期间回调已停——清频谱，避免恢复时显示旧帧
            SignalChain.SpectrumBus.Inst.Reset();
            PlayingMaster = false;
            metronomeEngine.Stop();
            loopEndTick = -1;
        }

        private void StartPlayback(double startMs, MasterAdapter masterAdapter) {
            toneGenerator.EndAllTones();

            this.startMs = startMs;
            metronomeEngine.StartPlayback(DocManager.Inst.Project.timeAxis, StartTick);
            var start = TimeSpan.FromMilliseconds(startMs);
            Log.Information($"StartPlayback at {start}");
            masterMix = masterAdapter;
            masterMix.Scale = MasterMuted ? 0 : DecibelToVolume(masterVolumeDb);
            AudioOutput.Stop();
            // 换源前确认旧回调线程已退出（B1 竞态屏障——旧链上的 VST handle 可安全延迟销毁）
            WaitForCallbackDrain();
            // 频谱总线清零（seek/循环重渲染——清掉旧链残块，避免新播放头显示上一渲染的频谱）
            SignalChain.SpectrumBus.Inst.Reset();
            // 安全点：换源完成、新链 Init 前，释放旧链延迟销毁的 VST handle
            Vst.VstPluginManager.Inst.TryFlushAllPendingDispose();
            InitOutput(new PlaybackOverlay(masterMix, metronomeEngine, startMs));
            AudioOutput.Play();
        }

        private void Render(UProject project, int tick, int endTick, int trackNo) {
            Task.Run(() => {
                try {
                    // 周期号：并发/连续 Play 时，旧周期晚到不得覆盖新周期（TOCTOU）
                    int myEpoch = Interlocked.Increment(ref renderEpoch);
                    // 合成层静态门面：不构造引擎、不持有缓存（两批策略在合成层内部）
                    var result = RenderEngine.RenderProject(
                        project, DocManager.Inst.MainScheduler, ref renderCancellation,
                        startTick: tick, endTick: endTick, trackNo: trackNo);
                    if (result == null) {
                        // 被新渲染周期取消——不启动旧链（C-3 两批策略的取消保护）
                        return;
                    }
                    // 周期校验：本周期开始后有更新的播放请求（周期号前进）→ 不启动旧链
                    if (myEpoch != Volatile.Read(ref renderEpoch)) {
                        return;
                    }
                    faders = result.Item2;
                    // 顺序修正（上游 e34dbb43）：先切输出、后落 StartingToPlay——反序会让
                    // "首次播放瞬间被判已停止"（UI 立刻回落停止态，表现为按下播放立即停）。
                    StartPlayback(project.timeAxis.TickPosToMsPos(tick), result.Item1);
                    StartingToPlay = false;
                } catch (Exception e) {
                    Log.Error(e, "Failed to render.");
                    StopPlayback();
                    var customEx = new MessageCustomizableException("Failed to render.", "<translate:errors.failed.render>", e);
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
                }
            });
        }

        public void UpdatePlayPos() {
            if (AudioOutput != null && AudioOutput.PlaybackState == PlaybackState.Playing && PlayingMaster) {
                // 硬件位置（字节）→ float 采样数 → 扣除等待静音（帧数）→ ms（格式显式化）
                double ms = (AudioOutput.GetPosition() / sizeof(float) - masterMix.Waited / masterMix.WaveFormat.Channels)
                    * 1000.0 / masterMix.WaveFormat.SampleRate;
                int tick = DocManager.Inst.Project.timeAxis.MsPosToTickPos(startMs + ms);
                if (loopEndTick > 0 && tick >= loopEndTick) {
                    // Loop back to range start
                    Play(DocManager.Inst.Project, tick: loopStartTick, endTick: loopEndTick);
                    return;
                }
                DocManager.Inst.ExecuteCmd(new SetPlayPosTickNotification(tick, masterMix.IsWaiting));
            } else if (AudioOutput != null && AudioOutput.PlaybackState == PlaybackState.Stopped && PlayingMaster) {
                // Playback stopped (e.g. silence at end). Check if we should loop.
                if (loopEndTick > 0) {
                    Play(DocManager.Inst.Project, tick: loopStartTick, endTick: loopEndTick);
                    return;
                }
            }
        }

        public static float DecibelToVolume(double db) {
            return (db <= -24) ? 0 : (float)MusicMath.DecibelToLinear((db < -16) ? db * 2 + 16 : db);
        }

        // Exporting mixdown
        public async Task RenderMixdown(UProject project, string exportPath) {
            try {
                await RecordMixdown(project, exportPath, 0, -1, null, default);
            } catch (IOException ioe) {
                var customEx = new MessageCustomizableException($"Failed to export {exportPath}.", $"<translate:errors.failed.export>: {exportPath}", ioe);
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
            } catch (Exception e) {
                // 渲染取消/无设备/路径不可写等——async void 菜单入口无捕获，这里必须转用户可见错误
                var customEx = new MessageCustomizableException("Failed to render.", $"<translate:errors.failed.render>: {exportPath}", e);
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
            }
        }

        /// <summary>导出录制中（禁止播放操作，防止打断录制通道）。</summary>
        int recordingSession;
        public bool IsRecording => Volatile.Read(ref recordingSession) != 0;

        /// <summary>
        /// 录制式混音导出：导出期间**直接禁用前台音频管线**（主 AudioOutput 临时换
        /// Dummy——彻底无声，任何播放操作都无声），录制用独立 WasapiOut 通道驱动
        /// 信号链（与预览完全同路径的 VST 激活时序）——RecordingAdapter 分流写文件。
        /// 独立通道转 PCM16（设备格式匹配，避免 float 播放破音）+ silentOutput 静音
        /// （双保险前台无声）；文件为 float 32bit WAV，与预览一致（含 VST 效果）。
        /// </summary>
        public Task RecordMixdown(UProject project, string exportPath, int startTick = 0, int endTick = -1,
                                  IProgress<double>? progress = null, CancellationToken ct = default) {
            // 同步置位（Task.Run 调度窗口内 PlayOrPause 可能放行一次播放渲染互踩）
            if (ct.IsCancellationRequested) return Task.FromCanceled(ct);
            if (Interlocked.CompareExchange(ref recordingSession, 1, 0) != 0) {
                return Task.FromException(new InvalidOperationException("An export recording is already running."));
            }
            var task = Task.Run(() => {
                var savedOutput = AudioOutput;
                CancellationTokenSource recordingCancellation = null;
                try {
                    ct.ThrowIfCancellationRequested();
                    StopPlayback();
                    // ── 直接禁用前台音频管线：主输出换 Dummy（彻底静音） ──
                    AudioOutput = new Audio.DummyAudioOutput();

                    if (ct.IsCancellationRequested) {
                        throw new OperationCanceledException(ct);
                    }
                    var result = RenderEngine.RenderProject(
                        project, DocManager.Inst.MainScheduler, ref recordingCancellation, ct,
                        startTick: startTick, endTick: endTick);
                    if (result == null) {
                        throw new OperationCanceledException(ct);
                    }
                    if (ct.IsCancellationRequested) {
                        // 渲染阶段取消（等批 1 完成才返回）——不写文件
                        throw new OperationCanceledException(ct);
                    }
                    faders = result.Item2;
                    StartingToPlay = false;
                    var master = result.Item1;
                    master.Scale = MasterMuted ? 0 : DecibelToVolume(masterVolumeDb);

                    // 录制时长（endTick=-1 → 全曲）
                    int realEnd = endTick == -1 ? project.EndTick : endTick;
                    double startMs = project.timeAxis.TickPosToMsPos(startTick);
                    double endMs = project.timeAxis.TickPosToMsPos(realEnd);
                    double totalMs = Math.Max(0, endMs - startMs);

                    AtomicFile.Write(exportPath, temporaryPath => {
                        using var writer = new WaveFileWriter(File.Create(temporaryPath), master.WaveFormat);
                        // 静音输出（双保险）——设备播放静音，用户听不到
                        var recorder = new SignalChain.RecordingAdapter(master, writer, silentOutput: true);
                        // 独立音频线路：PCM16 转换（设备格式匹配，杜绝 float 破音）+ WasapiOut
                        using var output = new NAudio.Wave.WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, 100);
                        output.Init(new NAudio.Wave.SampleProviders.SampleToWaveProvider16(recorder));
                        output.Play();

                        // 录制消费段进入在飞计数——Flush 不得释放正在被录制回调消费的 VST handle
                        //（TryFlush 另有 IsRecording 条件，双保险）
                        bool cancelled = false;
                        using (RenderGate.Enter()) {
                            // 等待录制完成（时长或取消）
                            var sw = System.Diagnostics.Stopwatch.StartNew();
                            while (sw.ElapsedMilliseconds < totalMs && !ct.IsCancellationRequested) {
                                Thread.Sleep(50);
                                if (progress != null) {
                                    progress.Report(Math.Min(1.0, sw.ElapsedMilliseconds / totalMs));
                                }
                            }
                            cancelled = ct.IsCancellationRequested;
                        }
                        output.Stop();
                        if (cancelled) {
                            throw new OperationCanceledException(ct);
                        }
                        ct.ThrowIfCancellationRequested();
                    });
                    progress?.Report(1.0);
                } finally {
                    AudioOutput = savedOutput;
                    // 失效导出链的 faders 引用（防 Volume/Pan 通知写入死链）
                    faders = null;
                    RenderCancellationLease.Retire(ref recordingCancellation);
                    Volatile.Write(ref recordingSession, 0);
                }
            }); // Always execute cleanup, even when cancellation wins before scheduling.
            return task;
        }

        // Exporting each tracks
        public async Task RenderToFiles(UProject project, string exportPath) {
            var session = new Export.ExportSession(project, exportPath,
                new Export.ExportSession.Options { PerTrack = true, ApplyMixFx = false });
            await RunExportSession(session, exportPath);
        }

        /// <summary>分轨导出（干轨，无 VST——与播放路径无关，离线安全）。</summary>
        private async Task RunExportSession(Export.ExportSession session, string exportPath) {
            await Task.Run(() => {
                string file = "";
                try {
                    session.RunAsync(new Progress<Export.ExportSession.ProgressInfo>(info => {
                        string msg = info.Percent >= 1
                            ? $"Exported to {info.CurrentFile}."
                            : $"Exporting to {info.CurrentFile}.";
                        DocManager.Inst.ExecuteCmd(new ProgressBarNotification(info.Percent * 100, msg));
                    })).GetAwaiter().GetResult();
                    file = exportPath;
                    // 安全点：导出消费段已退出
                    Vst.VstPluginManager.Inst.TryFlushAllPendingDispose();
                } catch (IOException ioe) {
                    var customEx = new MessageCustomizableException($"Failed to export {file}.", $"<translate:errors.failed.export>: {file}", ioe);
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
                    DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, $"Failed to export {file}."));
                } catch (Exception e) {
                    var customEx = new MessageCustomizableException("Failed to render.", "<translate:errors.failed.render>", e);
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
                    DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, $"Failed to render."));
                }
            });
        }

        void SchedulePreRender() {
            Log.Information("SchedulePreRender");
            RenderEngine.PreRender(DocManager.Inst.Project);
        }

        #region ICmdSubscriber

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is SeekPlayPosTickNotification) {
                var _cmd = cmd as SeekPlayPosTickNotification;
                StopPlayback();
                int tick = _cmd!.playPosTick;
                DocManager.Inst.ExecuteCmd(new SetPlayPosTickNotification(tick, false, _cmd.pause));
            } else if (cmd is VolumeChangeNotification) {
                var _cmd = cmd as VolumeChangeNotification;
                if (faders != null && faders.Count > _cmd.TrackNo) {
                    faders[_cmd.TrackNo].Scale = DecibelToVolume(_cmd.Volume);
                }
            } else if (cmd is PanChangeNotification) {
                var _cmd = cmd as PanChangeNotification;
                if (faders != null && faders.Count > _cmd!.TrackNo) {
                    faders[_cmd.TrackNo].Pan = (float)_cmd.Pan;
                }
            } else if (cmd is MasterVolumeChangeNotification masterVol) {
                ApplyMasterVolume(masterVol.Volume);
            } else if (cmd is BpmCommand ||
                cmd is TimeSignatureCommand ||
                cmd is AddTempoChangeCommand ||
                cmd is DelTempoChangeCommand ||
                cmd is AddTimeSigCommand ||
                cmd is DelTimeSigCommand) {
                if (PlayingMaster && Metronome) {
                    metronomeEngine.UpdateSchedule(DocManager.Inst.Project.timeAxis, DocManager.Inst.playPosTick);
                }
            } else if (cmd is LoadProjectNotification) {
                StopPlayback();
                RenderCancellationLease.Retire(ref renderCancellation);
                // 工程切换：合成层短语缓存整体失效（hash 含 Timestamp 已兜底，这里显式清空防膨胀）
                RenderEngine.InvalidatePhraseCache();
                // 工程切换卸载全部槽位实例（延迟销毁：无 UI 线程原生 Dispose——
                // ClearAll 同步 vst_unload 在原生 GUI 打开时会 native 崩溃退出进程；
                // 原生 Dispose 收敛到安全点 Flush）
                Vst.VstPluginManager.Inst.UnloadAllTracks();
                DocManager.Inst.ExecuteCmd(new SetPlayPosTickNotification(0));
            }
            if (cmd is PreRenderNotification || cmd is LoadProjectNotification) {
                if (Util.Preferences.Default.PreRender) {
                    SchedulePreRender();
                }
            }
        }

        #endregion
    }
}

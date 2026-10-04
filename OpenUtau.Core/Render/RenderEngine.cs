using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Audio;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtau.Classic;
using Serilog;

namespace OpenUtau.Core.Render {
    public class Progress {
        readonly int total;
        int completed = 0;
        int notifiedCount = 0;
        long lastNotifyMs = 0;
        readonly object lockObj = new();

        public Progress(int total) {
            this.total = total;
        }

        public void Complete(int n, string info) {
            int done = Interlocked.Add(ref completed, n);
            // 节流：每 50 音素或 ≥200ms 才通知一次，完成时强制——防每音素一条
            // Task 投 UI 调度器的洪泛（并行渲染后触发频率更高）
            bool notify = false;
            lock (lockObj) {
                long now = Environment.TickCount64;
                if (done - notifiedCount >= 50 || now - lastNotifyMs >= 200 || done >= total) {
                    notifiedCount = done;
                    lastNotifyMs = now;
                    notify = true;
                }
            }
            if (notify) Notify(done * 100.0 / total, info);
        }

        public void Clear() {
            Notify(0, string.Empty);
        }

        private void Notify(double progress, string info) {
            var notif = new ProgressBarNotification(progress, info);
            var task = new Task(() => DocManager.Inst.ExecuteCmd(notif));
            task.Start(DocManager.Inst.MainScheduler);
        }
    }

    class RenderPartRequest {
        public UVoicePart part;
        public long timestamp;
        public int trackNo;
        public RenderPhrase[] phrases;
        public WaveSource[] sources;
        public WaveMix mix;
    }

    /// <summary>
    /// 轨道效果（MixFx + VST）的接线模式——取代旧的 <c>applyMixFx:bool</c> 单开关。
    /// 语义与旧布尔值的对应：<see cref="Off"/> = false，<see cref="Snapshot"/> = true。
    /// </summary>
    public enum MixFxMode {
        /// <summary>干轨：整条效果链都不接（DJ 干声、分轨导出）。</summary>
        Off = 0,
        /// <summary>
        /// 固定快照：建链时把 <c>UTrack.MixFx</c> Clone 一次，导出结果确定、不随
        /// 导出过程中的参数改动漂移；轨道空转（无 FX / 主开关关 / 模块全透传）时
        /// 原样透传内层源。
        /// </summary>
        Snapshot = 1,
        /// <summary>
        /// 实时跟随：逐音频块读取 <c>UTrack.MixFx</c>，播放中调参一个块内可听，
        /// 不触发重渲染、不重启播放（播放 / 录制混音走这条）。
        /// </summary>
        Live = 2,
    }

    public class RenderEngine {
        readonly UProject project;
        readonly int startTick;
        readonly int endTick;
        readonly int trackNo;
        readonly PhraseRenderCache? cache;

        public RenderEngine(UProject project, int startTick = 0, int endTick = -1, int trackNo = -1,
                            PhraseRenderCache? cache = null) {
            this.project = project;
            this.startTick = startTick;
            this.endTick = endTick;
            this.trackNo = trackNo;
            this.cache = cache;
        }

        // for export（离线：固定快照）
        public Tuple<WaveMix, List<Fader>> RenderMixdown(TaskScheduler uiScheduler, ref CancellationTokenSource cancellation, bool wait = false) {
            return RenderMixdown(uiScheduler, ref cancellation, wait, MixFxMode.Snapshot);
        }

        // 旧布尔入口（调用点兼容）：true = 导出快照，false = 干轨（分轨/干声导出）。
        public Tuple<WaveMix, List<Fader>> RenderMixdown(TaskScheduler uiScheduler, ref CancellationTokenSource cancellation, bool wait, bool applyMixFx) {
            return RenderMixdown(uiScheduler, ref cancellation, wait, applyMixFx ? MixFxMode.Snapshot : MixFxMode.Off);
        }

        /// <summary>离线混音渲染核心（导出/录制共用）。</summary>
        public Tuple<WaveMix, List<Fader>> RenderMixdown(TaskScheduler uiScheduler, ref CancellationTokenSource cancellation, bool wait, MixFxMode mode) {
            var newCancellation = new CancellationTokenSource();
            var oldCancellation = Interlocked.Exchange(ref cancellation, newCancellation);
            if (oldCancellation != null) {
                oldCancellation.Cancel();
                oldCancellation.Dispose();
            }
            double startMs = project.timeAxis.TickPosToMsPos(startTick);
            double endMs = endTick == -1 ? double.PositiveInfinity : project.timeAxis.TickPosToMsPos(endTick);
            var faders = new List<Fader>();
            // 注意：不再在此处 Flush 延迟销毁的 VST handle——裸 Flush 无法保证旧
            // AudioOutput 回调线程已退出（B1 竞态）。Flush 收敛到安全点：
            // StopPlayback / StartPlayback（Stop+drain 后）/ 渲染与导出段尾部。
            var requests = FilterRequests(PrepareRequests(), startMs, endMs);
            var trackOutputs = BuildTrackOutputs(requests, mode, faders);
            var task = Task.Run(async () => {
                await RenderRequestsAsync(requests, newCancellation, playing: !wait);
            });
            task.ContinueWith(task => {
                if (task.IsFaulted && !wait) {
                    Log.Error(task.Exception.Flatten(), "Failed to render.");
                    PlaybackManager.Inst.StopPlayback();
                    var flatEx = task.Exception.Flatten();
                    var innerEx = flatEx.InnerExceptions.ToList();
                    if (innerEx.Count == 1 && innerEx[0] is MessageCustomizableException mce) {
                        DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(mce));
                    } else if (innerEx.Any(e => e is DllNotFoundException)) {
                        DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(
                            new MessageCustomizableException("Failed to render.", "<translate:errors.failed.render>: <translate:errors.install.cpp>", flatEx)));
                    } else {
                        DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(
                            new MessageCustomizableException("Failed to render.", "<translate:errors.failed.render>", flatEx)));
                    }
                }
            }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, uiScheduler);
            if (wait) {
                task.Wait();
            }
            // Build the final mix.  All tracks (FX-wrapped or dry) sum into a single WaveMix.
            var resultMix = new WaveMix(trackOutputs);
            return Tuple.Create(resultMix, faders);
        }

        // for playback
        /// <summary>
        /// 播放渲染：同步建链（秒级内完成）→ 等批 1（播放头前方短语）渲染完成即返回
        /// masterAdapter；批 2（播放头后方）后台继续。返回 null 表示被新渲染周期取消，
        /// 调用方不得 StartPlayback。
        /// </summary>
        public Tuple<MasterAdapter, List<Fader>>? RenderProject(TaskScheduler uiScheduler, ref CancellationTokenSource cancellation) {
            double startMs = project.timeAxis.TickPosToMsPos(startTick);
            double endMs = endTick == -1 ? double.PositiveInfinity : project.timeAxis.TickPosToMsPos(endTick);
            var newCancellation = new CancellationTokenSource();
            var oldCancellation = Interlocked.Exchange(ref cancellation, newCancellation);
            if (oldCancellation != null) {
                oldCancellation.Cancel();
                oldCancellation.Dispose();
            }

            var faders = new List<Fader>();
            var requests = FilterRequests(PrepareRequests(), startMs, endMs);
            // 播放：内置三件套走实时包装（逐块跟随 UTrack.MixFx），VST 链在其上。
            var trackOutputs = BuildTrackOutputs(requests, MixFxMode.Live, faders);

            // master 峰值由 MasterAdapter.Read 统计（主推子 Scale 应用之后 = 实际输出）
            var master = new MasterAdapter(new WaveMix(trackOutputs));
            master.SetPosition((int)(startMs * SignalChain.AudioSettings.SampleRate / 1000) * SignalChain.AudioSettings.Channels);

            // 两批渲染：等批 1 完成即返回（播放头前方数据必在）；批 2 后台继续
            RenderPlaybackAsync(requests, newCancellation).GetAwaiter().GetResult();
            if (newCancellation.IsCancellationRequested) {
                return null;
            }
            return Tuple.Create(master, faders);
        }

        /// <summary>按渲染窗口过滤短语（startMs/endMs）。</summary>
        private RenderPartRequest[] FilterRequests(RenderPartRequest[] requests, double startMs, double endMs) {
            return requests
                .Where(request => request.sources.Length > 0 && request.sources.Max(s => s.EndMs) > startMs && (double.IsPositiveInfinity(endMs) || request.sources.Min(s => s.offsetMs) < endMs))
                .ToArray();
        }

        /// <summary>
        /// 逐轨构建信号链（WaveMix → Fader → MixFxSource(内置三件套) → EffectChain(VST) → LevelTracker）。
        /// 同步操作，不含任何渲染。播放与导出共用，效果接线由 <paramref name="mode"/> 决定。
        /// </summary>
        private List<ISignalSource> BuildTrackOutputs(RenderPartRequest[] requests, MixFxMode mode, List<Fader> faders) {
            // 每轨各自持有 UMixFx（没有全局 FX 总线）。Off 时整条效果链不接；
            // Snapshot/Live 下轨道空转（无 FX / 主开关关 / 模块全透传）原样透传。
            var trackOutputs = new List<ISignalSource>();
            for (int i = 0; i < project.tracks.Count; ++i) {
                if (trackNo != -1 && trackNo != i) {
                    continue;
                }
                var track = project.tracks[i];
                var trackRequests = requests
                    .Where(req => req.trackNo == i)
                    .ToArray();
                var trackSources = trackRequests.Select(req => req.mix)
                    .OfType<ISignalSource>()
                    .ToList();
                trackSources.AddRange(project.parts
                    .Where(part => part is UWavePart && part.trackNo == i)
                    .Select(part => part as UWavePart)
                    .Where(part => part.Samples != null)
                    .Select(part => part.TrimSamples(project)));
                var trackMix = new WaveMix(trackSources);

                var fader = new Fader(trackMix);
                fader.Scale = PlaybackManager.DecibelToVolume(track.Muted ? -24 : track.Volume);
                fader.Pan = (float)track.Pan;
                fader.SetScaleToTarget();
                faders.Add(fader);

                // Collect VST effects — lock-free read from pre-loaded instances
                var vstEffects = new System.Collections.Generic.List<SignalChain.Effects.IEffect>();
                if (mode != MixFxMode.Off && track.VstSlots != null) {
                    var active = Vst.VstPluginManager.Inst.GetActiveEffects(track.TrackNo);
                    if (active.Count == 0 && track.VstSlots.Any(s => s.IsLoaded && !s.Bypassed)) {
                        // 兜底：槽位有插件但实例未加载（异步加载未触发/未完成/工程
                        // 恢复路径未走到）——渲染前同步补加载，保证 VST 生效
                        foreach (var s in track.VstSlots) {
                            if (s.IsLoaded && !s.Bypassed) {
                                Vst.VstPluginManager.Inst.LoadEffect(track.TrackNo, s);
                            }
                        }
                        active = Vst.VstPluginManager.Inst.GetActiveEffects(track.TrackNo);
                    }
                    foreach (var fx in active)
                        vstEffects.Add(fx);
                }

                ISignalSource trackOut = WrapTrackFx(fader, track, mode, vstEffects.ToArray());

                // LevelTracker wraps the FINAL per-track output (fader + FX),
                // so the mixer meter shows the actual audible signal.
                var tracker = new LevelTracker(trackOut);
                TrackLevels.Register(track.TrackNo, tracker);
                trackOutputs.Add(tracker);
            }
            return trackOutputs;
        }

        /// <summary>
        /// 单轨效果接线（可单测的接缝）：内置三件套（EQ/压缩/混响）由
        /// <see cref="MixFxSource"/> 承载，VST 链在其上，避免同一信号被处理两遍。
        /// <see cref="MixFxMode.Off"/> 时原样返回（连 VST 也不接，= 旧 applyMixFx:false）。
        /// </summary>
        internal static ISignalSource WrapTrackFx(ISignalSource inner, UTrack track, MixFxMode mode,
                                                  SignalChain.Effects.IEffect[] vstEffects) {
            if (mode == MixFxMode.Off) {
                return inner;
            }
            ISignalSource fx = mode == MixFxMode.Live
                ? MixFxSource.WrapLive(inner, track)
                : MixFxSource.WrapWith(inner, track.MixFx);
            return EffectChain.Build(fx, vstEffects);
        }

        // for export
        public List<WaveMix> RenderTracks(TaskScheduler uiScheduler, ref CancellationTokenSource cancellation) {
            var newCancellation = new CancellationTokenSource();
            var oldCancellation = Interlocked.Exchange(ref cancellation, newCancellation);
            if (oldCancellation != null) {
                oldCancellation.Cancel();
                oldCancellation.Dispose();
            }
            var trackMixes = new List<WaveMix>();
            var requests = PrepareRequests();
            // 遍历全部轨道——纯音频轨（UWavePart）/空轨也在列表中（null 或混入 wave part），
            // 此前只覆盖"有 voice part 的轨道"→ 分轨导出静默丢轨
            for (int i = 0; i < project.tracks.Count; ++i) {
                var trackRequests = requests.Where(req => req.trackNo == i).ToArray();
                if (trackRequests.Length == 0) {
                    var waveSources = project.parts
                        .Where(part => part is UWavePart && part.trackNo == i)
                        .Select(part => part as UWavePart)
                        .Where(part => part.Samples != null)
                        .Select(part => part.TrimSamples(project))
                        .OfType<ISignalSource>()
                        .ToList();
                    trackMixes.Add(waveSources.Count > 0 ? new WaveMix(waveSources) : null);
                } else {
                    // RenderTracks 由后台导出任务线程调用（无 UI 上下文依赖），同步等待安全
                    RenderRequestsAsync(trackRequests, newCancellation).GetAwaiter().GetResult();
                    trackMixes.Add(new WaveMix(trackRequests.Select(req => req.mix).ToArray()));
                }
            }
            return trackMixes;
        }

        // for pre render
        public void PreRenderProject(ref CancellationTokenSource cancellation) {
            // 播放/录制中不预渲染：此前 PreRender 无条件 Exchange+Cancel 共享的
            // renderCancellation——播放中编辑/撤销会杀掉播放批 2 的渲染
            //（播放头后方短语永不渲染 → 后半段静音直到重新播放）
            if (PlaybackManager.Inst.OutputActive || PlaybackManager.Inst.PlayingMaster || PlaybackManager.Inst.IsRecording) {
                return;
            }
            var newCancellation = new CancellationTokenSource();
            var oldCancellation = Interlocked.Exchange(ref cancellation, newCancellation);
            if (oldCancellation != null) {
                oldCancellation.Cancel();
                oldCancellation.Dispose();
            }
            Task.Run(async () => {
                try {
                    Thread.Sleep(200);
                    if (newCancellation.Token.IsCancellationRequested) {
                        return;
                    }
                    await RenderRequestsAsync(PrepareRequests(), newCancellation);
                } catch (Exception e) {
                    if (!newCancellation.IsCancellationRequested) {
                        Log.Error(e, "Failed to pre-render.");
                    }
                }
            });
        }

        private RenderPartRequest[] PrepareRequests() {
            RenderPartRequest[] requests;
            SingerManager.Inst.ReleaseSingersNotInUse(project);
            lock (project) {
                requests = project.parts
                    .Where(part => part is UVoicePart && (trackNo == -1 || part.trackNo == trackNo))
                    .Where(part => !Preferences.Default.SkipRenderingMutedTracks || !project.tracks[part.trackNo].Muted)
                    .Select(part => part as UVoicePart)
                    .Select(part => part.GetRenderRequest())
                    .Where(request => request != null)
                    .ToArray();
            }
            foreach (var request in requests) {
                if (endTick != -1) {
                    request.phrases = request.phrases
                        .Where(phrase => phrase.end > startTick && (endTick == -1 || phrase.position < endTick))
                        .ToArray();
                }
                request.sources = new WaveSource[request.phrases.Length];
                for (var i = 0; i < request.phrases.Length; i++) {
                    var phrase = request.phrases[i];
                    var firstPhone = phrase.phones.First();
                    var lastPhone = phrase.phones.Last();
                    var layout = phrase.renderer.Layout(phrase);
                    double posMs = layout.positionMs - layout.leadingMs;
                    double durMs = layout.estimatedLengthMs;
                    request.sources[i] = new WaveSource(posMs, durMs, 0, 1);
                }
                request.mix = new WaveMix(request.sources);
            }
            return requests;
        }

        private async Task RenderRequestsAsync(
            RenderPartRequest[] requests,
            CancellationTokenSource cancellation,
            bool playing = false) {
            if (requests.Length == 0 || cancellation.IsCancellationRequested) {
                return;
            }
            var tuples = requests
                .SelectMany(req => req.phrases
                    .Zip(req.sources, (phrase, source) => Tuple.Create(phrase, source, req)))
                .ToArray();
            if (playing) {
                // 播放头前方（end > startTick）的短语先渲染，保证先出声
                tuples = tuples
                    .Where(tuple => tuple.Item1.end > startTick)
                    .OrderBy(tuple => tuple.Item1.end)
                    .Concat(tuples.Where(tuple => tuple.Item1.end <= startTick))
                    .ToArray();
            }
            var progress = new Progress(tuples.Sum(t => t.Item1.phones.Length));
            // 短语级并行：DOP = NumRenderThreads（默认 2，渲染器线程安全未验证前保守）
            int dop = Math.Clamp(Preferences.Default.NumRenderThreads, 1, 8);
            using var sem = new SemaphoreSlim(dop);
            // 渲染合成段进入在飞计数——防止并发 Flush 释放正在被消费的 VST handle
            using (RenderGate.Enter()) {
                var tasks = tuples.Select(tuple => RenderOneAsync(tuple, cancellation, progress, sem)).ToArray();
                await Task.WhenAll(tasks);
            }
            if (!cancellation.IsCancellationRequested) {
                progress.Clear();
            }
            // 机会性 Flush：渲染段已退出计数、输出未播放时释放延迟销毁的旧 VST handle
            //（此前在 gate 作用域内调用恒被拒绝——死代码）
            if (!PlaybackManager.Inst.OutputActive) {
                Vst.VstPluginManager.Inst.TryFlushAllPendingDispose();
            }
        }

        /// <summary>
        /// 渲染单个短语：内存缓存命中则跳过合成；SetSamples/SetMix 有锁（并发安全）；
        /// 写入前检查取消，取消周期不写新周期共享的 part.mix。
        /// </summary>
        private async Task RenderOneAsync(
            Tuple<RenderPhrase, WaveSource, RenderPartRequest> tuple,
            CancellationTokenSource cancellation,
            Progress progress,
            SemaphoreSlim sem) {
            await sem.WaitAsync().ConfigureAwait(false);
            try {
                if (cancellation.IsCancellationRequested) return;
                var phrase = tuple.Item1;
                var source = tuple.Item2;
                var request = tuple.Item3;

                float[] samples = cache?.TryGet(phrase.hash) ?? null;
                if (samples == null) {
                    var task = phrase.renderer.Render(phrase, progress, request.trackNo, cancellation, true);
                    await task.ConfigureAwait(false);
                    if (cancellation.IsCancellationRequested) return;
                    samples = task.Result.samples;
                    cache?.Put(phrase.hash, samples);
                } else {
                    // 缓存命中：renderer 未运行，手动推进进度（total 含全部短语）
                    if (cancellation.IsCancellationRequested) return;
                    progress.Complete(phrase.phones.Length, "Cached");
                }
                source.SetSamples(samples);
                if (request.sources.All(s => s.HasSamples)) {
                    request.part.SetMix(request.mix);
                    DocManager.Inst.ExecuteCmd(new PartRenderedNotification(request.part));
                }
            } finally {
                sem.Release();
            }
        }

        /// <summary>
        /// 播放两批渲染：批 1（end &gt; startTick 播放头前方，按 end 升序）并行完成即返回，
        /// 调用方立即 StartPlayback；批 2（后方）fire-and-forget 后台继续
        /// （运行中的 Task 由线程池持有，不会 GC；取消由 token 传播）。
        /// 批 2 未就绪短语由既有 Waited 静音保护兜底（WaveMix 取 Max → MasterAdapter 累计）。
        /// </summary>
        private async Task RenderPlaybackAsync(RenderPartRequest[] requests, CancellationTokenSource cancellation) {
            if (requests.Length == 0 || cancellation.IsCancellationRequested) {
                return;
            }
            var allTuples = requests
                .SelectMany(req => req.phrases
                    .Zip(req.sources, (phrase, source) => Tuple.Create(phrase, source, req)))
                .ToArray();
            var tuples = allTuples
                .Where(tuple => tuple.Item1.end > startTick)
                .OrderBy(tuple => tuple.Item1.end)
                .Concat(allTuples.Where(tuple => tuple.Item1.end <= startTick))
                .ToArray();
            // 批 1 = 播放头前方 10 秒窗口内的短语——曲首播放不再等整曲渲染完才出声
            //（此前批 1 = 播放头前方全部短语，冷缓存曲首播放要等全曲）
            double startMs = project.timeAxis.TickPosToMsPos(startTick);
            int windowEndTick = project.timeAxis.MsPosToTickPos(startMs + 10000);
            int batch1Count = tuples.Count(t => t.Item1.end > startTick && t.Item1.end <= windowEndTick);
            var progress = new Progress(tuples.Sum(t => t.Item1.phones.Length));

            await RenderBatchAsync(tuples.Take(batch1Count).ToArray(), cancellation, progress);
            var batch2 = tuples.Skip(batch1Count).ToArray();
            if (batch2.Length == 0) {
                if (!cancellation.IsCancellationRequested) progress.Clear();
                if (!PlaybackManager.Inst.OutputActive) {
                    Vst.VstPluginManager.Inst.TryFlushAllPendingDispose();
                }
                return;
            }
            // 批 2 后台继续；完成时收尾（进度清零 + 机会性 Flush）。
            // 异常必须观察：批 2 渲染失败此前被静默吞掉（播放头后方无提示静音）
            _ = RenderBatchAsync(batch2, cancellation, progress).ContinueWith(t => {
                if (t.IsFaulted && !cancellation.IsCancellationRequested) {
                    Log.Error(t.Exception.Flatten(), "Failed to render background batch.");
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(
                        new MessageCustomizableException("Failed to render.", "<translate:errors.failed.render>", t.Exception.Flatten())));
                }
                if (!cancellation.IsCancellationRequested) progress.Clear();
                if (!PlaybackManager.Inst.OutputActive) {
                    Vst.VstPluginManager.Inst.TryFlushAllPendingDispose();
                }
            }, TaskScheduler.Default);
        }

        /// <summary>并行渲染一批短语（RenderGate 计数内）。</summary>
        private async Task RenderBatchAsync(
            Tuple<RenderPhrase, WaveSource, RenderPartRequest>[] tuples,
            CancellationTokenSource cancellation,
            Progress progress) {
            if (tuples.Length == 0) return;
            using var gate = RenderGate.Enter();
            int dop = Math.Clamp(Preferences.Default.NumRenderThreads, 1, 8);
            using var sem = new SemaphoreSlim(dop);
            var tasks = tuples.Select(tuple => RenderOneAsync(tuple, cancellation, progress, sem)).ToArray();
            await Task.WhenAll(tasks);
        }

        public static void ReleaseSourceTemp() {
            VoicebankFiles.Inst.ReleaseSourceTemp();
        }

        // ─────────────────────────────────────────────────────────────────────
        // 合成层对外门面（接缝）
        //
        // 调用方（播放/导出层）只依赖这四个静态成员，不构造 RenderEngine、不持有
        // PhraseRenderCache、不知道两批播放策略与乐句缓存的存在。将来把本类整体
        // 换成上游的渲染实现（frozen slot planner + 优先级调度）时，只需保持这四
        // 个签名不变，运输/混音/导出层零改动。
        //
        // 对应约定：PlaybackManager 与 ExportSession 不得直接 new RenderEngine、
        // 不得引用 PhraseCache/PhraseRenderCache 类型（有契约测试锁定）。
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>乐句渲染缓存（进程内单例；由合成层自己持有，外部只可整体失效）。</summary>
        private static readonly PhraseRenderCache phraseCache = new PhraseRenderCache();

        /// <summary>预热全曲乐句（后台预渲染入口）。</summary>
        public static void PreRender(UProject project) {
            var engine = new RenderEngine(project);
            engine.PreRenderProject(ref preRenderCancellation);
        }

        private static CancellationTokenSource preRenderCancellation = new CancellationTokenSource();

        /// <summary>播放渲染：同步建链 → 等批 1 完成即返回 master。null = 被新周期取消。</summary>
        public static Tuple<MasterAdapter, List<Fader>>? RenderProject(
            UProject project, TaskScheduler uiScheduler, ref CancellationTokenSource cancellation,
            int startTick = 0, int endTick = -1, int trackNo = -1) {
            var engine = new RenderEngine(project, startTick, endTick, trackNo, phraseCache);
            return engine.RenderProject(uiScheduler, ref cancellation);
        }

        /// <summary>
        /// 离线混音渲染（导出用；固定快照，导出确定）。导出干轨传 applyMixFx:false。
        /// 播放走 <see cref="RenderProject"/>（MixFxMode.Live）。
        /// </summary>
        public static Tuple<WaveMix, List<Fader>> RenderMixdown(
            UProject project, TaskScheduler uiScheduler, ref CancellationTokenSource cancellation,
            bool wait = false, bool applyMixFx = true,
            int startTick = 0, int endTick = -1, int trackNo = -1) {
            var engine = new RenderEngine(project, startTick, endTick, trackNo, phraseCache);
            return engine.RenderMixdown(uiScheduler, ref cancellation, wait, applyMixFx);
        }

        /// <summary>逐轨混音渲染（分轨导出用）。</summary>
        public static List<WaveMix> RenderTracks(
            UProject project, TaskScheduler uiScheduler, ref CancellationTokenSource cancellation,
            int startTick = 0, int endTick = -1) {
            var engine = new RenderEngine(project, startTick, endTick, -1, phraseCache);
            return engine.RenderTracks(uiScheduler, ref cancellation);
        }

        /// <summary>整体失效短语缓存（编辑/换工程/清缓存用）。</summary>
        public static void InvalidatePhraseCache() {
            phraseCache.Clear();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Api;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Test.TestSupport {
    /// <summary>
    /// W7-1/W7-2 经典渲染端到端夹具：伪声库 → 声库轨工程 → **产品音素化路径**
    /// （<c>UPart.Validate</c> → <c>PhonemizerRunner</c> → <c>part.SetPhonemizerResponse</c>
    /// → <c>RenderPhrase.FromPart</c>）→ <c>RenderEngine.RenderMixdown</c> →
    /// ClassicRenderer（worldline resampler + SharpWavtool）→ 混音链。
    ///
    /// 不复制产品的音素化逻辑：这里只是把 app 里由 <c>DocManager</c> 装配的那几个部件
    /// 手工接上（PhonemizerRunner / DocManager.Project / 调度器），其余全走产品代码。
    /// </summary>
    internal static class ClassicRenderSetup {
        /// <summary>
        /// 建立声库轨工程。notes = (lyric, tone, durationTicks)。
        /// 渲染器固定 <see cref="Renderers.CLASSIC"/>（逐音素 worldline resampler + SharpWavtool），
        /// 这是"经典渲染"里可离线复现的一条；WORLDLINE-R 走整句原生合成，另见 capability 用例。
        /// </summary>
        public static (UProject project, UVoicePart part) BuildProject(
            ClassicSinger singer, params (string lyric, int tone, int duration)[] notes) {
            // 经典渲染把 res-*.wav 写进 PathManager.CachePath；app 里由 PlaybackManager 构造时
            // Directory.CreateDirectory 建好，测试宿主必须自己先建（否则 resampler 写文件
            // DirectoryNotFoundException）。
            System.IO.Directory.CreateDirectory(OpenUtau.Core.PathManager.Inst.CachePath);
            var project = new UProject();
            OpenUtau.Core.Format.Ustx.AddDefaultExpressions(project);
            var track = project.tracks[0];
            track.TrackNo = 0;
            track.Singer = singer;
            track.Phonemizer = PhonemizerFactory.Get(typeof(DefaultPhonemizer)).Create();
            track.RendererSettings.renderer = Renderers.CLASSIC;
            track.RendererSettings.Validate(track);
            if (track.RendererSettings.Resampler == null || track.RendererSettings.Wavtool == null) {
                throw new InvalidOperationException("经典渲染器未装配成功（ToolsManager 未初始化？）");
            }
            var part = new UVoicePart { trackNo = 0, position = 0 };
            int pos = 0;
            foreach (var (lyric, tone, duration) in notes) {
                // 必须走 UProject.CreateNote()：它同时初始化 pitch/vibrato 与音高曲线点，
                // 手写 new UNote 会在 UNote.Validate 的 pitch.snapFirst/pitch.data[0] 上炸
                var note = project.CreateNote(tone, pos, duration);
                note.lyric = lyric;
                part.notes.Add(note);
                pos += duration;
            }
            part.Duration = pos + 480; // 尾巴留一拍，便于看衰减
            project.parts.Add(part);
            return (project, part);
        }

        /// <summary>
        /// 装配音素化运行器。返回的会话 Dispose 时必须**还原 DocManager 上的 runner**：
        /// 若把已 Dispose 的 runner 留在全局，后续任何 <c>UProject.ValidateFull</c> 走进
        /// <c>UPart.Validate → PhonemizerRunner.Push</c> 都会 ObjectDisposedException
        /// （实测在全量跑里炸掉 4 个无关用例）。
        /// </summary>
        public static PhonemizerSession StartPhonemizer(UProject project) {
            AudioFixtures.EnsureSchedulers(); // mainThread = 当前线程；mainScheduler = 线程池
            // 响应任务跑在线程池上，ExecuteCmd 会认为"不在主线程"而回调 PostOnUIThread。
            // 这里**丢弃**这类通知：本夹具只关心 SetPhonemizerResponse + Project.Validate
            // 的副作用（它们在 ExecuteCmd 之前已完成）。注意别写成 action => action()——
            // 那会在非主线程上无限自递归直到栈溢出。
            DocManager.Inst.PostOnUIThread = _ => { };
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(project));
            var property = typeof(DocManager)
                .GetProperty("PhonemizerRunner", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var previous = (PhonemizerRunner?)property.GetValue(DocManager.Inst);
            var runner = new PhonemizerRunner(TaskScheduler.Default);
            property.SetValue(DocManager.Inst, runner);
            return new PhonemizerSession(property, runner, previous);
        }

        public sealed class PhonemizerSession : IDisposable {
            readonly PropertyInfo property;
            readonly PhonemizerRunner runner;
            readonly PhonemizerRunner? previous;

            public PhonemizerSession(PropertyInfo property, PhonemizerRunner runner, PhonemizerRunner? previous) {
                this.property = property;
                this.runner = runner;
                this.previous = previous;
            }

            public void Dispose() {
                property.SetValue(DocManager.Inst, previous);
                runner.Dispose();
            }
        }

        /// <summary>跑一次音素化并等到渲染短语就绪（失败抛超时并打印诊断）。</summary>
        public static void PhonemizeAndWait(UProject project, UVoicePart part, Action<string>? log = null, int timeoutMs = 20000) {
            project.ValidateFull();
            var sw = Stopwatch.StartNew();
            bool ready = false;
            // 响应由 PhonemizerRunner 在线程池上消费，全程持有 lock(part)（UPart.Validate 的
            // lock(this)）。而 PhonemesUpToDate 在 renderPhrases 填好**之前**就变 true，
            // 所以必须拿同一把锁判"就绪"，否则会读到中间态（实测踩到）。
            while (!ready && sw.ElapsedMilliseconds < timeoutMs) {
                lock (part) {
                    ready = part.PhonemesUpToDate && part.renderPhrases.Count > 0;
                }
                if (!ready) {
                    Thread.Sleep(10);
                }
            }
            var track = project.tracks[part.trackNo];
            if (log != null) {
                log($"wait {sw.ElapsedMilliseconds} ms: PhonemesUpToDate={part.PhonemesUpToDate}, " +
                    $"phonemes={part.phonemes.Count}, phrases={part.renderPhrases.Count}, " +
                    $"setUpException={track.Phonemizer?.SetUpException?.Message ?? "none"}");
                foreach (var note in part.notes) {
                    log($"  note \"{note.lyric}\" tone={note.tone} pos={note.position} dur={note.duration} " +
                        $"Error={note.Error} Overlap={note.OverlapError} phonemeIndexes=[{string.Join(",", note.phonemeIndexes)}]");
                }
                foreach (var ph in part.phonemes) {
                    log($"  phoneme \"{ph.phoneme}\" pos={ph.position} end={ph.End} " +
                        $"oto={(ph.oto == null ? "null" : ph.oto.Alias)} preutter={ph.preutter:F1} Error={ph.Error}");
                }
            }
            if (!ready) {
                throw new TimeoutException(
                    $"音素化/建短语超时（{timeoutMs} ms）：PhonemesUpToDate={part.PhonemesUpToDate}, " +
                    $"phonemes={part.phonemes.Count}, phrases={part.renderPhrases.Count}");
            }
        }
    }
}

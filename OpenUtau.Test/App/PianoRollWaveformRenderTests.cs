using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using OpenUtau.App;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Ustx;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// 卷帘波形渲染的**可测性能证据**（上游 `ffcf2748` + `f05244c0` 的重写口径）。
    ///
    /// 断言的是"重建/重混次数"，不是像素：本仓 headless 走 UseHeadlessDrawing 桩绘制，
    /// 位面是空白。滚动引起的"每帧重新分桶 + 重新混音"是旧实现的可测开销，这里把它量化：
    ///   · 旧实现：**每次 Render** 都 `part.Mix.Mix(...)` 整段重混 + 逐列 `Min()/Max()`；
    ///   · 新实现：列固定在同一时间网格上、缓存范围比视口宽（左右各半屏余量）⇒
    ///     视口在小范围滚动时**只平移几何**，不重混、不重建。
    /// </summary>
    public class PianoRollWaveformRenderTests {
        const double Width = 400;
        const double Height = 80;

        /// <summary>计数用假信号源：记录 Mix 调用次数，写入 ±0.8 的交错采样。</summary>
        sealed class CountingMix : ISignalSource {
            public int MixCalls { get; private set; }
            public int SampleRate => 44100;
            public int Channels => 2;
            public bool IsReady(int position, int count) => true;
            public int Mix(int position, float[] buffer, int index, int count) {
                MixCalls++;
                for (int i = 0; i < count; i++) {
                    buffer[index + i] = (position + i) % 2 == 0 ? 0.8f : -0.8f;
                }
                return position + count;
            }
        }

        static (NotesViewModel vm, CountingMix mix) Setup(double tickWidth, double tickOffset) {
            DocManagerTestSetup.RunOnCurrentThread();
            var project = new UProject();
            project.timeAxis.BuildSegments(project);
            var part = new UVoicePart { trackNo = 0, position = 0, duration = 480 * 64 };
            project.parts.Add(part);          // 本仓片段挂在工程上，part.trackNo 指回轨道
            var mix = new CountingMix();
            part.SetMix(mix);
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(project));
            var vm = new NotesViewModel {
                Part = part,
                TickOrigin = 0,
                TickWidth = tickWidth,
                TickOffset = tickOffset,
                Bounds = new Rect(0, 0, Width, Height),
            };
            return (vm, mix);
        }

        static WaveformImage NewWaveform(NotesViewModel vm) =>
            new WaveformImage { DataContext = vm, ShowWaveform = true, Width = Width, Height = Height };

        /// <summary>一轮真实布局 + 绘制（RenderTargetBitmap 提供真 DrawingContext，位面是桩）。</summary>
        static void RenderOnce(Control control) {
            control.Measure(new Size(Width, Height));
            control.Arrange(new Rect(0, 0, Width, Height));
            using var target = new RenderTargetBitmap(new PixelSize((int)Width, (int)Height));
            target.Render(control);
        }

        [AvaloniaFact]
        public void ScrollingWithinCache_DoesNotRemixOrRebuildTheEnvelope() {
            var (vm, mix) = Setup(tickWidth: 0.3, tickOffset: 20);
            var waveform = NewWaveform(vm);

            // ① 首帧：缩放变化 ⇒ 只建**可见部分**（上游同口径：缩放期间每帧都会失效）
            RenderOnce(waveform);
            Assert.Equal(1, waveform.BuildCount);
            Assert.Equal(1, mix.MixCalls);

            // ② 滚出"只建了可见部分"的范围 ⇒ 建一次**带余量**的缓存（左右各半屏）
            vm.TickOffset = 500;                 // offsetPx=150 ⇒ 视口右缘越出首帧缓存 [6,407)
            RenderOnce(waveform);
            Assert.Equal(2, waveform.BuildCount);

            int buildsAfterCache = waveform.BuildCount;
            int mixesAfterCache = mix.MixCalls;
            Assert.Equal(2, mixesAfterCache);

            // ③ 在缓存余量内来回滚动 20 帧：只平移几何，不重混、不重建
            for (int i = 1; i <= 10; i++) {
                vm.TickOffset = 300 + i * 3;
                RenderOnce(waveform);
            }
            for (int i = 9; i >= 0; i--) {
                vm.TickOffset = 300 + i * 3;
                RenderOnce(waveform);
            }
            Assert.Equal(buildsAfterCache, waveform.BuildCount);
            Assert.Equal(mixesAfterCache, mix.MixCalls);

            // ④ 滚出缓存范围 ⇒ 重建一次（重建次数随"越界"走，不随帧数走）
            vm.TickOffset = 5000;
            RenderOnce(waveform);
            Assert.Equal(buildsAfterCache + 1, waveform.BuildCount);
            Assert.Equal(mixesAfterCache + 1, mix.MixCalls);

            // ⑤ 缩放会让缓存失效 ⇒ 重建（上游同口径：缩放期间每帧只建可见部分）
            vm.TickWidth = 0.5;
            RenderOnce(waveform);
            Assert.Equal(buildsAfterCache + 2, waveform.BuildCount);

            // 总账：23 帧渲染只重混 **4** 次；旧实现每次 Render 都重混 ⇒ 23 次
            Assert.Equal(4, mix.MixCalls);
        }

        [AvaloniaFact]
        public void HiddenOrZoomedOut_DoesNotMixAtAll() {
            var (vm, mix) = Setup(tickWidth: 0.3, tickOffset: 0);
            var waveform = NewWaveform(vm);
            waveform.ShowWaveform = false;
            RenderOnce(waveform);
            Assert.Equal(0, mix.MixCalls);
            Assert.Equal(0, waveform.BuildCount);

            // 缩到细节阈值以下（ViewConstants.PianoRollTickWidthShowDetails）也不取样
            waveform.ShowWaveform = true;
            vm.TickWidth = ViewConstants.PianoRollTickWidthShowDetails / 2;
            RenderOnce(waveform);
            Assert.Equal(0, mix.MixCalls);
            Assert.Equal(0, waveform.BuildCount);
        }

        [AvaloniaFact]
        public void SamplingGrid_UsesAbsoluteSongTime() {
            // 交错采样下标：毫秒 × 44100 / 1000 × 2 声道（与 mix 的排布一致）
            Assert.Equal(0, WaveformImage.SampleIndex(0));
            Assert.Equal(88200, WaveformImage.SampleIndex(1000));
            Assert.Equal(88, WaveformImage.SampleIndex(1));      // 1ms → 44 帧 → 88 交错采样
            // 列网格是**绝对**歌曲时间（TickOrigin 平移整格，不是重新分桶的相位）
            Assert.True(WaveformImage.SampleIndex(500) > WaveformImage.SampleIndex(400));
        }
    }
}

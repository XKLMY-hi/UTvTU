using System;
using System.Linq;
using OpenUtau.Core.Render;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.Audio {
    /// <summary>
    /// W7-1 前置结论：本机（离线沙箱、无外部依赖）能否跑通经典渲染。
    ///
    /// 经典渲染链 = ClassicRenderer（逐音素 resampler + SharpWavtool 拼接），
    /// 其中 resampler 默认是本仓唯一内建实现 <c>WorldlineResampler</c>，它 P/Invoke
    /// 原生 <c>worldline</c> 库。故"能否跑通"的第一个判据是：**原生库在本机可用，
    /// 且真实调用能返回结果**（不是只检查文件存在）。
    /// </summary>
    [Collection("AudioFixture")]
    public class ClassicRenderCapabilityTests {
        readonly ITestOutputHelper output;

        public ClassicRenderCapabilityTests(ITestOutputHelper output) {
            this.output = output;
        }

        static float[] Sine(double freq, double seconds, int rate = 44100, float amp = 0.5f) {
            var data = new float[(int)(seconds * rate)];
            for (int i = 0; i < data.Length; i++) {
                data[i] = amp * (float)Math.Sin(2 * Math.PI * freq * i / rate);
            }
            return data;
        }

        [Fact]
        public void NativeWorldline_LibraryLoadsAndExecutes() {
            output.WriteLine(NativeWorldline.Diagnostics());
            Assert.True(NativeWorldline.EnsureLoaded(out string reason), reason);

            // 真实本机调用：440 Hz 正弦的 F0 估计（method 0 = 默认）
            var samples = Sine(440, 1.0);
            var f0 = Worldline.F0(samples, 44100, 5.0, 0);
            Assert.True(f0 != null && f0.Length > 0, "worldline F0 返回空 —— 原生调用不可用");

            var voiced = f0!.Where(v => v > 0).ToArray();
            Assert.NotEmpty(voiced);
            var sorted = voiced.OrderBy(v => v).ToArray();
            double median = sorted[sorted.Length / 2];
            output.WriteLine($"F0 frames={f0.Length} voiced={voiced.Length} median={median:F2} Hz");
            Assert.InRange(median, 400, 480);
        }
    }
}

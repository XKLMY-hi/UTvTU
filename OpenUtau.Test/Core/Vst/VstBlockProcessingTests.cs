using System;
using System.Collections.Generic;
using Xunit;

namespace OpenUtau.Core.Vst {
    [Collection("VstShared")]
    public class VstBlockProcessingTests {
        const string Uid = "test:export-block-limit";

        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        public void LargeDeviceReadProcessesEveryFrameAndPreservesSurroundingSamples(int offset) {
            VstTestSetup.Register(VstTestSetup.MakeEntry(Uid));
            var calls = new List<int>();
            var bridge = new FakeVstBridge { ProcessCallback = (buffer, frames) => {
                calls.Add(frames);
                // Reproduce the native bridge's maximum block truncation.
                for (int i = 0; i < Math.Min(frames, 4096) * 2; i++) buffer[i] *= 2;
            }};
            using var effect = new VstEffect(VstTestSetup.CreateSlot(Uid), bridge);
            effect.Load();
            effect.Setup(44100, 4096);
            const int frames = 10003;
            var samples = new float[offset + frames * 2 + 7];
            Array.Fill(samples, .25f);
            effect.Process(samples, offset, frames * 2);
            Assert.Equal(new[] { 4096, 4096, 1811 }, calls);
            for (int i = 0; i < samples.Length; i++)
                Assert.Equal(i >= offset && i < offset + frames * 2 ? .5f : .25f, samples[i]);
        }

        [Fact]
        public void RepeatedLargeReadsDoNotAllocateOnTheAudioThread() {
            VstTestSetup.Register(VstTestSetup.MakeEntry(Uid));
            var bridge = new FakeVstBridge();
            using var effect = new VstEffect(VstTestSetup.CreateSlot(Uid), bridge);
            effect.Load();
            effect.Setup(48000, 4096);
            var samples = new float[9600];
            for (int i = 0; i < 8; i++) effect.Process(samples, 0, samples.Length);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 32; i++) effect.Process(samples, 0, samples.Length);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}

using System;
using Xunit;

namespace OpenUtau.Core.Vst {
    [Collection("VstShared")]
    public class VstEffectTest {
        private const string TestUid = "test:fake-plugin";

        public VstEffectTest() {
            VstTestSetup.Register(VstTestSetup.MakeEntry(TestUid));
        }

        [Fact]
        public void VstThreadProbe_InvokeDispose() {
            var t = new VstThread("probe");
            try {
                Assert.Equal(42, t.Invoke(() => 42));
            } finally {
                t.Dispose();
            }
        }

        [Fact]
        public void Construct_WithFakeBridge_LoadsSuccessfully() {
            var bridge = new FakeVstBridge();
            var slot = VstTestSetup.CreateSlot(TestUid);
            var fx = new VstEffect(slot, bridge);

            try {
                fx.Load();
                Assert.True(fx.IsLoaded);
                Assert.NotEqual(IntPtr.Zero, fx.GetBridgeHandle());
            } finally { fx.Dispose(); } // 释放专用 VST 线程
        }

        [Fact]
        public void Construct_WithoutBridge_UsesRealVstBridge() {
            var slot = VstTestSetup.CreateSlot(TestUid);
            var fx = new VstEffect(slot); // default bridge
            try { Assert.False(fx.IsLoaded); } finally { fx.Dispose(); } // No real VST DLL; handle stays zero
        }

        [Fact]
        public void Process_WithFakeBridge_DoesNotThrow() {
            var bridge = new FakeVstBridge();
            var slot = VstTestSetup.CreateSlot(TestUid);
            var fx = new VstEffect(slot, bridge);
            try {
                fx.Load();

                var buf = new float[64];
                var ex = Record.Exception(() => fx.Process(buf, 0, 64));
                Assert.Null(ex);
            } finally { fx.Dispose(); }
        }

        [Fact]
        public void Reset_WithFakeBridge_DoesNotThrow() {
            var bridge = new FakeVstBridge();
            var slot = VstTestSetup.CreateSlot(TestUid);
            var fx = new VstEffect(slot, bridge);
            try {
                fx.Load();

                var ex = Record.Exception(() => fx.Reset());
                Assert.Null(ex);
            } finally { fx.Dispose(); }
        }

        [Fact]
        public void SetupActivatesOnOwnerBeforeAnyAudioProcessing() {
            var bridge = new FakeVstBridge();
            var slot = VstTestSetup.CreateSlot(TestUid);
            var fx = new VstEffect(slot, bridge);
            try {
                fx.Load();
                fx.Setup(44100, 4096);
                Assert.Equal(1, bridge.ActivateCalls);
                fx.Process(new float[512], 0, 512);
                Assert.Equal(1, bridge.ActivateCalls);
            } finally { fx.Dispose(); }
        }
    }
}

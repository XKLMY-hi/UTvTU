using System;
using Xunit;

namespace OpenUtau.Core.Vst {
    public class NativeExportContractTests {
        [Fact]
        public void ShippedWindowsBridgeContainsExportAbiAndRejectsInvalidHandles() {
            Assert.False(VstBridge.SetupExport(IntPtr.Zero, 44100, 4096, true));
            Assert.False(VstBridge.ProcessExport(IntPtr.Zero, new float[2], 1, new VstExportContext(0, 0, 120, 4, 4)));
            Assert.Equal(-1, VstBridge.GetLatencySamples(IntPtr.Zero));
        }
    }
}

using System.Collections.Generic;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Test.Core.Util {
    public class WindowsGpuDetectorTest {
        [Fact]
        public void DetectsIndirectDisplayAdapterBit() {
            Assert.False(WindowsGpuDetector.IsIndirectDisplayAdapter(0u));
            Assert.False(WindowsGpuDetector.IsIndirectDisplayAdapter(1u << 0));
            Assert.True(WindowsGpuDetector.IsIndirectDisplayAdapter(1u << 6));
            Assert.True(WindowsGpuDetector.IsIndirectDisplayAdapter((1u << 6) | 1u));
        }

        [Fact]
        public void MissingOrInvalidLuidReturnsFalse() {
            Assert.False(WindowsGpuDetector.IsIndirectDisplayAdapter(new Dictionary<string, string>()));
            Assert.False(WindowsGpuDetector.IsIndirectDisplayAdapter(new Dictionary<string, string> {
                ["LUID"] = "not-a-number",
            }));
        }
    }
}

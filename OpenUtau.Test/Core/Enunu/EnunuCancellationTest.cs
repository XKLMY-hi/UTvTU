using System;
using System.Threading;
using Xunit;

namespace OpenUtau.Core.Enunu {
    public class EnunuCancellationTest {
        [Fact]
        public void CancelledRequestStopsBeforeOpeningASocketAndRestoresScope() {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.False(EnunuConnection.CurrentCancellation.IsCancellationRequested);
            using (EnunuConnection.WithCancellation(cancellation.Token)) {
                Assert.Throws<OperationCanceledException>(() => EnunuClient.Inst.Send(new object[] { "ver_check" }, "15556", 300));
                using (EnunuConnection.WithCancellation(CancellationToken.None)) {
                    Assert.False(EnunuConnection.CurrentCancellation.IsCancellationRequested);
                }
                Assert.True(EnunuConnection.CurrentCancellation.IsCancellationRequested);
            }
            Assert.False(EnunuConnection.CurrentCancellation.IsCancellationRequested);
        }
    }
}

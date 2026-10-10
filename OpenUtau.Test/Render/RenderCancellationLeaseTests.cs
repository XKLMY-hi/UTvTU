using System;
using System.Threading;
using OpenUtau.Core.Render;
using Xunit;

namespace OpenUtau.Test.Render {
    public class RenderCancellationLeaseTests {
        [Fact]
        public void ReplacementCancelsImmediately_ButKeepsSourceAliveUntilBackgroundBatchLeaves() {
            CancellationTokenSource slot = null!;
            var first = RenderCancellationLease.Replace(ref slot);
            var child = first.Acquire();
            var old = first.Source;
            var token = old.Token;
            first.Dispose(); // Foreground batch returned; background still owns the source.
            using var second = RenderCancellationLease.Replace(ref slot);
            Assert.True(token.IsCancellationRequested);
            Assert.True(old.Token.IsCancellationRequested);
            child.Dispose();
            child.Dispose();
            first.Dispose();
            Assert.Throws<ObjectDisposedException>(() => old.Token);
            Assert.False(second.Source.IsCancellationRequested);
        }
        [Fact]
        public void CompletedCurrentSource_RemainsCancelableUntilSlotIsReplaced() {
            CancellationTokenSource slot = null!;
            var first = RenderCancellationLease.Replace(ref slot);
            first.Dispose();
            slot.Cancel();
            Assert.True(slot.Token.IsCancellationRequested);
            using var second = RenderCancellationLease.Replace(ref slot);
            Assert.Throws<ObjectDisposedException>(() => first.Source.Token);
        }
        [Fact]
        public void ExternalSource_IsCanceledAndRemainsOwnedByItsCaller() {
            using var external = new CancellationTokenSource();
            var slot = external;
            using var operation = RenderCancellationLease.Replace(ref slot);
            Assert.True(external.Token.IsCancellationRequested);
            Assert.Same(operation.Source, slot);
        }
    }
}

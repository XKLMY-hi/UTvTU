using System;
using System.Threading;
using OpenUtau.Core.Render;
using Xunit;

namespace OpenUtau.Test.Render {
    public class RenderCancellationLeaseTests {
        [Fact]
        public void ParentCancellationFollowsEveryReplacement_AndRetirementDoesNotCancelTheParent() {
            using var parent = new CancellationTokenSource();
            CancellationTokenSource slot = null!;
            var first = RenderCancellationLease.Replace(ref slot, parent.Token);
            first.Dispose();
            var second = RenderCancellationLease.Replace(ref slot, parent.Token);
            Assert.False(parent.IsCancellationRequested);
            Assert.Throws<ObjectDisposedException>(() => first.Source.Token);
            parent.Cancel();
            Assert.True(second.Source.IsCancellationRequested);
            var borrowed = second.Acquire();
            second.Dispose();
            RenderCancellationLease.Retire(ref slot);
            Assert.Null(slot);
            Assert.True(second.Source.Token.IsCancellationRequested);
            borrowed.Dispose();
            Assert.Throws<ObjectDisposedException>(() => second.Source.Token);
        }

        [Fact]
        public void RetirementUnsubscribesFromParent_AndAlreadyCancelledParentStopsNewWork() {
            using var parent = new CancellationTokenSource();
            CancellationTokenSource slot = null!;
            var operation = RenderCancellationLease.Replace(ref slot, parent.Token);
            operation.Dispose();
            RenderCancellationLease.Retire(ref slot);
            Assert.Throws<ObjectDisposedException>(() => operation.Source.Token);
            parent.Cancel(); // No dangling callback may cancel a disposed child.
            using var cancelled = RenderCancellationLease.Replace(ref slot, parent.Token);
            Assert.True(cancelled.Source.IsCancellationRequested);
            RenderCancellationLease.Retire(ref slot);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ThrowingCancellationCallback_CannotBreakReplacementOrResourceRetirement(bool owned) {
            CancellationTokenSource slot = null!;
            var first = owned ? RenderCancellationLease.Replace(ref slot) : null;
            if (!owned) slot = new CancellationTokenSource();
            var old = slot;
            var token = old.Token;
            using var callback = token.Register(() => throw new InvalidOperationException("Renderer cancellation failed"));
            first?.Dispose();
            using var next = RenderCancellationLease.Replace(ref slot);
            Assert.True(token.IsCancellationRequested);
            Assert.Same(next.Source, slot);
            Assert.False(next.Source.IsCancellationRequested);
            if (owned) Assert.Throws<ObjectDisposedException>(() => old.Token);
            else { Assert.True(old.Token.IsCancellationRequested); old.Dispose(); }
        }

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

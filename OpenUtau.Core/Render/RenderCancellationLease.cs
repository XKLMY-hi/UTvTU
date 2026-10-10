using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace OpenUtau.Core.Render {
    /// <summary>The shared slot cancels a previous render immediately. Its source
    /// is disposed only after every foreground/background operation has left.</summary>
    internal sealed class RenderCancellationLease : IDisposable {
        static readonly ConditionalWeakTable<CancellationTokenSource, RenderCancellationLease> owners = new();
        readonly object sync = new();
        int users = 1;
        bool retired, canceling, disposed, ownerReleased;
        public CancellationTokenSource Source { get; } = new();

        public static RenderCancellationLease Replace(ref CancellationTokenSource slot) {
            var next = new RenderCancellationLease();
            owners.Add(next.Source, next);
            var previous = Interlocked.Exchange(ref slot, next.Source);
            if (previous != null) {
                if (owners.TryGetValue(previous, out var owner)) owner.Retire();
                else {
                    // An external source remains owned by its caller.
                    try { previous.Cancel(); } catch (ObjectDisposedException) { }
                }
            }
            return next;
        }

        public IDisposable Acquire() {
            lock (sync) {
                if (disposed) throw new ObjectDisposedException(nameof(RenderCancellationLease));
                ++users;
                return new Borrow(this);
            }
        }

        void Retire() {
            lock (sync) {
                if (retired) return;
                retired = true;
                canceling = true;
            }
            // Cancellation callbacks may finish/release work themselves. Do not
            // hold our lifetime lock while invoking arbitrary renderer callbacks.
            try { Source.Cancel(); }
            finally {
                lock (sync) {
                    canceling = false;
                    DisposeIfIdle();
                }
            }
        }
        void Release() {
            lock (sync) {
                --users;
                DisposeIfIdle();
            }
        }
        void DisposeIfIdle() {
            if (retired && !canceling && users == 0 && !disposed) {
                disposed = true;
                Source.Dispose();
            }
        }
        public void Dispose() {
            lock (sync) {
                if (ownerReleased) return;
                ownerReleased = true;
                --users;
                DisposeIfIdle();
            }
        }
        sealed class Borrow : IDisposable {
            RenderCancellationLease? owner;
            public Borrow(RenderCancellationLease owner) => this.owner = owner;
            public void Dispose() => Interlocked.Exchange(ref owner, null)?.Release();
        }
    }
}

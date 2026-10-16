using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace OpenUtau.App.Controls;

// Shared by realized resource/picker rows. No voicebank initialization or full-size image decode.
internal static class SingerThumbnailCache {
    internal const int Capacity = 96;
    static readonly object sync = new();
    static readonly SemaphoreSlim workers = new(2);
    static readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    static long clock;
    internal sealed class Entry {
        public required Bitmap Bitmap;
        public int Users;
        public long Used;
    }
    internal sealed class Lease : IDisposable {
        Entry? entry;
        public Bitmap Bitmap => entry?.Bitmap ?? throw new ObjectDisposedException(nameof(Lease));
        private Lease(Entry entry) { this.entry = entry; }
        public void Dispose() {
            lock (sync) {
                if (entry == null) return;
                entry.Users--;
                entry = null;
                Trim();
            }
        }
        internal static Lease Take(Entry entry) => new(entry);
    }

    internal static async Task<Lease?> AcquireAsync(byte[]? data, string? path, int revision, CancellationToken cancellation) {
        await workers.WaitAsync(cancellation).ConfigureAwait(false);
        try {
            return await Task.Run(() => Acquire(data, path, revision, cancellation), cancellation).ConfigureAwait(false);
        } finally { workers.Release(); }
    }

    static Lease? Acquire(byte[]? data, string? path, int revision, CancellationToken cancellation) {
        cancellation.ThrowIfCancellationRequested();
        string key;
        if (data is { Length: > 0 }) key = "data:" + Convert.ToHexString(SHA256.HashData(data)) + ":" + revision;
        else if (!string.IsNullOrEmpty(path) && Path.IsPathFullyQualified(path)) {
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            key = "file:" + info.FullName.ToUpperInvariant() + ":" + info.LastWriteTimeUtc.Ticks + ":" + info.Length + ":" + revision;
        } else return null;
        lock (sync) {
            if (entries.TryGetValue(key, out var cached)) {
                cached.Users++; cached.Used = ++clock;
                return Lease.Take(cached);
            }
        }
        using Stream stream = data is { Length: > 0 } ? new MemoryStream(data, false) : File.OpenRead(path!);
        var bitmap = Bitmap.DecodeToWidth(stream, 72);
        if (cancellation.IsCancellationRequested) { bitmap.Dispose(); cancellation.ThrowIfCancellationRequested(); }
        lock (sync) {
            if (entries.TryGetValue(key, out var cached)) {
                bitmap.Dispose(); cached.Users++; cached.Used = ++clock;
                return Lease.Take(cached);
            }
            var entry = new Entry { Bitmap = bitmap, Users = 1, Used = ++clock };
            entries.Add(key, entry);
            Trim();
            return Lease.Take(entry);
        }
    }

    static void Trim() {
        foreach (var item in entries.Where(pair => pair.Value.Users == 0).OrderBy(pair => pair.Value.Used).Take(Math.Max(0, entries.Count - Capacity)).ToArray()) {
            entries.Remove(item.Key);
            item.Value.Bitmap.Dispose();
        }
    }
}

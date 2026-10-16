using System.Security.Cryptography;
using System.Text;

namespace UTvTU.Setup;

/// <summary>Protects the whole file + registration operation across awaits/processes.
/// A semaphore can be released by a continuation on another thread; a mutex cannot.</summary>
public sealed class SetupOperationLease : IDisposable {
    readonly Semaphore semaphore;
    bool disposed;
    public SetupOperationLease(string directory, string sid) {
        string identity = SetupPaths.Full(directory).ToUpperInvariant() + "\n" + sid;
        string name = @"Local\UTvTU.Setup." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        semaphore = new Semaphore(1, 1, name);
        if (!semaphore.WaitOne(0)) { semaphore.Dispose(); throw new IOException("此目录已有安装或卸载任务，请等待它结束。"); }
    }
    public void Dispose() { if (disposed) return; disposed = true; semaphore.Release(); semaphore.Dispose(); }
}

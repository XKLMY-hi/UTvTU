using System.Security.Cryptography;
using System.Text;

namespace UTvTU.Setup;

/// <summary>Verified PE overlay, appended after single-file publishing. The UI compiler
/// never loads the large ZIP as an assembly resource. No external payload is needed.</summary>
public static class PayloadContainer {
    static readonly byte[] Magic = Encoding.ASCII.GetBytes("UTVTU_PAYLOAD_CONTAINER_V1_______");
    static int FooterLength => Magic.Length + 16 + 32;
    sealed record Footer(long Offset, long Length, byte[] Hash);
    static Footer? ReadFooter(FileStream file) {
        if (file.Length < FooterLength) return null;
        file.Position = file.Length - FooterLength;
        using var reader = new BinaryReader(file, Encoding.UTF8, true);
        if (!reader.ReadBytes(Magic.Length).SequenceEqual(Magic)) return null;
        long offset = reader.ReadInt64(), length = reader.ReadInt64(); byte[] hash = reader.ReadBytes(32);
        if (offset <= 0 || length <= 0 || length > file.Length - FooterLength || offset != file.Length - FooterLength - length) throw new InvalidDataException("安装载荷范围无效。");
        return new(offset, length, hash);
    }
    public static bool Contains(string executable) { using var file = File.OpenRead(executable); return ReadFooter(file) != null; }
    public static Stream Open(string executable, bool verify = true) {
        var file = File.OpenRead(executable);
        try {
            var footer = ReadFooter(file) ?? throw new InvalidDataException("此文件不包含完整的安装载荷。");
            var stream = new Slice(file, footer.Offset, footer.Length);
            if (verify) {
                if (!SHA256.HashData(stream).SequenceEqual(footer.Hash)) { stream.Dispose(); throw new InvalidDataException("安装文件损坏，载荷校验失败，请重新下载。"); }
                stream.Position = 0;
            }
            return stream;
        } catch { file.Dispose(); throw; }
    }
    public static void Append(string host, string payload, string destination) {
        if (SetupPaths.Same(host, destination) || SetupPaths.Same(payload, destination)) throw new IOException("封装输出不能覆盖输入文件。");
        using var source = File.OpenRead(host); using var zip = File.OpenRead(payload);
        byte[] hash = SHA256.HashData(zip); zip.Position = 0;
        using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
            source.CopyTo(output); long offset = output.Position; zip.CopyTo(output);
            using var writer = new BinaryWriter(output, Encoding.UTF8, true);
            writer.Write(Magic); writer.Write(offset); writer.Write(zip.Length); writer.Write(hash);
        }
        using var verified = Open(destination);
        using var package = new SetupPackage(verified); package.Verify();
    }
    sealed class Slice(FileStream file, long offset, long length) : Stream {
        long position;
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => position; set => Seek(value, SeekOrigin.Begin); }
        public override int Read(byte[] buffer, int start, int count) => Read(buffer.AsSpan(start, count));
        public override int Read(Span<byte> buffer) {
            int count = (int)Math.Min(buffer.Length, length - position); if (count <= 0) return 0;
            file.Position = offset + position; int read = file.Read(buffer[..count]); position += read; return read;
        }
        public override long Seek(long delta, SeekOrigin origin) {
            long next = origin switch { SeekOrigin.Begin => delta, SeekOrigin.Current => checked(position + delta), SeekOrigin.End => checked(length + delta), _ => throw new ArgumentException("无效的定位方式。") };
            if (next < 0 || next > length) throw new IOException("读取超出安装载荷范围。"); position = next; return next;
        }
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) file.Dispose(); base.Dispose(disposing); }
    }
}

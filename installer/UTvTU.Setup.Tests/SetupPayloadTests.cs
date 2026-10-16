using System.IO.Compression;
using UTvTU.Setup;
using Xunit;

[Collection("Setup")]
public class SetupPayloadTests : IDisposable {
    readonly string root = Path.Combine(Path.GetTempPath(), "UTvTU-Setup-Payload-Tests", Guid.NewGuid().ToString("N"));
    public SetupPayloadTests() => Directory.CreateDirectory(root);
    public void Dispose() { if (!SetupPaths.Within(Path.Combine(Path.GetTempPath(), "UTvTU-Setup-Payload-Tests"), root)) throw new Exception("Invalid test cleanup."); Directory.Delete(root, true); }
    string Bundle() {
        string host = Path.Combine(root, "host.exe"), zip = Path.Combine(root, "payload.zip"), output = Path.Combine(root, "setup.exe");
        File.WriteAllText(host, "synthetic apphost");
        using (var payload = SetupEngineTests.Package()) using (var file = File.Create(zip)) payload.CopyTo(file);
        Assert.False(PayloadContainer.Contains(host)); PayloadContainer.Append(host, zip, output); return output;
    }
    [Fact] public void SingleFilePayloadRemainsReadableWithoutAnyExternalZip() {
        string output = Bundle(); File.Delete(Path.Combine(root, "payload.zip")); File.Delete(Path.Combine(root, "host.exe"));
        Assert.True(PayloadContainer.Contains(output));
        using var stream = PayloadContainer.Open(output); using var archive = new SetupPackage(stream);
        archive.Verify(); Assert.Equal(2, archive.Manifest.Files.Count);
    }
    [Fact] public void CorruptedPayloadIsRejectedBeforeExtraction() {
        string output = Bundle();
        using (var file = new FileStream(output, FileMode.Open, FileAccess.ReadWrite)) { file.Position = "synthetic apphost".Length + 10; file.WriteByte(0xff); }
        Assert.Throws<InvalidDataException>(() => PayloadContainer.Open(output));
    }
    [Fact] public void ZipViewCannotSeekIntoHostOrFooter() {
        string output = Bundle(); using var stream = PayloadContainer.Open(output);
        Assert.Throws<IOException>(() => stream.Seek(-1, SeekOrigin.Begin));
        Assert.Throws<IOException>(() => stream.Seek(1, SeekOrigin.End));
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read); Assert.Equal(3, zip.Entries.Count);
    }
}

using PortableSecureNotepad.Core;
using System.Text;

namespace PortableSecureNotepad.Tests;

[TestClass]
public sealed class EmbeddedPayloadTests
{
    [TestMethod]
    public void Locate_ReturnsNoPayloadWhenFooterIsMissing()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("plain executable"));

        PayloadLocation location = EmbeddedPayload.Locate(stream);

        Assert.IsFalse(location.HasPayload);
        Assert.AreEqual(stream.Length, location.BaseLength);
    }

    [TestMethod]
    public void WriteExecutableWithPayload_AppendsReadablePayload()
    {
        using var temp = new TempDirectory();
        string source = temp.CreateFile("source.exe", "base-exe");
        string destination = temp.GetPath("destination.exe");

        EmbeddedPayload.WriteExecutableWithPayload(source, destination, Encoding.UTF8.GetBytes("vault-json"));

        byte[]? payload = EmbeddedPayload.ReadPayload(destination);
        Assert.IsNotNull(payload);
        Assert.AreEqual("vault-json", Encoding.UTF8.GetString(payload));
    }

    [TestMethod]
    public void WriteExecutableWithPayload_StripsPreviousPayloadBeforeAppending()
    {
        using var temp = new TempDirectory();
        string source = temp.CreateFile("source.exe", "base-exe");
        string first = temp.GetPath("first.exe");
        string second = temp.GetPath("second.exe");

        EmbeddedPayload.WriteExecutableWithPayload(source, first, Encoding.UTF8.GetBytes("old-vault"));
        EmbeddedPayload.WriteExecutableWithPayload(first, second, Encoding.UTF8.GetBytes("new-vault"));

        byte[]? payload = EmbeddedPayload.ReadPayload(second);
        Assert.IsNotNull(payload);
        Assert.AreEqual("new-vault", Encoding.UTF8.GetString(payload));

        using FileStream secondStream = File.OpenRead(second);
        PayloadLocation location = EmbeddedPayload.Locate(secondStream);
        Assert.AreEqual(new FileInfo(source).Length, location.BaseLength);
    }

    [TestMethod]
    public void WriteExecutableWithoutPayload_CopiesCleanBaseOnly()
    {
        using var temp = new TempDirectory();
        string source = temp.CreateFile("source.exe", "base-exe");
        string withPayload = temp.GetPath("with-payload.exe");
        string clean = temp.GetPath("clean.exe");

        EmbeddedPayload.WriteExecutableWithPayload(source, withPayload, Encoding.UTF8.GetBytes("vault"));
        EmbeddedPayload.WriteExecutableWithoutPayload(withPayload, clean);

        Assert.IsNull(EmbeddedPayload.ReadPayload(clean));
        Assert.AreEqual("base-exe", File.ReadAllText(clean));
    }

    private sealed class TempDirectory : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"psn-tests-{Guid.NewGuid():N}");

        public TempDirectory()
        {
            Directory.CreateDirectory(_path);
        }

        public string GetPath(string fileName)
        {
            return Path.Combine(_path, fileName);
        }

        public string CreateFile(string fileName, string contents)
        {
            string path = GetPath(fileName);
            File.WriteAllText(path, contents);
            return path;
        }

        public void Dispose()
        {
            Directory.Delete(_path, recursive: true);
        }
    }
}

using PortableSecureNotepad.Core;
using System.Text;

namespace PortableSecureNotepad.Tests;

[TestClass]
public sealed class VaultTransferTests
{
    private static VaultDocument Document() => new()
    {
        Notes = [new NoteEntry { Text = "  ʻōlelo 日本語\r\nmultiline notes  ",
            CreatedAt = DateTimeOffset.Parse("2026-10-01T10:30:00-10:00"),
            UpdatedAt = DateTimeOffset.Parse("2026-10-12T09:00:00-10:00") }]
    };

    [TestMethod]
    public void EncryptedBackup_RoundTripsAndRequiresPassword()
    {
        using var temp = new TransferFixture();
        var original = Document();
        byte[] payload = VaultCrypto.Encrypt(original, "password");
        string path = temp.PathFor("backup.psnvault");
        VaultTransfer.WriteExport(path, payload, VaultExportFormat.Encrypted);
        byte[] backup = VaultTransfer.ReadEncryptedBackup(path);
        Assert.ThrowsExactly<VaultUnlockException>(() => VaultCrypto.Decrypt(backup, "wrong"));
        Assert.AreEqual(original.Notes[0].Text, VaultCrypto.Decrypt(backup, "password").Notes[0].Text);
        CollectionAssert.AreEqual(payload, File.ReadAllBytes(path));
    }

    [TestMethod]
    public void JsonAndTextExports_PreserveUnicodeWhitespaceAndEditMetadata()
    {
        using var temp = new TransferFixture();
        var original = Document();
        string jsonPath = temp.PathFor("notes.json");
        VaultTransfer.WriteExport(jsonPath, VaultTransfer.SerializePlain(original, VaultExportFormat.Json), VaultExportFormat.Json);
        var restored = VaultTransfer.ReadPlainJson(jsonPath);
        Assert.AreEqual(original.Notes[0].Text, restored.Notes[0].Text);
        Assert.AreEqual(original.Notes[0].Id, restored.Notes[0].Id);
        Assert.AreEqual(original.Notes[0].CreatedAt, restored.Notes[0].CreatedAt);
        Assert.AreEqual(original.Notes[0].UpdatedAt, restored.Notes[0].UpdatedAt);
        string text = Encoding.UTF8.GetString(VaultTransfer.SerializePlain(original, VaultExportFormat.Text));
        StringAssert.Contains(text, original.Notes[0].Text);
        StringAssert.Contains(text, "Edited: 2026-10-12T09:00:00.0000000-10:00");
    }

    [TestMethod]
    public void ImportOldPortableExe_ReadsVaultPayload()
    {
        using var temp = new TransferFixture();
        string clean = temp.PathFor("clean.exe");
        File.WriteAllText(clean, "inert executable");
        string backup = temp.PathFor("old.backup.exe");
        EmbeddedPayload.WriteExecutableWithPayload(clean, backup, VaultCrypto.Encrypt(Document(), "old-password"));
        var restored = VaultCrypto.Decrypt(VaultTransfer.ReadEncryptedBackup(backup), "old-password");
        Assert.AreEqual("  ʻōlelo 日本語\r\nmultiline notes  ", restored.Notes[0].Text);
    }

    [TestMethod]
    public void ExportLockedDestination_PreservesExistingFileAndRemovesStagingFile()
    {
        using var temp = new TransferFixture();
        string path = temp.PathFor("notes.json");
        File.WriteAllText(path, "previous valuable data");
        using (var heldOpen = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.ThrowsExactly<IOException>(() => VaultTransfer.WriteExport(path,
                VaultTransfer.SerializePlain(Document(), VaultExportFormat.Json), VaultExportFormat.Json));
        }
        Assert.AreEqual("previous valuable data", File.ReadAllText(path));
        Assert.IsEmpty(Directory.GetFiles(temp.Root, "*.tmp"));
    }

    [TestMethod]
    public void ExportCannotOverwriteExecutableOrUseWrongExtension()
    {
        using var temp = new TransferFixture();
        string exe = temp.PathFor("notepad.exe");
        File.WriteAllText(exe, "existing executable and vault");
        Assert.ThrowsExactly<ArgumentException>(() => VaultTransfer.WriteExport(exe,
            VaultTransfer.SerializePlain(Document(), VaultExportFormat.Json), VaultExportFormat.Json));
        Assert.AreEqual("existing executable and vault", File.ReadAllText(exe));
        Assert.ThrowsExactly<ArgumentException>(() => VaultTransfer.WriteExport(temp.PathFor("wrong.txt"),
            VaultCrypto.Encrypt(Document(), "password"), VaultExportFormat.Encrypted));
    }

    [TestMethod]
    public void ExportOverwrite_WritesCompleteVerifiedFile()
    {
        using var temp = new TransferFixture();
        string path = temp.PathFor("backup.psnvault");
        File.WriteAllText(path, "previous backup");
        byte[] payload = VaultCrypto.Encrypt(Document(), "password");
        VaultTransfer.WriteExport(path, payload, VaultExportFormat.Encrypted);
        CollectionAssert.AreEqual(payload, File.ReadAllBytes(path));
        Assert.IsEmpty(Directory.GetFiles(temp.Root, "*.tmp"));
    }

    [TestMethod]
    public void BackupRead_RejectsMalformedAndOversizedFiles()
    {
        using var temp = new TransferFixture();
        string bad = temp.PathFor("bad.json");
        File.WriteAllText(bad, "{\"version\":1,\"notes\":null}");
        Assert.ThrowsExactly<InvalidDataException>(() => VaultTransfer.ReadPlainJson(bad));
        File.WriteAllText(bad, "{\"unrelated\":true}");
        Assert.ThrowsExactly<InvalidDataException>(() => VaultTransfer.ReadPlainJson(bad));
        string large = temp.PathFor("large.psnvault");
        using (var output = File.Create(large)) output.SetLength(EmbeddedPayload.MaximumPayloadLength + 1L);
        Assert.ThrowsExactly<InvalidDataException>(() => VaultTransfer.ReadEncryptedBackup(large));
    }

    private sealed class TransferFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"psn-transfer-{Guid.NewGuid():N}");
        public TransferFixture() => Directory.CreateDirectory(Root);
        public string PathFor(string name) => Path.Combine(Root, name);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

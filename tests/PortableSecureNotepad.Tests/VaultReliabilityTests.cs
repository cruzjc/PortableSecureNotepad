using PortableSecureNotepad.Core;
using System.Buffers.Binary;
using System.Text;

namespace PortableSecureNotepad.Tests;

[TestClass]
public sealed class VaultReliabilityTests
{
    private const string Password = "test-password";

    [TestMethod]
    public void Cleanup_PreservesOldPendingVaultsAndBackups()
    {
        using var fixture = new VaultFixture();
        var request = fixture.Store.PrepareCommit(Payload("new note"));
        File.Copy(fixture.Target, request.BackupPath);
        foreach (string path in new[] { request.ReplacementPath, request.BackupPath, request.HelperPath })
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-2));

        fixture.Store.CleanupStaleTemporaryFiles(TimeSpan.FromHours(1));

        Assert.IsTrue(File.Exists(request.ReplacementPath));
        Assert.IsTrue(File.Exists(request.BackupPath));
        Assert.IsFalse(File.Exists(request.HelperPath));
        Assert.AreEqual("new note", ReadNote(request.ReplacementPath));
        CollectionAssert.Contains(fixture.Store.GetPendingRecoveryPaths().ToArray(), request.ReplacementPath);
    }

    [TestMethod]
    public void Replacement_RetainsPreviousDecryptableVaultAndVerifiesNewVault()
    {
        using var fixture = new VaultFixture();
        string previousHash = ExecutableVaultStore.GetFileHash(fixture.Target);
        var request = fixture.Store.PrepareCommit(Payload("new note"));

        string committedHash = ExecutableReplacement.CompleteReplacement(request);

        Assert.AreEqual("new note", ReadNote(fixture.Target));
        Assert.AreEqual("old note", ReadNote(request.BackupPath));
        Assert.AreEqual(previousHash, ExecutableVaultStore.GetFileHash(request.BackupPath));
        Assert.AreEqual(committedHash, ExecutableVaultStore.GetFileHash(fixture.Target));
        Assert.IsFalse(File.Exists(request.ReplacementPath));
    }

    [TestMethod]
    public void Replacement_RejectsStaleSecondWriterAndPreservesItsRecoveryCopy()
    {
        using var fixture = new VaultFixture();
        var first = fixture.Store.PrepareCommit(Payload("first writer"));
        var second = fixture.Store.PrepareCommit(Payload("second writer"));
        ExecutableReplacement.CompleteReplacement(first);

        Assert.ThrowsExactly<InvalidDataException>(() => ExecutableReplacement.CompleteReplacement(second));

        Assert.AreEqual("first writer", ReadNote(fixture.Target));
        Assert.AreEqual("second writer", ReadNote(second.ReplacementPath));
        Assert.AreEqual("old note", ReadNote(first.BackupPath));
    }

    [TestMethod]
    public void PrepareCommit_RejectsExternallyChangedTarget()
    {
        using var fixture = new VaultFixture();
        File.AppendAllText(fixture.Target, "external change");

        Assert.ThrowsExactly<IOException>(() => fixture.Store.PrepareCommit(Payload("pending")));
        Assert.IsEmpty(fixture.Store.GetPendingRecoveryPaths());
    }

    [TestMethod]
    public void Replacement_DoesNotOverwriteExistingBackup()
    {
        using var fixture = new VaultFixture();
        var request = fixture.Store.PrepareCommit(Payload("pending"));
        File.WriteAllText(request.BackupPath, "valuable recovery data");

        Assert.ThrowsExactly<IOException>(() => ExecutableReplacement.CompleteReplacement(request));

        Assert.AreEqual("valuable recovery data", File.ReadAllText(request.BackupPath));
        Assert.AreEqual("old note", ReadNote(fixture.Target));
        Assert.AreEqual("pending", ReadNote(request.ReplacementPath));
    }

    [TestMethod]
    public void Replacement_RejectsMissingVaultWithoutTouchingTarget()
    {
        using var fixture = new VaultFixture();
        var request = fixture.Store.PrepareCommit(Payload("pending"));
        File.WriteAllText(request.ReplacementPath, "incomplete replacement");

        Assert.ThrowsExactly<InvalidDataException>(() => ExecutableReplacement.CompleteReplacement(request));

        Assert.AreEqual("old note", ReadNote(fixture.Target));
        Assert.IsTrue(File.Exists(request.ReplacementPath));
    }

    [TestMethod]
    public void Replacement_LockedTargetPreservesPendingAndOriginalVault()
    {
        using var fixture = new VaultFixture();
        var request = fixture.Store.PrepareCommit(Payload("pending"));
        using var heldOpen = new FileStream(fixture.Target, FileMode.Open, FileAccess.Read, FileShare.Read);

        Assert.ThrowsExactly<IOException>(() => ExecutableReplacement.CompleteReplacement(request));

        Assert.AreEqual("old note", ReadNote(fixture.Target));
        Assert.AreEqual("pending", ReadNote(request.ReplacementPath));
    }

    [TestMethod]
    public void InstanceLock_ExcludesAnotherThreadUntilOwnerReleases()
    {
        using var fixture = new VaultFixture();
        var owner = VaultInstanceLock.TryAcquire(fixture.Target, TimeSpan.Zero);
        Assert.IsNotNull(owner);
        bool wasBlocked = false;
        Exception? threadError = null;
        var contender = new Thread(() =>
        {
            try
            {
                using var other = VaultInstanceLock.TryAcquire(fixture.Target.ToUpperInvariant(), TimeSpan.Zero);
                wasBlocked = other is null;
            }
            catch (Exception ex) { threadError = ex; }
        });
        try { contender.Start(); contender.Join(); }
        finally { owner.Dispose(); }
        Assert.IsNull(threadError);
        Assert.IsTrue(wasBlocked);
        using var reopened = VaultInstanceLock.TryAcquire(fixture.Target, TimeSpan.Zero);
        Assert.IsNotNull(reopened);
    }

    [TestMethod]
    public void InstanceLock_AllowsDifferentPortableExecutables()
    {
        using var fixture = new VaultFixture();
        using var first = VaultInstanceLock.TryAcquire(fixture.Target, TimeSpan.Zero);
        using var second = VaultInstanceLock.TryAcquire(fixture.Target + ".other.exe", TimeSpan.Zero);
        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
    }

    [TestMethod]
    public void CleanupArguments_OnlyAllowSiblingHelpers()
    {
        using var fixture = new VaultFixture();
        var request = fixture.Store.PrepareCommit(Payload("pending"));
        string[] args = ["--cleanup", request.ReplacementPath, "--cleanup", request.BackupPath,
            "--cleanup", fixture.Target, "--cleanup", request.HelperPath,
            "--cleanup", Path.Combine(Path.GetTempPath(), "foreign.psn-id.helper.exe")];

        CollectionAssert.AreEqual(new[] { request.HelperPath },
            ExecutableReplacement.GetCleanupPaths(args, fixture.Target).ToArray());
    }

    [TestMethod]
    public void ReadPayload_RejectsCorruptFooter()
    {
        using var fixture = new VaultFixture();
        byte[] bytes = File.ReadAllBytes(fixture.Target);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(bytes.Length - EmbeddedPayload.FooterSize), ulong.MaxValue);
        File.WriteAllBytes(fixture.Target, bytes);

        Assert.ThrowsExactly<InvalidDataException>(() => EmbeddedPayload.ReadPayload(fixture.Target));
    }

    [TestMethod]
    public void ReadPayload_RejectsOversizedPayloadBeforeAllocating()
    {
        using var fixture = new VaultFixture();
        using (var output = new FileStream(fixture.Target, FileMode.Create, FileAccess.Write))
        {
            output.SetLength(EmbeddedPayload.MaximumPayloadLength + EmbeddedPayload.FooterSize + 1L);
            output.Position = output.Length - EmbeddedPayload.FooterSize;
            byte[] footer = new byte[EmbeddedPayload.FooterSize];
            BinaryPrimitives.WriteUInt64LittleEndian(footer, EmbeddedPayload.MaximumPayloadLength + 1UL);
            Encoding.ASCII.GetBytes("PSNVAULT.FOOTER1").CopyTo(footer, sizeof(ulong));
            output.Write(footer);
        }

        Assert.ThrowsExactly<InvalidDataException>(() => EmbeddedPayload.ReadPayload(fixture.Target));
    }

    private static byte[] Payload(string text) => VaultCrypto.Encrypt(
        new VaultDocument { Notes = [new NoteEntry { Text = text }] }, Password);

    private static string ReadNote(string path) => VaultCrypto.Decrypt(EmbeddedPayload.ReadPayload(path)!, Password).Notes[0].Text;

    private sealed class VaultFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"psn-reliability-{Guid.NewGuid():N}");
        public string Target { get; }
        public ExecutableVaultStore Store { get; }

        public VaultFixture()
        {
            Directory.CreateDirectory(_directory);
            string clean = Path.Combine(_directory, "clean.exe");
            File.WriteAllText(clean, "inert test executable");
            Target = Path.Combine(_directory, "notepad.exe");
            EmbeddedPayload.WriteExecutableWithPayload(clean, Target, Payload("old note"));
            Store = new ExecutableVaultStore(Target);
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}

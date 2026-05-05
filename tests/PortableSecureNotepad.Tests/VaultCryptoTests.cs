using PortableSecureNotepad.Core;

namespace PortableSecureNotepad.Tests;

[TestClass]
public sealed class VaultCryptoTests
{
    [TestMethod]
    public void EncryptAndDecrypt_RoundTripsVaultDocument()
    {
        var document = new VaultDocument
        {
            Notes =
            [
                new NoteEntry
                {
                    Id = Guid.NewGuid(),
                    CreatedAt = DateTimeOffset.Parse("2026-05-04T10:15:00-10:00"),
                    Text = "first secure note"
                }
            ]
        };

        byte[] encrypted = VaultCrypto.Encrypt(document, "1234");
        VaultDocument decrypted = VaultCrypto.Decrypt(encrypted, "1234");

        Assert.AreEqual(1, decrypted.Notes.Count);
        Assert.AreEqual(document.Notes[0].Id, decrypted.Notes[0].Id);
        Assert.AreEqual(document.Notes[0].CreatedAt, decrypted.Notes[0].CreatedAt);
        Assert.AreEqual("first secure note", decrypted.Notes[0].Text);
    }

    [TestMethod]
    public void Decrypt_WithWrongPasswordThrowsUnlockException()
    {
        var document = new VaultDocument
        {
            Notes = [new NoteEntry { Text = "secret" }]
        };

        byte[] encrypted = VaultCrypto.Encrypt(document, "correct-pin");

        Assert.ThrowsExactly<VaultUnlockException>(() => VaultCrypto.Decrypt(encrypted, "wrong-pin"));
    }
}

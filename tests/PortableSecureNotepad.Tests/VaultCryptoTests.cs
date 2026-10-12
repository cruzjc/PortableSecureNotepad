using PortableSecureNotepad.Core;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace PortableSecureNotepad.Tests;

[TestClass]
public sealed class VaultCryptoTests
{
    [TestMethod]
    [DataRow("salt")]
    [DataRow("nonce")]
    [DataRow("tag")]
    [DataRow("ciphertext")]
    public void Decrypt_RejectsNullEnvelopeFields(string field)
    {
        var envelope = JsonNode.Parse(VaultCrypto.Encrypt(new VaultDocument(), "password"))!;
        envelope[field] = null;
        Assert.ThrowsExactly<InvalidDataException>(() => VaultCrypto.Decrypt(
            Encoding.UTF8.GetBytes(envelope.ToJsonString()), "password"));
    }

    [TestMethod]
    [DataRow(99_999)]
    [DataRow(int.MaxValue)]
    public void Decrypt_RejectsOutOfRangeIterationsBeforeDerivation(int iterations)
    {
        var envelope = JsonNode.Parse(VaultCrypto.Encrypt(new VaultDocument(), "password"))!;
        envelope["iterations"] = iterations;
        Assert.ThrowsExactly<InvalidDataException>(() => VaultCrypto.Decrypt(
            Encoding.UTF8.GetBytes(envelope.ToJsonString()), "password"));
    }

    [TestMethod]
    [DataRow("{\"version\":1,\"notes\":null}")]
    [DataRow("{\"version\":1,\"notes\":[null]}")]
    [DataRow("{\"version\":1,\"notes\":[{\"text\":null}]}")]
    [DataRow("{\"version\":99,\"notes\":[]}")]
    public void Decrypt_RejectsAuthenticatedInvalidDocuments(string json)
    {
        var envelope = JsonNode.Parse(VaultCrypto.Encrypt(new VaultDocument(), "password"))!;
        byte[] key = Rfc2898DeriveBytes.Pbkdf2("password",
            Convert.FromBase64String(envelope["salt"]!.GetValue<string>()),
            VaultCrypto.DefaultIterations, HashAlgorithmName.SHA256, 32);
        byte[] plaintext = Encoding.UTF8.GetBytes(json);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[16];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(Convert.FromBase64String(envelope["nonce"]!.GetValue<string>()),
                plaintext, ciphertext, tag, "PortableSecureNotepad.Vault.v1"u8);
            envelope["ciphertext"] = Convert.ToBase64String(ciphertext);
            envelope["tag"] = Convert.ToBase64String(tag);
            Assert.ThrowsExactly<InvalidDataException>(() => VaultCrypto.Decrypt(
                Encoding.UTF8.GetBytes(envelope.ToJsonString()), "password"));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    [TestMethod]
    public void Decrypt_RejectsTamperedCiphertext()
    {
        var envelope = JsonNode.Parse(VaultCrypto.Encrypt(new VaultDocument(), "password"))!;
        byte[] ciphertext = Convert.FromBase64String(envelope["ciphertext"]!.GetValue<string>());
        ciphertext[0] ^= 1;
        envelope["ciphertext"] = Convert.ToBase64String(ciphertext);
        Assert.ThrowsExactly<VaultUnlockException>(() => VaultCrypto.Decrypt(
            Encoding.UTF8.GetBytes(envelope.ToJsonString()), "password"));
    }

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

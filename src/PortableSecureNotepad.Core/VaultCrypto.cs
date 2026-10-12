using System.Security.Cryptography;
using System.Text.Json;

namespace PortableSecureNotepad.Core;

public static class VaultCrypto
{
    public const int CurrentVersion = 1;
    public const int DefaultIterations = 210_000;

    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int KeySize = 32;
    private const int TagSize = 16;

    private static readonly byte[] AssociatedData = "PortableSecureNotepad.Vault.v1"u8.ToArray();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static byte[] Encrypt(VaultDocument document, string password)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateDocument(document);
        ValidatePassword(password);

        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] key = DeriveKey(password, salt, DefaultIterations);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSize];

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData);

            var envelope = new VaultEnvelope
            {
                Version = CurrentVersion,
                Kdf = "PBKDF2-SHA256",
                Iterations = DefaultIterations,
                Salt = salt,
                Nonce = nonce,
                Ciphertext = ciphertext,
                Tag = tag
            };

            return JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public static VaultDocument Decrypt(ReadOnlySpan<byte> envelopeBytes, string password)
    {
        ValidatePassword(password);

        VaultEnvelope envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<VaultEnvelope>(envelopeBytes, JsonOptions)
                ?? throw new InvalidDataException("Vault payload is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Vault payload is not valid JSON.", ex);
        }

        ValidateEnvelope(envelope);

        byte[] key = DeriveKey(password, envelope.Salt, envelope.Iterations);
        byte[] plaintext = new byte[envelope.Ciphertext.Length];

        try
        {
            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(envelope.Nonce, envelope.Ciphertext, envelope.Tag, plaintext, AssociatedData);
            }
            catch (CryptographicException ex)
            {
                throw new VaultUnlockException("Password/PIN did not unlock this vault.", ex);
            }
            VaultDocument document = JsonSerializer.Deserialize<VaultDocument>(plaintext, JsonOptions)
                ?? throw new InvalidDataException("Vault document is empty.");
            ValidateDocument(document);
            return document;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Vault document is not valid JSON.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static byte[] DeriveKey(string password, byte[] salt, int iterations)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            KeySize);
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password/PIN cannot be empty.", nameof(password));
        }
    }

    private static void ValidateEnvelope(VaultEnvelope envelope)
    {
        if (envelope.Version != CurrentVersion)
        {
            throw new InvalidDataException($"Unsupported vault version {envelope.Version}.");
        }

        if (!string.Equals(envelope.Kdf, "PBKDF2-SHA256", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Unsupported vault key derivation function.");
        }

        if (envelope.Iterations < 100_000 || envelope.Iterations > 2_000_000)
        {
            throw new InvalidDataException("Vault key derivation iteration count is outside the supported range.");
        }

        if (envelope.Salt is null || envelope.Salt.Length != SaltSize ||
            envelope.Nonce is null || envelope.Nonce.Length != NonceSize ||
            envelope.Tag is null || envelope.Tag.Length != TagSize ||
            envelope.Ciphertext is null || envelope.Ciphertext.Length == 0)
        {
            throw new InvalidDataException("Vault envelope has invalid cryptographic parameters.");
        }
    }

    internal static void ValidateDocument(VaultDocument document)
    {
        if (document.Version != CurrentVersion || document.Notes is null ||
            document.Notes.Any(note => note is null || note.Text is null) ||
            document.Notes.Select(note => note.Id).Distinct().Count() != document.Notes.Count)
        {
            throw new InvalidDataException("Vault document has an unsupported version or invalid note data.");
        }
    }

    private sealed class VaultEnvelope
    {
        public int Version { get; set; }

        public string Kdf { get; set; } = string.Empty;

        public int Iterations { get; set; }

        public byte[] Salt { get; set; } = [];

        public byte[] Nonce { get; set; } = [];

        public byte[] Ciphertext { get; set; } = [];

        public byte[] Tag { get; set; } = [];
    }
}

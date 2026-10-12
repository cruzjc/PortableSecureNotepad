using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PortableSecureNotepad.Core;

public enum VaultExportFormat { Encrypted, Json, Text }

public static class VaultTransfer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static byte[] SerializePlain(VaultDocument document, VaultExportFormat format)
    {
        VaultCrypto.ValidateDocument(document);
        if (format == VaultExportFormat.Json) return JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        if (format != VaultExportFormat.Text) throw new ArgumentException("Unsupported plain export format.", nameof(format));
        var text = new StringBuilder("Portable Secure Notepad - unencrypted export\r\n\r\n");
        foreach (NoteEntry note in document.Notes)
        {
            text.AppendLine($"Created: {note.CreatedAt:O}");
            if (note.UpdatedAt is DateTimeOffset updated) text.AppendLine($"Edited: {updated:O}");
            text.AppendLine($"ID: {note.Id}");
            text.AppendLine(note.Text);
            text.AppendLine("------------------------------");
        }
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    public static VaultDocument ReadPlainJson(string path)
    {
        byte[] bytes = ReadBoundedFile(path);
        try
        {
            using var json = JsonDocument.Parse(bytes);
            if (json.RootElement.ValueKind != JsonValueKind.Object ||
                !json.RootElement.TryGetProperty("version", out _) ||
                !json.RootElement.TryGetProperty("notes", out var notes) || notes.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("The JSON backup must contain a vault version and note array.");
            VaultDocument document = JsonSerializer.Deserialize<VaultDocument>(bytes, JsonOptions)
                ?? throw new InvalidDataException("The backup document is empty.");
            VaultCrypto.ValidateDocument(document);
            return document;
        }
        catch (JsonException ex) { throw new InvalidDataException("The JSON backup is not a valid vault document.", ex); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public static byte[] ReadEncryptedBackup(string path)
    {
        if (Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            return EmbeddedPayload.ReadPayload(path) ?? throw new InvalidDataException("This EXE has no saved vault.");
        return ReadBoundedFile(path);
    }

    public static void WriteExport(string path, byte[] bytes, VaultExportFormat format)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        string expectedExtension = format switch
        {
            VaultExportFormat.Encrypted => ".psnvault", VaultExportFormat.Json => ".json",
            VaultExportFormat.Text => ".txt", _ => throw new ArgumentException("Unsupported export format.")
        };
        string destination = Path.GetFullPath(path);
        if (!Path.GetExtension(destination).Equals(expectedExtension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Use a {expectedExtension} filename for this export.");
        if (bytes.Length == 0 || bytes.Length > EmbeddedPayload.MaximumPayloadLength)
            throw new InvalidDataException("The export is empty or exceeds the 64 MiB limit.");
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".psn-export-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(bytes);
                output.Flush(flushToDisk: true);
            }
            if (ExecutableVaultStore.GetFileHash(temporary) != Convert.ToHexString(SHA256.HashData(bytes)))
                throw new IOException("The export failed verification. The existing destination was preserved.");
            if (File.Exists(destination)) File.Replace(temporary, destination, null);
            else File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static byte[] ReadBoundedFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length == 0 || stream.Length > EmbeddedPayload.MaximumPayloadLength)
            throw new InvalidDataException("The backup is empty or exceeds the 64 MiB limit.");
        byte[] bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }
}

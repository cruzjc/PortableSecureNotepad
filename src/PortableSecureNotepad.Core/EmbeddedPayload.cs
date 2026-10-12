using System.Buffers.Binary;
using System.Text;

namespace PortableSecureNotepad.Core;

public sealed record PayloadLocation(
    bool HasPayload,
    long BaseLength,
    long PayloadOffset,
    long PayloadLength,
    long FooterOffset);

public static class EmbeddedPayload
{
    private static readonly byte[] FooterMagic = Encoding.ASCII.GetBytes("PSNVAULT.FOOTER1");

    public static int FooterSize => sizeof(ulong) + FooterMagic.Length;
    public const int MaximumPayloadLength = 64 * 1024 * 1024;

    public static PayloadLocation Locate(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek || !stream.CanRead)
        {
            throw new ArgumentException("Stream must be readable and seekable.", nameof(stream));
        }

        long fileLength = stream.Length;
        if (fileLength < FooterSize)
        {
            return NoPayload(fileLength);
        }

        Span<byte> footer = stackalloc byte[FooterSize];
        stream.Position = fileLength - FooterSize;
        ReadExactly(stream, footer);

        ReadOnlySpan<byte> magic = footer[sizeof(ulong)..];
        if (!magic.SequenceEqual(FooterMagic))
        {
            return NoPayload(fileLength);
        }

        ulong payloadLengthValue = BinaryPrimitives.ReadUInt64LittleEndian(footer[..sizeof(ulong)]);
        if (payloadLengthValue > long.MaxValue)
        {
            throw new InvalidDataException("Embedded payload length is invalid.");
        }

        long payloadLength = (long)payloadLengthValue;
        long payloadOffset = fileLength - FooterSize - payloadLength;
        if (payloadOffset < 0)
        {
            throw new InvalidDataException("Embedded payload length exceeds executable size.");
        }

        return new PayloadLocation(
            HasPayload: true,
            BaseLength: payloadOffset,
            PayloadOffset: payloadOffset,
            PayloadLength: payloadLength,
            FooterOffset: fileLength - FooterSize);
    }

    public static byte[]? ReadPayload(string executablePath)
    {
        using FileStream input = OpenExecutableForRead(executablePath);
        PayloadLocation location = Locate(input);
        if (!location.HasPayload)
        {
            return null;
        }

        if (location.PayloadLength == 0 || location.PayloadLength > MaximumPayloadLength)
        {
            throw new InvalidDataException("Embedded payload is empty or exceeds the 64 MiB limit.");
        }

        byte[] payload = new byte[location.PayloadLength];
        input.Position = location.PayloadOffset;
        input.ReadExactly(payload);
        return payload;
    }

    public static void WriteExecutableWithPayload(
        string sourceExecutablePath,
        string destinationExecutablePath,
        ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty || payload.Length > MaximumPayloadLength)
        {
            throw new InvalidDataException("Embedded payload is empty or exceeds the 64 MiB limit.");
        }
        CopyExecutableBase(sourceExecutablePath, destinationExecutablePath, payload);
    }

    public static void WriteExecutableWithoutPayload(
        string sourceExecutablePath,
        string destinationExecutablePath)
    {
        CopyExecutableBase(sourceExecutablePath, destinationExecutablePath, ReadOnlySpan<byte>.Empty);
    }

    private static void CopyExecutableBase(
        string sourceExecutablePath,
        string destinationExecutablePath,
        ReadOnlySpan<byte> payload)
    {
        string? destinationDirectory = Path.GetDirectoryName(Path.GetFullPath(destinationExecutablePath));
        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        using FileStream input = OpenExecutableForRead(sourceExecutablePath);
        PayloadLocation location = Locate(input);
        input.Position = 0;

        using FileStream output = new(
            destinationExecutablePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);

        CopyExactly(input, output, location.BaseLength);

        if (!payload.IsEmpty)
        {
            output.Write(payload);

            Span<byte> footer = stackalloc byte[FooterSize];
            BinaryPrimitives.WriteUInt64LittleEndian(footer[..sizeof(ulong)], (ulong)payload.Length);
            FooterMagic.CopyTo(footer[sizeof(ulong)..]);
            output.Write(footer);
        }

        output.Flush(flushToDisk: true);
    }

    private static FileStream OpenExecutableForRead(string executablePath)
    {
        return new FileStream(
            executablePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
    }

    private static PayloadLocation NoPayload(long fileLength)
    {
        return new PayloadLocation(
            HasPayload: false,
            BaseLength: fileLength,
            PayloadOffset: fileLength,
            PayloadLength: 0,
            FooterOffset: fileLength);
    }

    private static void CopyExactly(Stream input, Stream output, long byteCount)
    {
        byte[] buffer = new byte[1024 * 128];
        long remaining = byteCount;

        while (remaining > 0)
        {
            int readLength = (int)Math.Min(buffer.Length, remaining);
            int read = input.Read(buffer, 0, readLength);
            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected end of executable while copying.");
            }

            output.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = stream.Read(buffer[offset..]);
            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected end of executable while reading footer.");
            }

            offset += read;
        }
    }
}

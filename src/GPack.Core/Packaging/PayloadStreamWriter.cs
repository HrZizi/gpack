using System.Buffers.Binary;
using System.Text;
using GPack.Core.Files;
using GPack.Core.Manifest;

namespace GPack.Core.Packaging;

public static class PayloadConstants
{
    public static readonly byte[] FileEntryMagic = new byte[] { 0x47, 0x50, 0x4B, 0x46 }; // "GPKF"
    public static readonly byte[] EndMagic = new byte[] { 0x47, 0x50, 0x4B, 0x45 };       // "GPKE"
}

public class PayloadStreamWriter
{
    private readonly Stream _stream;

    public PayloadStreamWriter(Stream stream)
    {
        _stream = stream;
    }

    public async Task WriteFileEntryAsync(
        string relativePath,
        string sourceFilePath,
        long fileSize,
        DateTime lastModifiedUtc,
        FileAttributes attributes,
        CancellationToken cancellationToken = default)
    {
        var normalizedPath = PathSecurity.NormalizeRelativePath(relativePath);
        var pathBytes = Encoding.UTF8.GetBytes(normalizedPath);

        // Header:
        // [4 bytes Magic "GPKF"]
        // [4 bytes path length]
        // [N bytes path UTF-8]
        // [8 bytes file length]
        // [8 bytes lastModified UTC ticks]
        // [4 bytes attributes]
        await _stream.WriteAsync(PayloadConstants.FileEntryMagic, cancellationToken);

        byte[] headerBuffer = new byte[4 + pathBytes.Length + 8 + 8 + 4];
        BinaryPrimitives.WriteInt32LittleEndian(headerBuffer.AsSpan(0, 4), pathBytes.Length);
        pathBytes.CopyTo(headerBuffer.AsSpan(4, pathBytes.Length));

        int offset = 4 + pathBytes.Length;
        BinaryPrimitives.WriteInt64LittleEndian(headerBuffer.AsSpan(offset, 8), fileSize);
        offset += 8;
        BinaryPrimitives.WriteInt64LittleEndian(headerBuffer.AsSpan(offset, 8), lastModifiedUtc.Ticks);
        offset += 8;
        BinaryPrimitives.WriteInt32LittleEndian(headerBuffer.AsSpan(offset, 4), (int)attributes);

        await _stream.WriteAsync(headerBuffer, cancellationToken);

        // Write file payload content
        if (fileSize > 0)
        {
            using var fileStream = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            byte[] copyBuffer = new byte[81920];
            long bytesRemaining = fileSize;

            while (bytesRemaining > 0)
            {
                int toRead = (int)Math.Min(bytesRemaining, copyBuffer.Length);
                int read = await fileStream.ReadAsync(copyBuffer.AsMemory(0, toRead), cancellationToken);
                if (read == 0) break;

                await _stream.WriteAsync(copyBuffer.AsMemory(0, read), cancellationToken);
                bytesRemaining -= read;
            }
        }
    }

    public async Task WriteEndAsync(CancellationToken cancellationToken = default)
    {
        await _stream.WriteAsync(PayloadConstants.EndMagic, cancellationToken);
        await _stream.FlushAsync(cancellationToken);
    }
}

public class PayloadEntryHeader
{
    public string RelativePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public FileAttributes Attributes { get; set; }
}

public class PayloadStreamReader
{
    private readonly Stream _stream;

    public PayloadStreamReader(Stream stream)
    {
        _stream = stream;
    }

    public async Task<PayloadEntryHeader?> ReadNextEntryHeaderAsync(CancellationToken cancellationToken = default)
    {
        byte[] magic = new byte[4];
        int magicRead = await ReadExactBytesAsync(_stream, magic, cancellationToken);
        if (magicRead == 0) return null; // EOF

        if (magic.SequenceEqual(PayloadConstants.EndMagic))
        {
            return null; // Reached end of payload
        }

        if (!magic.SequenceEqual(PayloadConstants.FileEntryMagic))
        {
            throw new InvalidDataException("Corrupted payload stream: invalid entry magic header.");
        }

        // Read path length (4 bytes)
        byte[] pathLenBuf = new byte[4];
        await ReadExactBytesOrThrowAsync(_stream, pathLenBuf, cancellationToken);
        int pathLength = BinaryPrimitives.ReadInt32LittleEndian(pathLenBuf);
        if (pathLength <= 0 || pathLength > 32768)
        {
            throw new InvalidDataException($"Corrupted payload stream: invalid path length {pathLength}.");
        }

        // Read path
        byte[] pathBytes = new byte[pathLength];
        await ReadExactBytesOrThrowAsync(_stream, pathBytes, cancellationToken);
        string relativePath = Encoding.UTF8.GetString(pathBytes);

        // Read size (8 bytes), modified ticks (8 bytes), attributes (4 bytes)
        byte[] metaBuf = new byte[8 + 8 + 4];
        await ReadExactBytesOrThrowAsync(_stream, metaBuf, cancellationToken);

        long fileSize = BinaryPrimitives.ReadInt64LittleEndian(metaBuf.AsSpan(0, 8));
        long ticks = BinaryPrimitives.ReadInt64LittleEndian(metaBuf.AsSpan(8, 8));
        int attr = BinaryPrimitives.ReadInt32LittleEndian(metaBuf.AsSpan(16, 4));

        return new PayloadEntryHeader
        {
            RelativePath = PathSecurity.NormalizeRelativePath(relativePath),
            FileSize = fileSize,
            LastModifiedUtc = new DateTime(ticks, DateTimeKind.Utc),
            Attributes = (FileAttributes)attr
        };
    }

    public async Task ExtractFileContentAsync(
        Stream destinationStream,
        long fileSize,
        IProgress<long>? bytesExtractedProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (fileSize <= 0) return;

        byte[] buffer = new byte[81920];
        long remaining = fileSize;

        while (remaining > 0)
        {
            int toRead = (int)Math.Min(remaining, buffer.Length);
            int read = await _stream.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException($"Unexpected end of stream while extracting file payload ({remaining} bytes remaining).");
            }

            await destinationStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            remaining -= read;
            bytesExtractedProgress?.Report(read);
        }
    }

    private static async Task<int> ReadExactBytesAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken);
            if (read == 0) break;
            total += read;
        }
        return total;
    }

    private static async Task ReadExactBytesOrThrowAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int read = await ReadExactBytesAsync(stream, buffer, cancellationToken);
        if (read < buffer.Length)
        {
            throw new EndOfStreamException("Unexpected end of stream while reading payload metadata.");
        }
    }
}

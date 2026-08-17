using System.Security.Cryptography;
using GPack.Core.Manifest;

namespace GPack.Core.Packaging;

public class ChunkedOutputStream : Stream
{
    private readonly string _chunkDirectory;
    private readonly string _chunkFileNamePrefix;
    private readonly string _chunkFileNameExtension;
    private readonly long _maxChunkSize;
    private readonly List<PackageChunk> _chunks = new();

    private int _currentChunkIndex = 0;
    private FileStream? _currentFileStream;
    private IncrementalHash? _currentChunkHasher;
    private long _currentChunkBytesWritten = 0;
    private long _totalBytesWritten = 0;
    private bool _isDisposed = false;

    public IReadOnlyList<PackageChunk> Chunks
    {
        get
        {
            if (_currentFileStream != null)
            {
                CloseCurrentChunk();
            }
            return _chunks;
        }
    }

    public long TotalBytesWritten => _totalBytesWritten;

    public ChunkedOutputStream(
        string chunkDirectory,
        long maxChunkSize,
        string prefix = "payload-",
        string extension = ".gpk")
    {
        _chunkDirectory = chunkDirectory;
        _maxChunkSize = maxChunkSize > 0 ? maxChunkSize : 4L * 1024 * 1024 * 1024;
        _chunkFileNamePrefix = prefix;
        _chunkFileNameExtension = extension;

        Directory.CreateDirectory(_chunkDirectory);
        OpenNextChunk();
    }

    private void OpenNextChunk()
    {
        CloseCurrentChunk();

        string chunkFileName = $"{_chunkFileNamePrefix}{_currentChunkIndex:D3}{_chunkFileNameExtension}";
        string fullPath = Path.Combine(_chunkDirectory, chunkFileName);

        _currentFileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        _currentChunkHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        _currentChunkBytesWritten = 0;
    }

    private void CloseCurrentChunk()
    {
        if (_currentFileStream != null && _currentChunkHasher != null)
        {
            _currentFileStream.Flush();
            _currentFileStream.Dispose();
            _currentFileStream = null;

            byte[] hashBytes = _currentChunkHasher.GetHashAndReset();
            _currentChunkHasher.Dispose();
            _currentChunkHasher = null;

            string relativeChunkPath = Path.Combine("data", $"{_chunkFileNamePrefix}{_currentChunkIndex:D3}{_chunkFileNameExtension}").Replace('\\', '/');
            _chunks.Add(new PackageChunk
            {
                Index = _currentChunkIndex,
                File = relativeChunkPath,
                Size = _currentChunkBytesWritten,
                Sha256 = Convert.ToHexStringLower(hashBytes)
            });

            _currentChunkIndex++;
        }
    }

    public IReadOnlyList<PackageChunk> Complete()
    {
        CloseCurrentChunk();
        return _chunks;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        int bytesRemaining = count;
        int currentOffset = offset;

        while (bytesRemaining > 0)
        {
            long spaceInCurrentChunk = _maxChunkSize - _currentChunkBytesWritten;
            if (spaceInCurrentChunk <= 0)
            {
                OpenNextChunk();
                spaceInCurrentChunk = _maxChunkSize;
            }

            int toWrite = (int)Math.Min(bytesRemaining, spaceInCurrentChunk);

            _currentFileStream!.Write(buffer, currentOffset, toWrite);
            _currentChunkHasher!.AppendData(buffer, currentOffset, toWrite);

            _currentChunkBytesWritten += toWrite;
            _totalBytesWritten += toWrite;
            currentOffset += toWrite;
            bytesRemaining -= toWrite;
        }
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        int bytesRemaining = count;
        int currentOffset = offset;

        while (bytesRemaining > 0)
        {
            long spaceInCurrentChunk = _maxChunkSize - _currentChunkBytesWritten;
            if (spaceInCurrentChunk <= 0)
            {
                OpenNextChunk();
                spaceInCurrentChunk = _maxChunkSize;
            }

            int toWrite = (int)Math.Min(bytesRemaining, spaceInCurrentChunk);

            await _currentFileStream!.WriteAsync(buffer.AsMemory(currentOffset, toWrite), cancellationToken);
            _currentChunkHasher!.AppendData(buffer, currentOffset, toWrite);

            _currentChunkBytesWritten += toWrite;
            _totalBytesWritten += toWrite;
            currentOffset += toWrite;
            bytesRemaining -= toWrite;
        }
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int bytesRemaining = buffer.Length;
        int currentOffset = 0;

        while (bytesRemaining > 0)
        {
            long spaceInCurrentChunk = _maxChunkSize - _currentChunkBytesWritten;
            if (spaceInCurrentChunk <= 0)
            {
                OpenNextChunk();
                spaceInCurrentChunk = _maxChunkSize;
            }

            int toWrite = (int)Math.Min(bytesRemaining, spaceInCurrentChunk);

            var slice = buffer.Slice(currentOffset, toWrite);
            await _currentFileStream!.WriteAsync(slice, cancellationToken);
            _currentChunkHasher!.AppendData(slice.Span);

            _currentChunkBytesWritten += toWrite;
            _totalBytesWritten += toWrite;
            currentOffset += toWrite;
            bytesRemaining -= toWrite;
        }
    }

    public override void Flush()
    {
        _currentFileStream?.Flush();
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return _currentFileStream?.FlushAsync(cancellationToken) ?? Task.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            if (disposing)
            {
                CloseCurrentChunk();
            }
            _isDisposed = true;
        }
        base.Dispose(disposing);
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _totalBytesWritten;
    public override long Position
    {
        get => _totalBytesWritten;
        set => throw new NotSupportedException();
    }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

using System.Security.Cryptography;
using GPack.Core.Manifest;

namespace GPack.Core.Packaging;

public class ChunkedInputStream : Stream
{
    private readonly IReadOnlyList<string> _chunkFullPaths;
    private readonly IReadOnlyList<PackageChunk>? _expectedChunks;
    private readonly long _totalLength;

    private int _currentChunkIndex = 0;
    private FileStream? _currentFileStream;
    private IncrementalHash? _currentChunkHasher;
    private long _currentChunkBytesRead = 0;
    private long _totalBytesRead = 0;
    private bool _isDisposed = false;

    public ChunkedInputStream(
        string baseDirectory,
        IReadOnlyList<PackageChunk> chunks,
        bool verifyHashesOnRead = true)
    {
        _expectedChunks = chunks;
        var fullPaths = new List<string>();
        long total = 0;

        foreach (var chunk in chunks.OrderBy(c => c.Index))
        {
            var chunkPath = Path.Combine(baseDirectory, chunk.File.Replace('/', '\\'));
            if (!File.Exists(chunkPath))
            {
                throw new FileNotFoundException($"Package payload chunk file not found: {chunkPath}", chunkPath);
            }
            fullPaths.Add(chunkPath);
            total += new FileInfo(chunkPath).Length;
        }

        _chunkFullPaths = fullPaths;
        _totalLength = total;

        if (_chunkFullPaths.Count > 0)
        {
            OpenChunk(0);
        }
    }

    public ChunkedInputStream(IEnumerable<string> chunkFiles)
    {
        _chunkFullPaths = chunkFiles.ToList();
        _expectedChunks = null;
        long total = 0;

        foreach (var path in _chunkFullPaths)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Package payload chunk file not found: {path}", path);
            }
            total += new FileInfo(path).Length;
        }

        _totalLength = total;
        if (_chunkFullPaths.Count > 0)
        {
            OpenChunk(0);
        }
    }

    private void OpenChunk(int index)
    {
        CloseCurrentChunk();

        if (index < _chunkFullPaths.Count)
        {
            _currentChunkIndex = index;
            string path = _chunkFullPaths[index];
            _currentFileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            _currentChunkHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            _currentChunkBytesRead = 0;
        }
    }

    private void CloseCurrentChunk()
    {
        if (_currentFileStream != null && _currentChunkHasher != null)
        {
            _currentFileStream.Dispose();
            _currentFileStream = null;

            if (_expectedChunks != null && _currentChunkIndex < _expectedChunks.Count)
            {
                byte[] hashBytes = _currentChunkHasher.GetHashAndReset();
                string computedHash = Convert.ToHexStringLower(hashBytes);
                var expected = _expectedChunks[_currentChunkIndex];

                if (!string.IsNullOrEmpty(expected.Sha256) &&
                    !string.Equals(computedHash, expected.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    _currentChunkHasher.Dispose();
                    _currentChunkHasher = null;
                    throw new InvalidDataException(
                        $"Chunk integrity verification failed for '{expected.File}'. Expected: {expected.Sha256}, Actual: {computedHash}");
                }
            }

            _currentChunkHasher.Dispose();
            _currentChunkHasher = null;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_currentFileStream == null) return 0;

        int totalRead = 0;
        while (count > 0 && _currentFileStream != null)
        {
            int bytesRead = _currentFileStream.Read(buffer, offset, count);
            if (bytesRead == 0)
            {
                // Move to next chunk
                OpenChunk(_currentChunkIndex + 1);
            }
            else
            {
                _currentChunkHasher?.AppendData(buffer, offset, bytesRead);
                _currentChunkBytesRead += bytesRead;
                _totalBytesRead += bytesRead;
                offset += bytesRead;
                count -= bytesRead;
                totalRead += bytesRead;
            }
        }

        return totalRead;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_currentFileStream == null) return 0;

        int totalRead = 0;
        while (count > 0 && _currentFileStream != null)
        {
            int bytesRead = await _currentFileStream.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
            if (bytesRead == 0)
            {
                OpenChunk(_currentChunkIndex + 1);
            }
            else
            {
                _currentChunkHasher?.AppendData(buffer, offset, bytesRead);
                _currentChunkBytesRead += bytesRead;
                _totalBytesRead += bytesRead;
                offset += bytesRead;
                count -= bytesRead;
                totalRead += bytesRead;
            }
        }

        return totalRead;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_currentFileStream == null) return 0;

        int totalRead = 0;
        while (buffer.Length > 0 && _currentFileStream != null)
        {
            int bytesRead = await _currentFileStream.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                OpenChunk(_currentChunkIndex + 1);
            }
            else
            {
                _currentChunkHasher?.AppendData(buffer.Span.Slice(0, bytesRead));
                _currentChunkBytesRead += bytesRead;
                _totalBytesRead += bytesRead;
                buffer = buffer.Slice(bytesRead);
                totalRead += bytesRead;
            }
        }

        return totalRead;
    }

    public override void Flush() { }

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

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _totalLength;
    public override long Position
    {
        get => _totalBytesRead;
        set => throw new NotSupportedException();
    }
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

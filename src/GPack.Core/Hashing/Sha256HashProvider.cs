using System.Security.Cryptography;

namespace GPack.Core.Hashing;

public interface IHashProvider
{
    string AlgorithmName { get; }
    Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken = default);
    Task<string> ComputeStreamHashAsync(Stream stream, CancellationToken cancellationToken = default);
    string ComputeBytesHash(byte[] bytes);
}

public class Sha256HashProvider : IHashProvider
{
    public string AlgorithmName => "SHA-256";

    public async Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken = default)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);
        return await ComputeStreamHashAsync(stream, cancellationToken);
    }

    public async Task<string> ComputeStreamHashAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hashBytes);
    }

    public string ComputeBytesHash(byte[] bytes)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(bytes);
        return Convert.ToHexStringLower(hashBytes);
    }
}

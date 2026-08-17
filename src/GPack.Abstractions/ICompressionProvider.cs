namespace GPack.Abstractions;

public interface ICompressionProvider
{
    string AlgorithmName { get; }
    Stream CreateCompressionStream(Stream outputStream, int level = 0, bool leaveOpen = false);
    Stream CreateDecompressionStream(Stream inputStream, bool leaveOpen = false);
}

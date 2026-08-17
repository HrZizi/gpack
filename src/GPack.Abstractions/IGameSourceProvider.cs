namespace GPack.Abstractions;

public interface IGameSourceProvider
{
    string Name { get; }
    Task<IReadOnlyList<string>> DiscoverFilesAsync(string sourcePath, CancellationToken cancellationToken = default);
}

public interface IMetadataProvider
{
    string Name { get; }
}

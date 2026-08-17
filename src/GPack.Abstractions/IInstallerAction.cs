namespace GPack.Abstractions;

public interface IInstallerAction
{
    string ActionType { get; }
    Task ExecuteAsync(IInstallContext context, CancellationToken cancellationToken = default);
}

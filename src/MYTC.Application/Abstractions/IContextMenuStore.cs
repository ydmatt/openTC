using MYTC.Domain.Configuration;

namespace MYTC.Application.Abstractions;

public interface IContextMenuStore
{
    Task<ContextMenuConfiguration> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        ContextMenuConfiguration configuration,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListSchemeNamesAsync(
        CancellationToken cancellationToken = default);

    Task<ContextMenuConfiguration?> LoadSchemeAsync(
        string name,
        CancellationToken cancellationToken = default);

    Task SaveSchemeAsync(
        string name,
        ContextMenuConfiguration configuration,
        CancellationToken cancellationToken = default);
}

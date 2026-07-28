using MYTC.Domain.Configuration;

namespace MYTC.Application.Abstractions;

public interface ITabContextMenuStore
{
    Task<TabContextMenuConfiguration> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        TabContextMenuConfiguration configuration,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListSchemeNamesAsync(
        CancellationToken cancellationToken = default);

    Task<TabContextMenuConfiguration?> LoadSchemeAsync(
        string name,
        CancellationToken cancellationToken = default);

    Task SaveSchemeAsync(
        string name,
        TabContextMenuConfiguration configuration,
        CancellationToken cancellationToken = default);
}

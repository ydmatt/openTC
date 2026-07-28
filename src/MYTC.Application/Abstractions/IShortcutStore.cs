using MYTC.Domain.Configuration;

namespace MYTC.Application.Abstractions;

public interface IShortcutStore
{
    Task<ShortcutConfiguration> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        ShortcutConfiguration configuration,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListSchemeNamesAsync(
        CancellationToken cancellationToken = default);

    Task<ShortcutConfiguration?> LoadSchemeAsync(
        string name,
        CancellationToken cancellationToken = default);

    Task SaveSchemeAsync(
        string name,
        ShortcutConfiguration configuration,
        CancellationToken cancellationToken = default);
}

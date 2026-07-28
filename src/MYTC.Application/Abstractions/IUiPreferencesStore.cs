using MYTC.Domain.Configuration;

namespace MYTC.Application.Abstractions;

public interface IUiPreferencesStore
{
    Task<UiPreferences> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        UiPreferences preferences,
        CancellationToken cancellationToken = default);
}

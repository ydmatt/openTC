using System.Text.Json;
using MYTC.Application.Abstractions;
using MYTC.Domain.Configuration;

namespace MYTC.Infrastructure.Configuration;

public sealed class JsonUiPreferencesStore(string dataRoot) : IUiPreferencesStore
{
    private readonly string _path = Path.Combine(
        Path.GetFullPath(dataRoot),
        "ui-preferences.json");
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<UiPreferences> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return UiPreferences.CreateDefault();
        }

        try
        {
            await using var stream = File.OpenRead(_path);
            var preferences = await JsonSerializer.DeserializeAsync<UiPreferences>(
                stream,
                _options,
                cancellationToken);
            if (preferences is null ||
                preferences.SchemaVersion > UiPreferences.CurrentSchemaVersion)
            {
                return UiPreferences.CreateDefault();
            }

            return preferences.SchemaVersion < 2
                ? preferences with
                {
                    SchemaVersion = UiPreferences.CurrentSchemaVersion,
                    ConfirmRecycleDelete = true,
                    StartWithWindows = false,
                    IsWorkspaceToolbarVisible = true,
                    IsSettingsToolbarVisible = true,
                    LastWorkspaceName = null,
                    HasConfirmedWinRarPath = false,
                    WinRarExecutablePath = null,
                }
                : preferences.SchemaVersion < 3
                    ? preferences with
                    {
                        SchemaVersion = UiPreferences.CurrentSchemaVersion,
                        IsWorkspaceToolbarVisible = true,
                        IsSettingsToolbarVisible = true,
                        LastWorkspaceName = null,
                        HasConfirmedWinRarPath = false,
                        WinRarExecutablePath = null,
                    }
                : preferences.SchemaVersion < 4
                    ? preferences with
                    {
                        SchemaVersion = UiPreferences.CurrentSchemaVersion,
                        HasConfirmedWinRarPath = false,
                        WinRarExecutablePath = null,
                    }
                : preferences with
                {
                    SchemaVersion = UiPreferences.CurrentSchemaVersion,
                };
        }
        catch (JsonException)
        {
            return UiPreferences.CreateDefault();
        }
    }

    public async Task SaveAsync(
        UiPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("无法确定界面设置目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    preferences with
                    {
                        SchemaVersion = UiPreferences.CurrentSchemaVersion,
                    },
                    _options,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}

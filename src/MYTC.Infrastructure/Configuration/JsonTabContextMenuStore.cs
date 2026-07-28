using System.Text.Json;
using System.Text.Json.Serialization;
using MYTC.Application.Abstractions;
using MYTC.Application.Configuration;
using MYTC.Domain.Configuration;

namespace MYTC.Infrastructure.Configuration;

public sealed class JsonTabContextMenuStore : ITabContextMenuStore
{
    private readonly string _path;
    private readonly string _schemeRoot;
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public JsonTabContextMenuStore(string dataRoot)
    {
        var resolvedRoot = Path.GetFullPath(dataRoot);
        _path = Path.Combine(resolvedRoot, "tab-context-menu.json");
        _schemeRoot = Path.Combine(
            resolvedRoot,
            "tab-context-menu-schemes");
    }

    public async Task<TabContextMenuConfiguration> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        return await LoadFileAsync(_path, cancellationToken)
            ?? TabContextMenuDefaults.Create();
    }

    public Task SaveAsync(
        TabContextMenuConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        return SaveFileAsync(_path, configuration, cancellationToken);
    }

    public Task<IReadOnlyList<string>> ListSchemeNamesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(_schemeRoot))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        var names = Directory
            .EnumerateFiles(_schemeRoot, "*.json", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .Cast<string>()
            .ToArray();
        return Task.FromResult<IReadOnlyList<string>>(names);
    }

    public Task<TabContextMenuConfiguration?> LoadSchemeAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        return LoadFileAsync(GetSchemePath(name), cancellationToken);
    }

    public Task SaveSchemeAsync(
        string name,
        TabContextMenuConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        return SaveFileAsync(
            GetSchemePath(name),
            configuration,
            cancellationToken);
    }

    private async Task<TabContextMenuConfiguration?> LoadFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var configuration =
                await JsonSerializer.DeserializeAsync<TabContextMenuConfiguration>(
                    stream,
                    _options,
                    cancellationToken);
            return configuration is null ||
                configuration.SchemaVersion >
                TabContextMenuConfiguration.CurrentSchemaVersion
                ? null
                : configuration with
                {
                    SchemaVersion =
                        TabContextMenuConfiguration.CurrentSchemaVersion,
                };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task SaveFileAsync(
        string path,
        TabContextMenuConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "无法确定标签右键菜单配置目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                configuration with
                {
                    SchemaVersion =
                        TabContextMenuConfiguration.CurrentSchemaVersion,
                },
                _options,
                cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }

    private string GetSchemePath(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name
            .Trim()
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray());
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            throw new ArgumentException(
                "方案名称不包含有效字符。",
                nameof(name));
        }

        return Path.Combine(_schemeRoot, sanitized + ".json");
    }
}

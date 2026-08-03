using System.Text.Json;
using System.Text.Json.Serialization;
using MYTC.Application.Abstractions;
using MYTC.Application.Configuration;
using MYTC.Domain.Configuration;

namespace MYTC.Infrastructure.Configuration;

public sealed class JsonContextMenuStore : IContextMenuStore
{
    private readonly string _path;
    private readonly string _schemeRoot;
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public JsonContextMenuStore(string dataRoot)
    {
        var resolvedRoot = Path.GetFullPath(dataRoot);
        _path = Path.Combine(resolvedRoot, "context-menu.json");
        _schemeRoot = Path.Combine(resolvedRoot, "context-menu-schemes");
    }

    public async Task<ContextMenuConfiguration> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        return await LoadFileAsync(_path, cancellationToken)
            ?? ContextMenuDefaults.Create();
    }

    public Task SaveAsync(
        ContextMenuConfiguration configuration,
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

    public Task<ContextMenuConfiguration?> LoadSchemeAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        return LoadFileAsync(GetSchemePath(name), cancellationToken);
    }

    public Task SaveSchemeAsync(
        string name,
        ContextMenuConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        return SaveFileAsync(
            GetSchemePath(name),
            configuration,
            cancellationToken);
    }

    private async Task<ContextMenuConfiguration?> LoadFileAsync(
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
                await JsonSerializer.DeserializeAsync<ContextMenuConfiguration>(
                    stream,
                    _options,
                    cancellationToken);
            if (configuration is null ||
                configuration.SchemaVersion >
                ContextMenuConfiguration.CurrentSchemaVersion)
            {
                return null;
            }

            return configuration.SchemaVersion <
                ContextMenuConfiguration.CurrentSchemaVersion
                ? Migrate(configuration)
                : configuration;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task SaveFileAsync(
        string path,
        ContextMenuConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("无法确定右键菜单配置目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                configuration with
                {
                    SchemaVersion =
                        ContextMenuConfiguration.CurrentSchemaVersion,
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
            throw new ArgumentException("方案名称不包含有效字符。", nameof(name));
        }

        return Path.Combine(_schemeRoot, sanitized + ".json");
    }

    private static ContextMenuConfiguration Migrate(
        ContextMenuConfiguration configuration)
    {
        var items = configuration.Items.ToList();
        if (!items.Any(item => item.Action == ContextMenuAction.CopyFullPath))
        {
            var copyPath = ContextMenuDefaults.Create().Items.Single(item =>
                item.Action == ContextMenuAction.CopyFullPath);
            var pasteIndex = items.FindIndex(item =>
                item.Action == ContextMenuAction.PasteFromClipboard);
            items.Insert(
                pasteIndex >= 0 ? pasteIndex + 1 : items.Count,
                copyPath);
        }

        if (!items.Any(item =>
                item.Action == ContextMenuAction.CreateDirectory))
        {
            var createDirectory = ContextMenuDefaults.Create().Items.Single(
                item => item.Action == ContextMenuAction.CreateDirectory);
            var openIndex = items.FindIndex(item =>
                item.Action == ContextMenuAction.Open);
            items.Insert(
                openIndex >= 0 ? openIndex + 1 : 0,
                createDirectory);
        }

        if (!items.Any(item => item.Action == ContextMenuAction.OpenWith))
        {
            var openWith = ContextMenuDefaults.Create().Items.Single(item =>
                item.Action == ContextMenuAction.OpenWith);
            var openIndex = items.FindIndex(item =>
                item.Action == ContextMenuAction.Open);
            items.Insert(
                openIndex >= 0 ? openIndex + 1 : 0,
                openWith);
        }

        var defaults = ContextMenuDefaults.Create().Items;
        var newSubmenu = defaults.Single(item =>
            item.Kind == ContextMenuItemKind.Submenu &&
            StringComparer.Ordinal.Equals(item.Id, "new-submenu"));
        var submenuIndex = items.FindIndex(item =>
            StringComparer.Ordinal.Equals(item.Id, newSubmenu.Id));
        if (submenuIndex < 0)
        {
            var createIndex = items.FindIndex(item =>
                item.Action == ContextMenuAction.CreateDirectory);
            items.Insert(createIndex >= 0 ? createIndex : 0, newSubmenu);
        }

        var createDirectoryIndex = items.FindIndex(item =>
            item.Action == ContextMenuAction.CreateDirectory);
        if (createDirectoryIndex >= 0)
        {
            var createDirectory = items[createDirectoryIndex];
            var label = createDirectory.Label;
            if (StringComparer.Ordinal.Equals(label, "新建文件夹（&W）"))
            {
                label = "文件夹（&F）";
            }

            items[createDirectoryIndex] = createDirectory with
            {
                Label = label,
                ParentId = newSubmenu.Id,
            };
        }

        if (!items.Any(item =>
                item.Action == ContextMenuAction.CreateTextDocument))
        {
            var createTextDocument = defaults.Single(item =>
                item.Action == ContextMenuAction.CreateTextDocument);
            createDirectoryIndex = items.FindIndex(item =>
                item.Action == ContextMenuAction.CreateDirectory);
            items.Insert(
                createDirectoryIndex >= 0
                    ? createDirectoryIndex + 1
                    : submenuIndex >= 0 ? submenuIndex + 1 : items.Count,
                createTextDocument);
        }

        if (!items.Any(item => item.Action == ContextMenuAction.UndoDelete))
        {
            var undoDelete = defaults.Single(item =>
                item.Action == ContextMenuAction.UndoDelete);
            var recycleIndex = items.FindIndex(item =>
                item.Action == ContextMenuAction.RecycleDelete);
            items.Insert(
                recycleIndex >= 0 ? recycleIndex + 1 : items.Count,
                undoDelete);
        }

        if (!items.Any(item => item.Action == ContextMenuAction.Properties))
        {
            var properties = defaults.Single(item =>
                item.Action == ContextMenuAction.Properties);
            var permanentDeleteIndex = items.FindIndex(item =>
                item.Action == ContextMenuAction.PermanentDelete);
            items.Insert(
                permanentDeleteIndex >= 0
                    ? permanentDeleteIndex + 1
                    : items.Count,
                properties);
        }

        if (!items.Any(item => item.Action == ContextMenuAction.Refresh))
        {
            var refresh = defaults.Single(item =>
                item.Action == ContextMenuAction.Refresh);
            var propertiesIndex = items.FindIndex(item =>
                item.Action == ContextMenuAction.Properties);
            items.Insert(
                propertiesIndex >= 0 ? propertiesIndex : items.Count,
                refresh);
        }

        if (!items.Any(item =>
                item.Action == ContextMenuAction.ExtractHereWithWinRar))
        {
            var extractHere = defaults.Single(item =>
                item.Action == ContextMenuAction.ExtractHereWithWinRar);
            var openWithIndex = items.FindIndex(item =>
                item.Action == ContextMenuAction.OpenWith);
            items.Insert(
                openWithIndex >= 0 ? openWithIndex + 1 : 0,
                extractHere);
        }

        if (!items.Any(item =>
                item.Action ==
                    ContextMenuAction.ExtractToNamedDirectoryWithWinRar))
        {
            var extractNamed = defaults.Single(item =>
                item.Action ==
                    ContextMenuAction.ExtractToNamedDirectoryWithWinRar);
            var extractHereIndex = items.FindIndex(item =>
                item.Action == ContextMenuAction.ExtractHereWithWinRar);
            items.Insert(
                extractHereIndex >= 0 ? extractHereIndex + 1 : 0,
                extractNamed);
        }

        return new ContextMenuConfiguration(
            ContextMenuConfiguration.CurrentSchemaVersion,
            items);
    }
}

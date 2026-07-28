using System.Text.Json;
using System.Text.Json.Serialization;
using MYTC.Application.Abstractions;
using MYTC.Application.Configuration;
using MYTC.Domain.Configuration;

namespace MYTC.Infrastructure.Configuration;

public sealed class JsonShortcutStore : IShortcutStore
{
    private readonly string _path;
    private readonly string _schemeRoot;
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public JsonShortcutStore(string dataRoot)
    {
        var resolvedRoot = Path.GetFullPath(dataRoot);
        _path = Path.Combine(resolvedRoot, "shortcuts.json");
        _schemeRoot = Path.Combine(resolvedRoot, "shortcut-schemes");
    }

    public async Task<ShortcutConfiguration> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        return await LoadFileAsync(_path, cancellationToken)
            ?? ShortcutDefaults.Create();
    }

    public Task SaveAsync(
        ShortcutConfiguration configuration,
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

    public Task<ShortcutConfiguration?> LoadSchemeAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        return LoadFileAsync(GetSchemePath(name), cancellationToken);
    }

    public Task SaveSchemeAsync(
        string name,
        ShortcutConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        return SaveFileAsync(
            GetSchemePath(name),
            configuration,
            cancellationToken);
    }

    private async Task<ShortcutConfiguration?> LoadFileAsync(
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
            var configuration = await JsonSerializer.DeserializeAsync<ShortcutConfiguration>(
                stream,
                _options,
                cancellationToken);
            if (configuration is null ||
                configuration.SchemaVersion > ShortcutConfiguration.CurrentSchemaVersion)
            {
                return null;
            }

            return configuration.SchemaVersion < ShortcutConfiguration.CurrentSchemaVersion
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
        ShortcutConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("无法确定快捷键配置目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp";

        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                configuration with
                {
                    SchemaVersion = ShortcutConfiguration.CurrentSchemaVersion,
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

    private static ShortcutConfiguration Migrate(ShortcutConfiguration saved)
    {
        var bindings = saved.Bindings.ToList();
        AddIfGestureUnused(bindings, ShortcutAction.RecycleDelete, "Del");
        AddIfGestureUnused(bindings, ShortcutAction.PermanentDelete, "Shift+Del");
        AddIfGestureUnused(bindings, ShortcutAction.FocusAddressBar, "Alt+D");
        AddIfGestureUnused(bindings, ShortcutAction.NavigateUp, "Backspace");
        AddIfGestureUnused(bindings, ShortcutAction.RecycleDelete, "Ctrl+D");
        return new ShortcutConfiguration(
            ShortcutConfiguration.CurrentSchemaVersion,
            bindings);
    }

    private static void AddIfGestureUnused(
        ICollection<ShortcutBinding> bindings,
        ShortcutAction action,
        string gesture)
    {
        var normalized = NormalizeForConflictCheck(gesture);
        if (bindings.Any(binding =>
            {
                var existing = NormalizeForConflictCheck(binding.Gesture);
                return StringComparer.OrdinalIgnoreCase.Equals(existing, normalized) ||
                    existing.StartsWith(normalized + ",", StringComparison.OrdinalIgnoreCase) ||
                    normalized.StartsWith(existing + ",", StringComparison.OrdinalIgnoreCase);
            }))
        {
            return;
        }

        bindings.Add(new ShortcutBinding(action, gesture));
    }

    private static string NormalizeForConflictCheck(string gesture)
    {
        return string.Join(
            ",",
            gesture.Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries)
                .Select(chord => chord.Replace(" ", string.Empty)));
    }
}

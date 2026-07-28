using System.Text.Json;
using System.Text.Json.Serialization;
using MYTC.Application.Abstractions;
using MYTC.Domain.Workspaces;

namespace MYTC.Infrastructure.Configuration;

public sealed class JsonWorkspaceStore : IWorkspaceStore
{
    private readonly string _dataRoot;
    private readonly string _workspaceRoot;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public JsonWorkspaceStore(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _dataRoot = Path.GetFullPath(dataRoot);
        _workspaceRoot = Path.Combine(_dataRoot, "workspaces");
    }

    public Task<WorkspaceSnapshot?> LoadSessionAsync(
        CancellationToken cancellationToken = default)
    {
        return LoadAsync(
            Path.Combine(_dataRoot, "session.json"),
            cancellationToken);
    }

    public Task SaveSessionAsync(
        WorkspaceSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        return SaveAtomicAsync(
            Path.Combine(_dataRoot, "session.json"),
            snapshot,
            cancellationToken);
    }

    public Task<IReadOnlyList<string>> ListWorkspaceNamesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(_workspaceRoot))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        var names = Directory
            .EnumerateFiles(_workspaceRoot, "*.json", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .Cast<string>()
            .ToArray();

        return Task.FromResult<IReadOnlyList<string>>(names);
    }

    public Task<WorkspaceSnapshot?> LoadWorkspaceAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        return LoadAsync(GetWorkspacePath(name), cancellationToken);
    }

    public Task SaveWorkspaceAsync(
        string name,
        WorkspaceSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        return SaveAtomicAsync(
            GetWorkspacePath(name),
            snapshot with { Name = name },
            cancellationToken);
    }

    private async Task<WorkspaceSnapshot?> LoadAsync(
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
            var snapshot = await JsonSerializer.DeserializeAsync<WorkspaceSnapshot>(
                stream,
                _jsonOptions,
                cancellationToken);

            if (snapshot is null)
            {
                return null;
            }

            if (snapshot.SchemaVersion > WorkspaceSnapshot.CurrentSchemaVersion)
            {
                return null;
            }

            return snapshot;
        }
        catch (JsonException)
        {
            var backupPath = path + ".bak";
            if (!File.Exists(backupPath))
            {
                return null;
            }

            try
            {
                await using var backupStream = File.OpenRead(backupPath);
                return await JsonSerializer.DeserializeAsync<WorkspaceSnapshot>(
                    backupStream,
                    _jsonOptions,
                    cancellationToken);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    private async Task SaveAtomicAsync(
        string path,
        WorkspaceSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"Unable to resolve directory for {path}.");
        Directory.CreateDirectory(directory);

        var temporaryPath = path + ".tmp";
        var backupPath = path + ".bak";

        await using (var stream = new FileStream(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            useAsync: true))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                snapshot,
                _jsonOptions,
                cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        await using (var validationStream = File.OpenRead(temporaryPath))
        {
            _ = await JsonSerializer.DeserializeAsync<WorkspaceSnapshot>(
                validationStream,
                _jsonOptions,
                cancellationToken)
                ?? throw new InvalidDataException("Serialized workspace could not be read back.");
        }

        if (File.Exists(path))
        {
            File.Replace(temporaryPath, path, backupPath, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temporaryPath, path);
        }
    }

    private string GetWorkspacePath(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name
            .Trim()
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray());

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            throw new ArgumentException("Workspace name contains no valid filename characters.", nameof(name));
        }

        return Path.Combine(_workspaceRoot, sanitized + ".json");
    }
}

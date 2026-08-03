using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using MYTC.Application.Abstractions;
using MYTC.Domain.Workspaces;

namespace MYTC.Infrastructure.Configuration;

public sealed class JsonWorkspaceStore : IWorkspaceStore
{
    private readonly string _dataRoot;
    private readonly string _workspaceRoot;
    private readonly string _sessionPath;
    private readonly string _legacySessionPath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public JsonWorkspaceStore(
        string dataRoot,
        string? sessionScope = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _dataRoot = Path.GetFullPath(dataRoot);
        _workspaceRoot = Path.Combine(_dataRoot, "workspaces");
        _legacySessionPath = Path.Combine(_dataRoot, "session.json");
        _sessionPath = GetSessionPath(sessionScope);
    }

    public async Task<WorkspaceSnapshot?> LoadSessionAsync(
        CancellationToken cancellationToken = default)
    {
        var scoped = await LoadAsync(_sessionPath, cancellationToken);
        return scoped ?? (_sessionPath == _legacySessionPath
            ? null
            : await LoadAsync(_legacySessionPath, cancellationToken));
    }

    public Task SaveSessionAsync(
        WorkspaceSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        return SaveAtomicAsync(
            _sessionPath,
            snapshot with
            {
                SchemaVersion = WorkspaceSnapshot.CurrentSchemaVersion,
            },
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
            snapshot with
            {
                SchemaVersion = WorkspaceSnapshot.CurrentSchemaVersion,
                Name = name,
            },
            cancellationToken);
    }

    public Task DeleteWorkspaceAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetWorkspacePath(name);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        var backupPath = path + ".bak";
        if (File.Exists(backupPath))
        {
            File.Delete(backupPath);
        }

        return Task.CompletedTask;
    }

    public async Task RenameWorkspaceAsync(
        string currentName,
        string newName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        var currentPath = GetWorkspacePath(currentName);
        var targetPath = GetWorkspacePath(newName);
        var snapshot = await LoadAsync(currentPath, cancellationToken)
            ?? throw new FileNotFoundException(
                $"找不到工作区“{currentName.Trim()}”。",
                currentPath);

        if (!StringComparer.OrdinalIgnoreCase.Equals(currentPath, targetPath) &&
            File.Exists(targetPath))
        {
            throw new IOException($"工作区“{newName.Trim()}”已经存在。");
        }

        var normalizedName = newName.Trim();
        await SaveAtomicAsync(
            targetPath,
            snapshot with { Name = normalizedName },
            cancellationToken);

        if (StringComparer.OrdinalIgnoreCase.Equals(currentPath, targetPath))
        {
            return;
        }

        File.Delete(currentPath);
        var currentBackup = currentPath + ".bak";
        if (File.Exists(currentBackup))
        {
            File.Delete(currentBackup);
        }
    }

    public async Task ExportWorkspaceAsync(
        string name,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var sourcePath = GetWorkspacePath(name);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"找不到工作区“{name}”。", sourcePath);
        }

        var fullDestination = Path.GetFullPath(destinationPath);
        var destinationDirectory = Path.GetDirectoryName(fullDestination)
            ?? throw new IOException("无法确定导出目录。");
        Directory.CreateDirectory(destinationDirectory);
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        await using var destination = new FileStream(
            fullDestination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            useAsync: true);
        await source.CopyToAsync(destination, cancellationToken);
        await destination.FlushAsync(cancellationToken);
    }

    public async Task<string> ImportWorkspaceAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var fullSource = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSource))
        {
            throw new FileNotFoundException("找不到要导入的工作区文件。", fullSource);
        }

        var snapshot = await LoadExternalAsync(fullSource, cancellationToken)
            ?? throw new InvalidDataException("工作区文件无效或版本不受支持。");
        var preferredName = string.IsNullOrWhiteSpace(snapshot.Name)
            ? Path.GetFileNameWithoutExtension(fullSource)
            : snapshot.Name;
        var importedName = GetAvailableWorkspaceName(preferredName);
        await SaveWorkspaceAsync(
            importedName,
            snapshot with { Name = importedName },
            cancellationToken);
        return importedName;
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

            return snapshot with
            {
                SchemaVersion = WorkspaceSnapshot.CurrentSchemaVersion,
            };
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
                var backup = await JsonSerializer.DeserializeAsync<WorkspaceSnapshot>(
                    backupStream,
                    _jsonOptions,
                    cancellationToken);
                return backup is null ||
                    backup.SchemaVersion > WorkspaceSnapshot.CurrentSchemaVersion
                    ? null
                    : backup with
                    {
                        SchemaVersion = WorkspaceSnapshot.CurrentSchemaVersion,
                    };
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    private async Task<WorkspaceSnapshot?> LoadExternalAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var snapshot = await JsonSerializer.DeserializeAsync<WorkspaceSnapshot>(
                stream,
                _jsonOptions,
                cancellationToken);
            return snapshot is null ||
                snapshot.SchemaVersion > WorkspaceSnapshot.CurrentSchemaVersion
                ? null
                : snapshot with
                {
                    SchemaVersion = WorkspaceSnapshot.CurrentSchemaVersion,
                };
        }
        catch (JsonException)
        {
            return null;
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

    private string GetSessionPath(string? sessionScope)
    {
        if (string.IsNullOrWhiteSpace(sessionScope))
        {
            return _legacySessionPath;
        }

        var normalized = sessionScope.Trim();
        var safeName = SanitizeWorkspaceName(normalized);
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(
                normalized.ToUpperInvariant())))[..8];
        return Path.Combine(
            _dataRoot,
            "sessions",
            $"{safeName}-{hash}.json");
    }

    private string GetAvailableWorkspaceName(string preferredName)
    {
        var baseName = SanitizeWorkspaceName(preferredName);
        var candidate = baseName;
        var suffix = 2;
        while (File.Exists(Path.Combine(_workspaceRoot, candidate + ".json")))
        {
            candidate = $"{baseName} ({suffix++})";
        }

        return candidate;
    }

    private static string SanitizeWorkspaceName(string name)
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

        return sanitized;
    }
}

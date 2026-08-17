using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using MYTC.Application.Updates;

namespace MYTC.Infrastructure.Updates;

public sealed class PortableUpdatePackageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<PreparedPortableUpdate> PrepareAsync(
        string archivePath,
        string stagingParent,
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingParent);
        ArgumentNullException.ThrowIfNull(currentVersion);

        var fullArchivePath = Path.GetFullPath(archivePath);
        if (!File.Exists(fullArchivePath))
        {
            throw new FileNotFoundException("找不到所选升级包。", fullArchivePath);
        }

        Directory.CreateDirectory(stagingParent);
        var stagingRoot = Path.Combine(
            Path.GetFullPath(stagingParent),
            $"stage-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingRoot);

        try
        {
            await ExtractSafelyAsync(
                fullArchivePath,
                stagingRoot,
                cancellationToken);
            var packageRoot = FindPackageRoot(stagingRoot);
            var manifestPath = Path.Combine(
                packageRoot,
                PortableUpdateConstants.ManifestFileName);
            var manifest = await ReadManifestAsync(
                manifestPath,
                cancellationToken);
            ValidateManifest(manifest, currentVersion);
            await ValidateExtractedFilesAsync(
                packageRoot,
                manifest,
                cancellationToken);

            return new PreparedPortableUpdate(
                fullArchivePath,
                packageRoot,
                manifest.Version,
                manifest.Files);
        }
        catch
        {
            TryDeleteDirectory(stagingRoot);
            throw;
        }
    }

    public static async Task<PortableUpdateManifest> ReadManifestAsync(
        string manifestPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(manifestPath))
        {
            throw new InvalidDataException(
                $"升级包缺少 {PortableUpdateConstants.ManifestFileName}。");
        }

        await using var stream = new FileStream(
            manifestPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var manifest = await JsonSerializer.DeserializeAsync<PortableUpdateManifest>(
            stream,
            JsonOptions,
            cancellationToken);
        return manifest
            ?? throw new InvalidDataException("升级包清单为空或格式错误。");
    }

    public static void ValidateManifest(
        PortableUpdateManifest manifest,
        Version currentVersion,
        bool requireNewerVersion = true)
    {
        if (manifest.SchemaVersion !=
            PortableUpdateConstants.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"不支持的升级包清单版本：{manifest.SchemaVersion}。");
        }

        if (!StringComparer.Ordinal.Equals(
                manifest.ProductId,
                PortableUpdateConstants.ProductId))
        {
            throw new InvalidDataException("所选 ZIP 不是 openTC 升级包。");
        }

        if (!StringComparer.OrdinalIgnoreCase.Equals(
                manifest.Architecture,
                "win-x64"))
        {
            throw new InvalidDataException(
                $"升级包架构“{manifest.Architecture}”与本程序不匹配。");
        }

        if (!Version.TryParse(manifest.Version, out var packageVersion))
        {
            throw new InvalidDataException(
                $"升级包版本号无效：{manifest.Version}。");
        }

        if (requireNewerVersion && packageVersion <= currentVersion)
        {
            throw new InvalidDataException(
                $"升级包版本 {packageVersion} 不高于当前版本 {currentVersion}。");
        }

        if (manifest.Files.Count == 0)
        {
            throw new InvalidDataException("升级包清单没有文件。");
        }

        var normalizedPaths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            var normalized = NormalizeRelativePath(file.Path);
            if (!normalizedPaths.Add(normalized))
            {
                throw new InvalidDataException(
                    $"升级包清单包含重复路径：{file.Path}。");
            }

            if (IsProtectedDataPath(normalized))
            {
                throw new InvalidDataException(
                    "升级包不得包含 data 目录；用户配置不会参与覆盖。");
            }

            if (file.Length < 0 ||
                file.Sha256.Length != 64 ||
                !file.Sha256.All(Uri.IsHexDigit))
            {
                throw new InvalidDataException(
                    $"升级包文件校验信息无效：{file.Path}。");
            }
        }

        foreach (var required in new[]
                 {
                     PortableUpdateConstants.MainExecutableName,
                     PortableUpdateConstants.MaintenanceExecutableName,
                 })
        {
            if (!normalizedPaths.Contains(required))
            {
                throw new InvalidDataException(
                    $"升级包缺少必要文件：{required}。");
            }
        }
    }

    public static async Task ValidateExtractedFilesAsync(
        string packageRoot,
        PortableUpdateManifest manifest,
        CancellationToken cancellationToken = default)
    {
        var root = EnsureTrailingSeparator(Path.GetFullPath(packageRoot));
        var expectedPaths = new HashSet<string>(
            manifest.Files.Select(file => NormalizeRelativePath(file.Path)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = NormalizeRelativePath(file.Path);
            var fullPath = Path.GetFullPath(Path.Combine(root, relative));
            EnsureInsideRoot(root, fullPath);
            if (!File.Exists(fullPath))
            {
                throw new InvalidDataException(
                    $"升级包缺少清单文件：{relative}。");
            }

            var info = new FileInfo(fullPath);
            if (info.Length != file.Length)
            {
                throw new InvalidDataException(
                    $"升级包文件长度校验失败：{relative}。");
            }

            var hash = await ComputeSha256Async(fullPath, cancellationToken);
            if (!StringComparer.OrdinalIgnoreCase.Equals(hash, file.Sha256))
            {
                throw new InvalidDataException(
                    $"升级包文件哈希校验失败：{relative}。");
            }
        }

        var unexpected = Directory
            .EnumerateFiles(packageRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(packageRoot, path))
            .Where(path => !StringComparer.OrdinalIgnoreCase.Equals(
                path,
                PortableUpdateConstants.ManifestFileName))
            .Where(path => !expectedPaths.Contains(NormalizeRelativePath(path)))
            .Take(1)
            .FirstOrDefault();
        if (unexpected is not null)
        {
            throw new InvalidDataException(
                $"升级包含有未列入清单的文件：{unexpected}。");
        }
    }

    public static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            throw new InvalidDataException($"升级包路径无效：{path}。");
        }

        var normalized = path
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Trim();
        var segments = normalized.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 ||
            segments.Any(segment => segment is "." or ".."))
        {
            throw new InvalidDataException($"升级包路径不安全：{path}。");
        }

        return string.Join(Path.DirectorySeparatorChar, segments);
    }

    private static async Task ExtractSafelyAsync(
        string archivePath,
        string stagingRoot,
        CancellationToken cancellationToken)
    {
        var root = EnsureTrailingSeparator(Path.GetFullPath(stagingRoot));
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count == 0)
        {
            throw new InvalidDataException("升级包为空。");
        }

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedEntry = entry.FullName.Replace(
                Path.AltDirectorySeparatorChar,
                Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalizedEntry) ||
                normalizedEntry.Split(
                    Path.DirectorySeparatorChar,
                    StringSplitOptions.RemoveEmptyEntries)
                    .Any(segment => segment is "." or ".."))
            {
                throw new InvalidDataException(
                    $"升级包包含不安全路径：{entry.FullName}。");
            }

            var destination = Path.GetFullPath(
                Path.Combine(root, normalizedEntry));
            EnsureInsideRoot(root, destination);

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(destination)
                ?? throw new InvalidDataException("升级包路径无效。"));
            await using var source = entry.Open();
            await using var target = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await source.CopyToAsync(target, cancellationToken);
        }
    }

    private static string FindPackageRoot(string stagingRoot)
    {
        var rootManifest = Path.Combine(
            stagingRoot,
            PortableUpdateConstants.ManifestFileName);
        if (File.Exists(rootManifest))
        {
            return stagingRoot;
        }

        var directories = Directory.GetDirectories(stagingRoot);
        var files = Directory.GetFiles(stagingRoot);
        if (files.Length == 0 &&
            directories.Length == 1 &&
            File.Exists(Path.Combine(
                directories[0],
                PortableUpdateConstants.ManifestFileName)))
        {
            return directories[0];
        }

        throw new InvalidDataException(
            $"升级包必须在根目录或唯一的顶层目录中包含 {PortableUpdateConstants.ManifestFileName}。");
    }

    private static bool IsProtectedDataPath(string relativePath)
    {
        return StringComparer.OrdinalIgnoreCase.Equals(relativePath, "data") ||
            relativePath.StartsWith(
                $"data{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureInsideRoot(string root, string fullPath)
    {
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("升级包路径越出了暂存目录。");
        }
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup; the original validation error is more useful.
        }
    }
}

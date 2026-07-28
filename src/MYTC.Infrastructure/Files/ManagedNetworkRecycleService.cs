using System.Security.Cryptography;
using System.Text;
using MYTC.Application.Abstractions;
using MYTC.Domain.Operations;

namespace MYTC.Infrastructure.Files;

/// <summary>
/// Provides reversible deletion for mapped and UNC shares, where the Windows
/// recycle bin is normally unavailable. Items are moved on the same share to a
/// hidden MYTC-owned directory so the operation remains fast and reversible.
/// </summary>
public sealed class ManagedNetworkRecycleService : IManagedRecycleService
{
    private const string RecycleDirectoryName = ".MYTC-RecycleBin";
    private readonly Func<string, bool> _requiresManagedRecycle;
    private readonly Func<string, string> _recycleRootResolver;
    private readonly string _userSegment;

    public ManagedNetworkRecycleService(
        Func<string, bool>? requiresManagedRecycle = null,
        Func<string, string>? recycleRootResolver = null)
    {
        _requiresManagedRecycle =
            requiresManagedRecycle ?? IsNetworkPath;
        _recycleRootResolver =
            recycleRootResolver ?? ResolveRecycleRoot;
        _userSegment = CreateUserSegment();
    }

    public bool RequiresManagedRecycle(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return _requiresManagedRecycle(Path.GetFullPath(path));
    }

    public Task<ManagedRecycleDeleteResult> RecycleAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var entries = new List<ManagedRecycleEntry>();
        var failures = new List<FileOperationFailure>();
        var batchId = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";

        for (var index = 0; index < paths.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var suppliedPath = paths[index];
            try
            {
                var originalPath = Path.GetFullPath(suppliedPath);
                var isFile = File.Exists(originalPath);
                var isDirectory = Directory.Exists(originalPath);
                if (!isFile && !isDirectory)
                {
                    throw new FileNotFoundException(
                        "要删除的项目不存在。",
                        originalPath);
                }

                var recycleRoot = Path.GetFullPath(
                    _recycleRootResolver(originalPath));
                if (IsSameOrDescendant(originalPath, recycleRoot))
                {
                    throw new InvalidOperationException(
                        "不能将 MYTC 回收区本身再次移入回收区。");
                }

                var leafName = Path.GetFileName(
                    originalPath.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(leafName))
                {
                    throw new InvalidOperationException(
                        "不能删除共享盘根目录。");
                }

                var batchRoot = Path.Combine(
                    recycleRoot,
                    _userSegment,
                    batchId);
                Directory.CreateDirectory(batchRoot);
                TryHideRecycleRoot(recycleRoot);

                var storedPath = Path.Combine(
                    batchRoot,
                    $"{index:D4}-{Guid.NewGuid():N}-{leafName}");
                if (isFile)
                {
                    File.Move(originalPath, storedPath);
                }
                else
                {
                    Directory.Move(originalPath, storedPath);
                }

                entries.Add(new ManagedRecycleEntry(
                    originalPath,
                    storedPath,
                    recycleRoot));
            }
            catch (Exception exception)
                when (exception is not OperationCanceledException)
            {
                failures.Add(new FileOperationFailure(
                    suppliedPath,
                    exception.Message));
            }
        }

        return Task.FromResult(new ManagedRecycleDeleteResult(
            entries,
            failures));
    }

    public Task<RecycleBinRestoreResult> RestoreAsync(
        IReadOnlyList<ManagedRecycleEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var restored = new List<string>();
        var failures = new List<FileOperationFailure>();
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var originalPath = Path.GetFullPath(entry.OriginalPath);
                var storedPath = Path.GetFullPath(entry.StoredPath);
                var recycleRoot = Path.GetFullPath(entry.RecycleRoot);
                if (!IsSameOrDescendant(storedPath, recycleRoot))
                {
                    throw new InvalidOperationException(
                        "回收项目不属于已记录的 MYTC 回收区。");
                }

                if (File.Exists(originalPath) ||
                    Directory.Exists(originalPath))
                {
                    throw new IOException(
                        "原位置已存在同名项目，无法还原。");
                }

                var parent = Path.GetDirectoryName(originalPath);
                if (string.IsNullOrWhiteSpace(parent) ||
                    !Directory.Exists(parent))
                {
                    throw new DirectoryNotFoundException(
                        "原目录已经不存在，无法还原。");
                }

                if (File.Exists(storedPath))
                {
                    File.Move(storedPath, originalPath);
                }
                else if (Directory.Exists(storedPath))
                {
                    Directory.Move(storedPath, originalPath);
                }
                else
                {
                    throw new FileNotFoundException(
                        "MYTC 回收区中找不到对应项目。",
                        storedPath);
                }

                restored.Add(originalPath);
                RemoveEmptyRecycleDirectories(
                    Path.GetDirectoryName(storedPath),
                    recycleRoot);
            }
            catch (Exception exception)
                when (exception is not OperationCanceledException)
            {
                failures.Add(new FileOperationFailure(
                    entry.OriginalPath,
                    exception.Message));
            }
        }

        return Task.FromResult(new RecycleBinRestoreResult(
            restored,
            failures));
    }

    private static bool IsNetworkPath(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return true;
        }

        var root = Path.GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        try
        {
            return new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveRecycleRoot(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException(
                "无法确定共享盘根目录。");
        }

        return Path.Combine(root, RecycleDirectoryName);
    }

    private static bool IsSameOrDescendant(
        string path,
        string candidateParent)
    {
        var normalizedPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var normalizedParent = Path.GetFullPath(candidateParent)
            .TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(
            normalizedParent,
            StringComparison.OrdinalIgnoreCase);
    }

    private static void TryHideRecycleRoot(string recycleRoot)
    {
        try
        {
            var directory = new DirectoryInfo(recycleRoot);
            directory.Attributes |=
                FileAttributes.Hidden |
                FileAttributes.System;
        }
        catch
        {
            // Hiding is cosmetic. A share may reject attribute changes even
            // though move and restore operations are available.
        }
    }

    private static void RemoveEmptyRecycleDirectories(
        string? startDirectory,
        string recycleRoot)
    {
        var current = startDirectory;
        var normalizedRoot = Path.GetFullPath(recycleRoot)
            .TrimEnd(Path.DirectorySeparatorChar);
        while (!string.IsNullOrWhiteSpace(current) &&
               IsSameOrDescendant(current, normalizedRoot))
        {
            try
            {
                if (!Directory.Exists(current) ||
                    Directory.EnumerateFileSystemEntries(current).Any())
                {
                    return;
                }

                Directory.Delete(current);
            }
            catch
            {
                return;
            }

            if (StringComparer.OrdinalIgnoreCase.Equals(
                    current.TrimEnd(Path.DirectorySeparatorChar),
                    normalizedRoot))
            {
                return;
            }

            current = Path.GetDirectoryName(current);
        }
    }

    private static string CreateUserSegment()
    {
        var identity =
            $"{Environment.UserDomainName}\\{Environment.UserName}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return Convert.ToHexString(hash)[..16];
    }
}

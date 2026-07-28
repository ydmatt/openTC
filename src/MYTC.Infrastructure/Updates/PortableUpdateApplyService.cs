using System.Diagnostics;
using System.Security.Cryptography;
using MYTC.Application.Updates;

namespace MYTC.Infrastructure.Updates;

public sealed class PortableUpdateApplyService
{
    public async Task<PortableUpdateApplyResult> ApplyAsync(
        string installRoot,
        string stagedRoot,
        string backupParent,
        string logParent,
        CancellationToken cancellationToken = default)
    {
        var fullInstallRoot = Path.GetFullPath(installRoot);
        var fullStagedRoot = Path.GetFullPath(stagedRoot);
        Directory.CreateDirectory(backupParent);
        Directory.CreateDirectory(logParent);

        var stamp = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var backupRoot = Path.Combine(
            Path.GetFullPath(backupParent),
            $"backup-{stamp}");
        var logPath = Path.Combine(
            Path.GetFullPath(logParent),
            $"update-{stamp}.log");
        Directory.CreateDirectory(backupRoot);

        var log = new List<string>
        {
            $"{DateTimeOffset.Now:O} 开始应用 MYTC 升级。",
            $"安装目录：{fullInstallRoot}",
            $"暂存目录：{fullStagedRoot}",
            $"备份目录：{backupRoot}",
        };

        string? previousVersion = null;
        string newVersion = "未知";
        var changedPaths = new List<ChangedPath>();

        try
        {
            var newManifestPath = Path.Combine(
                fullStagedRoot,
                PortableUpdateConstants.ManifestFileName);
            var newManifest =
                await PortableUpdatePackageService.ReadManifestAsync(
                    newManifestPath,
                    cancellationToken);
            PortableUpdatePackageService.ValidateManifest(
                newManifest,
                new Version(0, 0),
                requireNewerVersion: false);
            await PortableUpdatePackageService.ValidateExtractedFilesAsync(
                fullStagedRoot,
                newManifest,
                cancellationToken);
            newVersion = newManifest.Version;

            var oldManifestPath = Path.Combine(
                fullInstallRoot,
                PortableUpdateConstants.ManifestFileName);
            PortableUpdateManifest? oldManifest = null;
            if (File.Exists(oldManifestPath))
            {
                oldManifest =
                    await PortableUpdatePackageService.ReadManifestAsync(
                        oldManifestPath,
                        cancellationToken);
                previousVersion = oldManifest.Version;
            }
            else
            {
                var currentExecutable = Path.Combine(
                    fullInstallRoot,
                    PortableUpdateConstants.MainExecutableName);
                if (File.Exists(currentExecutable))
                {
                    previousVersion = FileVersionInfo
                        .GetVersionInfo(currentExecutable)
                        .FileVersion;
                }
            }

            var newFiles = newManifest.Files
                .Select(file =>
                    PortableUpdatePackageService.NormalizeRelativePath(file.Path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var file in newManifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = PortableUpdatePackageService
                    .NormalizeRelativePath(file.Path);
                var source = GetContainedPath(fullStagedRoot, relative);
                var destination = GetContainedPath(fullInstallRoot, relative);
                await ReplaceFileAsync(
                    source,
                    destination,
                    relative,
                    backupRoot,
                    changedPaths,
                    cancellationToken);

                var installedHash = await ComputeSha256Async(
                    destination,
                    cancellationToken);
                if (!StringComparer.OrdinalIgnoreCase.Equals(
                        installedHash,
                        file.Sha256))
                {
                    throw new IOException($"升级后哈希校验失败：{relative}。");
                }

                log.Add($"已更新：{relative}");
            }

            await ReplaceFileAsync(
                newManifestPath,
                oldManifestPath,
                PortableUpdateConstants.ManifestFileName,
                backupRoot,
                changedPaths,
                cancellationToken);
            log.Add($"已更新：{PortableUpdateConstants.ManifestFileName}");

            if (oldManifest is not null)
            {
                foreach (var oldFile in oldManifest.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relative = PortableUpdatePackageService
                        .NormalizeRelativePath(oldFile.Path);
                    if (newFiles.Contains(relative))
                    {
                        continue;
                    }

                    var obsoletePath = GetContainedPath(
                        fullInstallRoot,
                        relative);
                    if (!File.Exists(obsoletePath))
                    {
                        continue;
                    }

                    await BackupExistingFileAsync(
                        obsoletePath,
                        relative,
                        backupRoot,
                        cancellationToken);
                    changedPaths.Add(new ChangedPath(
                        obsoletePath,
                        relative,
                        ExistedBefore: true));
                    File.Delete(obsoletePath);
                    log.Add($"已移除旧版受管文件：{relative}");
                }
            }

            log.Add($"{DateTimeOffset.Now:O} 升级成功：{newVersion}。");
            await WriteLogAsync(logPath, log, cancellationToken);
            return new PortableUpdateApplyResult(
                true,
                previousVersion,
                newVersion,
                backupRoot,
                logPath,
                null);
        }
        catch (Exception exception)
        {
            log.Add($"{DateTimeOffset.Now:O} 升级失败：{exception}");
            try
            {
                await RollBackAsync(
                    backupRoot,
                    changedPaths,
                    log,
                    CancellationToken.None);
            }
            catch (Exception rollbackException)
            {
                log.Add($"回滚也发生错误：{rollbackException}");
            }

            await WriteLogAsync(logPath, log, CancellationToken.None);
            return new PortableUpdateApplyResult(
                false,
                previousVersion,
                newVersion,
                backupRoot,
                logPath,
                exception.Message);
        }
    }

    private static async Task ReplaceFileAsync(
        string source,
        string destination,
        string relative,
        string backupRoot,
        ICollection<ChangedPath> changedPaths,
        CancellationToken cancellationToken)
    {
        var existedBefore = File.Exists(destination);
        if (existedBefore)
        {
            await BackupExistingFileAsync(
                destination,
                relative,
                backupRoot,
                cancellationToken);
        }

        var destinationDirectory = Path.GetDirectoryName(destination)
            ?? throw new IOException($"无法识别目标目录：{destination}。");
        Directory.CreateDirectory(destinationDirectory);
        var temporary = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileName(destination)}.mytc-new-{Guid.NewGuid():N}");
        try
        {
            await CopyFileAsync(source, temporary, cancellationToken);
            File.Move(temporary, destination, overwrite: true);
            changedPaths.Add(new ChangedPath(
                destination,
                relative,
                existedBefore));
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static async Task BackupExistingFileAsync(
        string source,
        string relative,
        string backupRoot,
        CancellationToken cancellationToken)
    {
        var backupPath = GetContainedPath(backupRoot, relative);
        Directory.CreateDirectory(
            Path.GetDirectoryName(backupPath)
            ?? throw new IOException("无法创建升级备份目录。"));
        await CopyFileAsync(source, backupPath, cancellationToken);
    }

    private static async Task RollBackAsync(
        string backupRoot,
        IReadOnlyList<ChangedPath> changedPaths,
        ICollection<string> log,
        CancellationToken cancellationToken)
    {
        foreach (var changed in changedPaths.Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (changed.ExistedBefore)
            {
                var backupPath = GetContainedPath(
                    backupRoot,
                    changed.RelativePath);
                if (!File.Exists(backupPath))
                {
                    throw new IOException(
                        $"回滚备份缺失：{changed.RelativePath}。");
                }

                var directory = Path.GetDirectoryName(changed.Destination)
                    ?? throw new IOException("回滚目标目录无效。");
                Directory.CreateDirectory(directory);
                await CopyFileAsync(
                    backupPath,
                    changed.Destination,
                    cancellationToken);
            }
            else if (File.Exists(changed.Destination))
            {
                File.Delete(changed.Destination);
            }

            log.Add($"已回滚：{changed.RelativePath}");
        }
    }

    private static async Task CopyFileAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(
            destination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await input.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
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

    private static string GetContainedPath(string root, string relative)
    {
        var fullRoot = EnsureTrailingSeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!fullPath.StartsWith(
                fullRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"路径越出允许目录：{relative}。");
        }

        return fullPath;
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static async Task WriteLogAsync(
        string logPath,
        IEnumerable<string> lines,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(logPath)
            ?? throw new IOException("升级日志目录无效。"));
        await File.WriteAllLinesAsync(logPath, lines, cancellationToken);
    }

    private sealed record ChangedPath(
        string Destination,
        string RelativePath,
        bool ExistedBefore);
}

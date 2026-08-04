using System.Diagnostics;
using Microsoft.Win32;
using MYTC.Application.Abstractions;

namespace MYTC.Infrastructure.Files;

/// <summary>
/// Invokes an installed WinRAR executable to extract one archive into a
/// caller-supplied directory. Existing files are never overwritten.
/// </summary>
public sealed class WinRarArchiveExtractionService : IArchiveExtractionService
{
    private static readonly HashSet<string> SupportedExtensions = new(
        [
            ".7z", ".ace", ".arj", ".bz2", ".cab", ".gz", ".iso",
            ".jar", ".lzh", ".rar", ".tar", ".tgz", ".xz", ".zip",
        ],
        StringComparer.OrdinalIgnoreCase);

    private readonly Func<string?> _executableLocator;
    private readonly Func<ProcessStartInfo, CancellationToken, Task<int>>
        _processRunner;

    public WinRarArchiveExtractionService(
        Func<string?>? executableLocator = null,
        Func<ProcessStartInfo, CancellationToken, Task<int>>? processRunner = null)
    {
        _executableLocator = executableLocator ?? FindWinRarExecutable;
        _processRunner = processRunner ?? RunProcessAsync;
    }

    public string? FindSuggestedExecutablePath() => _executableLocator();

    public bool CanExtract(string archivePath, string? executablePath)
    {
        return IsSupportedArchive(archivePath) &&
            !string.IsNullOrWhiteSpace(executablePath) &&
            File.Exists(executablePath);
    }

    public async Task ExtractToDirectoryAsync(
        string archivePath,
        string destinationDirectory,
        string? executablePath,
        CancellationToken cancellationToken = default)
    {
        await ExtractCoreAsync(
            archivePath,
            destinationDirectory,
            executablePath,
            requireExistingDestination: true,
            cancellationToken);
    }

    public async Task ExtractToNamedDirectoryAsync(
        string archivePath,
        string destinationParentDirectory,
        string? executablePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationParentDirectory);
        var fullArchivePath = Path.GetFullPath(archivePath);
        var parent = Path.GetFullPath(destinationParentDirectory);
        if (!Directory.Exists(parent))
        {
            throw new DirectoryNotFoundException(
                "解压目标的父目录不存在。");
        }

        await ExtractCoreAsync(
            fullArchivePath,
            Path.Combine(
                parent,
                Path.GetFileNameWithoutExtension(fullArchivePath)),
            executablePath,
            requireExistingDestination: false,
            cancellationToken);
    }

    private async Task ExtractCoreAsync(
        string archivePath,
        string destinationDirectory,
        string? executablePath,
        bool requireExistingDestination,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        var fullArchivePath = Path.GetFullPath(archivePath);
        var fullDestinationDirectory = Path.GetFullPath(destinationDirectory);
        if (!File.Exists(fullArchivePath))
        {
            throw new FileNotFoundException("找不到要解压的压缩包。", fullArchivePath);
        }

        if (requireExistingDestination &&
            !Directory.Exists(fullDestinationDirectory))
        {
            throw new DirectoryNotFoundException("解压目标目录不存在。");
        }

        if (!IsSupportedArchive(fullArchivePath))
        {
            throw new NotSupportedException("该文件类型不在 WinRAR 解压菜单支持范围内。");
        }

        var executable = executablePath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
        {
            throw new InvalidOperationException("未检测到可用的 WinRAR。请先安装 WinRAR。");
        }

        Directory.CreateDirectory(fullDestinationDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = fullDestinationDirectory,
        };
        startInfo.ArgumentList.Add("x");
        startInfo.ArgumentList.Add("-ibck");
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-o-");
        startInfo.ArgumentList.Add(fullArchivePath);
        startInfo.ArgumentList.Add(
            Path.EndsInDirectorySeparator(fullDestinationDirectory)
                ? fullDestinationDirectory
                : fullDestinationDirectory + Path.DirectorySeparatorChar);

        var exitCode = await _processRunner(startInfo, cancellationToken);
        if (exitCode > 1)
        {
            throw new InvalidOperationException(
                $"WinRAR 解压失败，退出代码：{exitCode}。");
        }
    }

    private static bool IsSupportedArchive(string archivePath)
    {
        return SupportedExtensions.Contains(Path.GetExtension(archivePath));
    }

    private static string? FindWinRarExecutable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        foreach (var baseKey in new[]
                 {
                     Registry.CurrentUser,
                     Registry.LocalMachine,
                 })
        {
            try
            {
                using var appPath = baseKey.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\WinRAR.exe");
                if (appPath?.GetValue(string.Empty) is string registered &&
                    File.Exists(registered))
                {
                    return registered;
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Fall back to standard portable installation locations.
            }
        }

        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "WinRAR",
                "WinRAR.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "WinRAR",
                "WinRAR.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task<int> RunProcessAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 WinRAR。");
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }
}

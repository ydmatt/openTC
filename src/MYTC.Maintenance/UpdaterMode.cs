using System.Diagnostics;
using Microsoft.Win32;
using MYTC.Application.Shell;
using MYTC.Application.Updates;
using MYTC.Infrastructure.Updates;

namespace MYTC.Maintenance;

internal static class UpdaterMode
{
    public static async Task<int> RunAsync(string[] arguments)
    {
        var options = ParseOptions(arguments);
        var installRoot = RequireOption(options, "--install-root");
        var stagedRoot = RequireOption(options, "--staged-root");
        var processIdText = RequireOption(options, "--pid");
        var silent = options.ContainsKey("--silent");
        if (!int.TryParse(processIdText, out var processId) ||
            processId <= 0)
        {
            throw new ArgumentException("升级参数中的进程编号无效。");
        }

        if (!InstallationPathPolicy.IsSupportedFixedLocalPath(
                installRoot,
                out var reason))
        {
            throw new InvalidOperationException(reason);
        }

        WinEBridge.SignalExit();
        await WaitForProcessAsync(processId, TimeSpan.FromMinutes(2));
        try
        {
            await WaitForInstalledMaintenanceProcessesAsync(
                installRoot,
                TimeSpan.FromSeconds(15));
        }
        catch
        {
            RestartMainApplication(installRoot);
            throw;
        }

        var localRoot = options.TryGetValue(
                "--state-root",
                out var configuredStateRoot) &&
            !string.IsNullOrWhiteSpace(configuredStateRoot)
                ? Path.GetFullPath(configuredStateRoot)
                : Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "MYTC",
                    "updates");
        var service = new PortableUpdateApplyService();
        var result = await service.ApplyAsync(
            installRoot,
            stagedRoot,
            Path.Combine(localRoot, "backups"),
            Path.Combine(localRoot, "logs"));
        TryDeleteStagedUpdate(
            stagedRoot,
            Path.Combine(localRoot, "staging"));

        if (!result.Succeeded)
        {
            if (!silent)
            {
                StartUpdaterHostCleanup(installRoot);
                MessageBox.Show(
                    "升级失败，已尝试恢复原版本。\n\n" +
                    $"{result.ErrorMessage}\n\n日志：{result.LogPath}",
                    "openTC 升级失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                RestartMainApplication(installRoot);
                RestartBridgeIfRegistered(installRoot);
            }

            return 1;
        }

        if (!silent)
        {
            StartUpdaterHostCleanup(installRoot);
            RestartBridgeIfRegistered(installRoot);
            RestartMainApplication(installRoot, result.NewVersion);
        }

        return 0;
    }

    private static void StartUpdaterHostCleanup(string installRoot)
    {
        var cleanupExecutable = Path.Combine(
            installRoot,
            PortableUpdateConstants.MaintenanceExecutableName);
        var currentExecutable = Environment.ProcessPath;
        var currentDirectory = string.IsNullOrWhiteSpace(currentExecutable)
            ? null
            : Path.GetDirectoryName(currentExecutable);
        if (!File.Exists(cleanupExecutable) ||
            string.IsNullOrWhiteSpace(currentDirectory))
        {
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = cleanupExecutable,
            WorkingDirectory = installRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--cleanup-updater");
        startInfo.ArgumentList.Add("--pid");
        startInfo.ArgumentList.Add(
            Environment.ProcessId.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--path");
        startInfo.ArgumentList.Add(currentDirectory);
        try
        {
            _ = Process.Start(startInfo);
        }
        catch
        {
            // The host lives under LocalAppData and can be cleaned manually.
        }
    }

    private static Dictionary<string, string> ParseOptions(
        IReadOnlyList<string> arguments)
    {
        var result = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < arguments.Count; index++)
        {
            if (!arguments[index].StartsWith(
                    "--",
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (index + 1 < arguments.Count &&
                !arguments[index + 1].StartsWith(
                    "--",
                    StringComparison.Ordinal))
            {
                result[arguments[index]] = arguments[index + 1];
                index++;
            }
            else
            {
                result[arguments[index]] = string.Empty;
            }
        }

        return result;
    }

    private static string RequireOption(
        IReadOnlyDictionary<string, string> options,
        string name)
    {
        if (!options.TryGetValue(name, out var value) ||
            string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"升级缺少参数：{name}。");
        }

        return value;
    }

    private static async Task WaitForProcessAsync(
        int processId,
        TimeSpan timeout)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return;
        }

        using (process)
        {
            using var timeoutToken = new CancellationTokenSource(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutToken.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException(
                    "等待 openTC 退出超时。请关闭正在运行的 openTC 后重试升级。");
            }
        }
    }

    private static async Task WaitForInstalledMaintenanceProcessesAsync(
        string installRoot,
        TimeSpan timeout)
    {
        var fullRoot = EnsureTrailingSeparator(Path.GetFullPath(installRoot));
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var blocking = Process
                .GetProcessesByName("MYTC.Maintenance")
                .Where(process => process.Id != Environment.ProcessId)
                .Where(process => IsProcessInside(process, fullRoot))
                .ToArray();
            if (blocking.Length == 0)
            {
                return;
            }

            foreach (var process in blocking)
            {
                process.Dispose();
            }

            await Task.Delay(250);
        }

        throw new IOException(
            "安装目录中的 openTC 维护/Win+E 桥接进程仍未退出。请关闭维护工具后重试；不要强行覆盖。");
    }

    private static bool IsProcessInside(Process process, string fullRoot)
    {
        try
        {
            var path = process.MainModule?.FileName;
            return path?.StartsWith(
                fullRoot,
                StringComparison.OrdinalIgnoreCase) == true;
        }
        catch
        {
            return false;
        }
        finally
        {
            process.Dispose();
        }
    }

    private static void RestartMainApplication(
        string installRoot,
        string? completedUpdateVersion = null)
    {
        var executable = Path.Combine(
            installRoot,
            PortableUpdateConstants.MainExecutableName);
        if (File.Exists(executable))
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = installRoot,
                UseShellExecute = false,
            };
            if (!string.IsNullOrWhiteSpace(completedUpdateVersion))
            {
                startInfo.ArgumentList.Add("--update-complete");
                startInfo.ArgumentList.Add(completedUpdateVersion);
            }

            Process.Start(startInfo);
        }
    }

    private static void RestartBridgeIfRegistered(string installRoot)
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run");
        var configured = runKey?.GetValue(
            ShellIntegrationConstants.BridgeRunValueName) as string;
        if (configured?.Contains(
                "--bridge",
                StringComparison.OrdinalIgnoreCase) != true)
        {
            return;
        }

        var maintenance = Path.Combine(
            installRoot,
            PortableUpdateConstants.MaintenanceExecutableName);
        if (File.Exists(maintenance))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = maintenance,
                    WorkingDirectory = installRoot,
                    UseShellExecute = false,
                    ArgumentList = { "--bridge" },
                });
            }
            catch
            {
                // Win+E falls back to Explorer until the next logon/start.
            }
        }
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static void TryDeleteStagedUpdate(
        string packageRoot,
        string stagingRoot)
    {
        try
        {
            var fullStagingRoot = Path.GetFullPath(stagingRoot)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            var allowedPrefix =
                fullStagingRoot + Path.DirectorySeparatorChar;
            var candidate = new DirectoryInfo(
                Path.GetFullPath(packageRoot));
            if (!candidate.FullName.StartsWith(
                    allowedPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            while (candidate.Parent is not null &&
                   !StringComparer.OrdinalIgnoreCase.Equals(
                       candidate.Parent.FullName.TrimEnd(
                           Path.DirectorySeparatorChar,
                           Path.AltDirectorySeparatorChar),
                       fullStagingRoot))
            {
                candidate = candidate.Parent;
            }

            if (candidate.Parent is not null &&
                StringComparer.OrdinalIgnoreCase.Equals(
                    candidate.Parent.FullName.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar),
                    fullStagingRoot) &&
                Directory.Exists(candidate.FullName))
            {
                Directory.Delete(candidate.FullName, recursive: true);
            }
        }
        catch
        {
            // Staging cleanup is best-effort after the update result is known.
        }
    }
}

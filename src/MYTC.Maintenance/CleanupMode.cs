using System.Diagnostics;

namespace MYTC.Maintenance;

internal static class CleanupMode
{
    public static async Task<int> RunAsync(string[] arguments)
    {
        var processIdText = GetOption(arguments, "--pid");
        var targetPath = GetOption(arguments, "--path");
        if (!int.TryParse(processIdText, out var processId) ||
            processId <= 0)
        {
            return 2;
        }

        var allowedRoot = EnsureTrailingSeparator(Path.GetFullPath(
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "MYTC",
                "updates",
                "updater-hosts")));
        var fullTarget = Path.GetFullPath(targetPath);
        if (!fullTarget.StartsWith(
                allowedRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            using var timeout = new CancellationTokenSource(
                TimeSpan.FromMinutes(3));
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (ArgumentException)
        {
            // The updater has already exited.
        }
        catch (OperationCanceledException)
        {
            return 4;
        }

        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                if (Directory.Exists(fullTarget))
                {
                    Directory.Delete(fullTarget, recursive: true);
                }

                return 0;
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException)
            {
                await Task.Delay(500);
            }
        }

        return 5;
    }

    private static string GetOption(
        IReadOnlyList<string> arguments,
        string name)
    {
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(
                    arguments[index],
                    name))
            {
                return arguments[index + 1];
            }
        }

        return string.Empty;
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}

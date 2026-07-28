using System.Diagnostics;
using MYTC.Application.Abstractions;

namespace MYTC.Infrastructure.Files;

public sealed class ShellFileLauncher : IFileLauncher
{
    public void Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }

    public string? TryResolveShortcutTarget(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return ShellShortcutTargetResolver.TryResolve(path);
    }
}

using Microsoft.Win32;
using MYTC.Application.Abstractions;

namespace MYTC.Infrastructure.Configuration;

public sealed class WindowsAutoStartService : IAutoStartService
{
    private const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MYTC";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        var configured = key?.GetValue(ValueName) as string;
        return StringComparer.OrdinalIgnoreCase.Equals(
            configured,
            BuildCommand());
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            key.SetValue(ValueName, BuildCommand(), RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private static string BuildCommand()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new InvalidOperationException("无法确定 MYTC 程序路径。");
        }

        return $"\"{executable}\"";
    }
}

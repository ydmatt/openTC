using System.ComponentModel;
using System.Runtime.InteropServices;
using MYTC.Application.Abstractions;

namespace MYTC.Infrastructure.Files;

/// <summary>
/// Opens the standard Windows Shell property sheet for a file or directory.
/// </summary>
public sealed class ShellPropertiesService : IPropertiesService
{
    private const uint ShopFilePath = 0x00000002;

    public void Show(string path, nint ownerHandle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var resolvedPath = Path.GetFullPath(path);
        if (!File.Exists(resolvedPath) && !Directory.Exists(resolvedPath))
        {
            throw new FileNotFoundException("要查看属性的项目不存在。", resolvedPath);
        }

        if (SHObjectProperties(
                ownerHandle,
                ShopFilePath,
                resolvedPath,
                null))
        {
            return;
        }

        var error = Marshal.GetLastWin32Error();
        if (error != 0)
        {
            throw new Win32Exception(error);
        }
    }

    [DllImport(
        "shell32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHObjectProperties(
        nint ownerHandle,
        uint objectType,
        string objectName,
        string? propertyPage);
}

using System.Runtime.InteropServices;
using MYTC.Application.Abstractions;

namespace MYTC.Infrastructure.Files;

public sealed class ShellOpenWithService : IOpenWithService
{
    private const uint OpenAndExecute = 0x00000004;

    public void Show(string filePath, nint ownerHandle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var information = new OpenAsInfo
        {
            File = Path.GetFullPath(filePath),
            FileClass = null,
            Flags = OpenAndExecute,
        };
        Marshal.ThrowExceptionForHR(
            SHOpenWithDialog(ownerHandle, ref information));
    }

    [DllImport(
        "shell32.dll",
        CharSet = CharSet.Unicode,
        ExactSpelling = true)]
    private static extern int SHOpenWithDialog(
        nint ownerHandle,
        ref OpenAsInfo openAsInfo);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenAsInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string File;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? FileClass;

        public uint Flags;
    }
}

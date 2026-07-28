using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace MYTC.Infrastructure.Files;

internal static class ShellShortcutTargetResolver
{
    public static string? TryResolve(string shortcutPath)
    {
        if (!File.Exists(shortcutPath) ||
            !Path.GetExtension(shortcutPath).Equals(
                ".lnk",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        object shellLinkObject = new ShellLinkComObject();
        var shellLink = (IShellLinkW)shellLinkObject;
        try
        {
            ((IPersistFile)shellLink).Load(Path.GetFullPath(shortcutPath), 0);
            shellLink.Resolve(IntPtr.Zero, ResolveNoUi);
            var target = new StringBuilder(32768);
            shellLink.GetPath(
                target,
                target.Capacity,
                IntPtr.Zero,
                GetPathRaw);
            return string.IsNullOrWhiteSpace(target.ToString())
                ? null
                : Environment.ExpandEnvironmentVariables(target.ToString());
        }
        catch (Exception exception) when (
            exception is COMException or
                IOException or
                UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            _ = Marshal.FinalReleaseComObject(shellLinkObject);
        }
    }

    private const uint ResolveNoUi = 0x1;
    private const uint GetPathRaw = 0x4;

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class ShellLinkComObject;

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file,
            int fileLength,
            IntPtr findData,
            uint flags);

        void GetIDList(out IntPtr itemIdList);

        void SetIDList(IntPtr itemIdList);

        void GetDescription(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name,
            int nameLength);

        void SetDescription(
            [MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetWorkingDirectory(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory,
            int directoryLength);

        void SetWorkingDirectory(
            [MarshalAs(UnmanagedType.LPWStr)] string directory);

        void GetArguments(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments,
            int argumentsLength);

        void SetArguments(
            [MarshalAs(UnmanagedType.LPWStr)] string arguments);

        void GetHotkey(out short hotkey);

        void SetHotkey(short hotkey);

        void GetShowCmd(out int showCommand);

        void SetShowCmd(int showCommand);

        void GetIconLocation(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath,
            int iconPathLength,
            out int iconIndex);

        void SetIconLocation(
            [MarshalAs(UnmanagedType.LPWStr)] string iconPath,
            int iconIndex);

        void SetRelativePath(
            [MarshalAs(UnmanagedType.LPWStr)] string relativePath,
            uint reserved);

        void Resolve(IntPtr windowHandle, uint flags);

        void SetPath(
            [MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}

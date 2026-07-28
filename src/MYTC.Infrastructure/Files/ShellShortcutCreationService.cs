using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using MYTC.Application.Abstractions;

namespace MYTC.Infrastructure.Files;

public sealed class ShellShortcutCreationService : IShortcutCreationService
{
    public Task<IReadOnlyList<string>> CreateAsync(
        IReadOnlyList<string> sourcePaths,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        if (!Directory.Exists(destinationDirectory))
        {
            throw new DirectoryNotFoundException(
                $"目标目录不存在：{destinationDirectory}");
        }

        var created = new List<string>(sourcePaths.Count);
        foreach (var sourcePath in sourcePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullSourcePath = Path.GetFullPath(sourcePath);
            if (!File.Exists(fullSourcePath) &&
                !Directory.Exists(fullSourcePath))
            {
                throw new FileNotFoundException(
                    "快捷方式目标不存在。",
                    fullSourcePath);
            }

            var shortcutPath = GetAvailableShortcutPath(
                destinationDirectory,
                fullSourcePath);
            CreateShellLink(fullSourcePath, shortcutPath);
            created.Add(shortcutPath);
        }

        return Task.FromResult<IReadOnlyList<string>>(created);
    }

    private static string GetAvailableShortcutPath(
        string destinationDirectory,
        string sourcePath)
    {
        var sourceName = Path.GetFileName(
            Path.TrimEndingDirectorySeparator(sourcePath));
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            sourceName = "快捷方式";
        }

        var baseName = sourceName + " - 快捷方式";
        var candidate = Path.Combine(
            destinationDirectory,
            baseName + ".lnk");
        for (var copyNumber = 2; File.Exists(candidate); copyNumber++)
        {
            candidate = Path.Combine(
                destinationDirectory,
                $"{baseName} ({copyNumber}).lnk");
        }

        return candidate;
    }

    private static void CreateShellLink(
        string targetPath,
        string shortcutPath)
    {
        object shellLinkObject = new ShellLinkComObject();
        var shellLink = (IShellLinkW)shellLinkObject;
        try
        {
            shellLink.SetPath(targetPath);
            var workingDirectory = Directory.Exists(targetPath)
                ? targetPath
                : Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                shellLink.SetWorkingDirectory(workingDirectory);
            }

            shellLink.SetDescription($"指向 {targetPath} 的快捷方式");
            ((IPersistFile)shellLink).Save(shortcutPath, false);
        }
        finally
        {
            _ = Marshal.FinalReleaseComObject(shellLinkObject);
        }
    }

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

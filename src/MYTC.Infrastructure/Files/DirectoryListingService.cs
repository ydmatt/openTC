using MYTC.Application.Abstractions;
using MYTC.Domain.Files;

namespace MYTC.Infrastructure.Files;

public sealed class DirectoryListingService : IDirectoryListingService
{
    public Task<IReadOnlyList<FileSystemEntry>> ListAsync(
        string path,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return Task.Run<IReadOnlyList<FileSystemEntry>>(
            () => Enumerate(path, cancellationToken),
            cancellationToken);
    }

    private static IReadOnlyList<FileSystemEntry> Enumerate(
        string path,
        CancellationToken cancellationToken)
    {
        var directory = new DirectoryInfo(path);
        if (!directory.Exists)
        {
            throw new DirectoryNotFoundException($"Directory does not exist: {path}");
        }

        var entries = new List<FileSystemEntry>();
        foreach (var item in directory.EnumerateFileSystemInfos())
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (item is DirectoryInfo &&
                    StringComparer.OrdinalIgnoreCase.Equals(
                        item.Name,
                        ".MYTC-RecycleBin"))
                {
                    continue;
                }

                entries.Add(CreateEntry(item));
            }
            catch (FileNotFoundException)
            {
                // The entry disappeared between enumeration and metadata access.
            }
            catch (DirectoryNotFoundException)
            {
                // The entry disappeared between enumeration and metadata access.
            }
        }

        return entries;
    }

    private static FileSystemEntry CreateEntry(FileSystemInfo item)
    {
        if (item is DirectoryInfo directory)
        {
            return new FileSystemEntry(
                directory.FullName,
                directory.Name,
                EntryKind.Directory,
                directory.LastWriteTime,
                "文件夹",
                null);
        }

        var file = (FileInfo)item;
        var extension = file.Extension.TrimStart('.');
        var typeDisplayName = string.IsNullOrWhiteSpace(extension)
            ? "文件"
            : $"{extension.ToUpperInvariant()} 文件";

        return new FileSystemEntry(
            file.FullName,
            file.Name,
            EntryKind.File,
            file.LastWriteTime,
            typeDisplayName,
            file.Length);
    }
}

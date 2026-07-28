using MYTC.Domain.Files;

namespace MYTC.Application.Files;

public static class PathClipboardTextBuilder
{
    public static string Build(
        IReadOnlyList<FileSystemEntry> selectedItems,
        string currentDirectory)
    {
        ArgumentNullException.ThrowIfNull(selectedItems);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentDirectory);

        var paths = selectedItems.Count > 0
            ? selectedItems.Select(item => item.FullPath)
            : [currentDirectory];
        return string.Join(Environment.NewLine, paths);
    }
}

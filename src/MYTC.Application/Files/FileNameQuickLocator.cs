using MYTC.Domain.Files;

namespace MYTC.Application.Files;

/// <summary>
/// Finds the item selected by the file-list type-ahead interaction.
/// Directories are always searched before files; within each kind, name starts
/// are preferred over matches at any position.
/// </summary>
public static class FileNameQuickLocator
{
    public static int FindMatchIndex(
        IReadOnlyList<FileSystemEntry> entries,
        string prefix)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return -1;
        }

        var query = prefix.Trim();
        var directoryStartsWithIndex = FindIndex(
            entries,
            query,
            entry => entry.Kind == EntryKind.Directory,
            static (name, value) => name.StartsWith(
                value,
                StringComparison.OrdinalIgnoreCase));
        if (directoryStartsWithIndex >= 0)
        {
            return directoryStartsWithIndex;
        }

        var directoryContainsIndex = FindIndex(
            entries,
            query,
            entry => entry.Kind == EntryKind.Directory,
            static (name, value) => name.Contains(
                value,
                StringComparison.OrdinalIgnoreCase));
        if (directoryContainsIndex >= 0)
        {
            return directoryContainsIndex;
        }

        var fileStartsWithIndex = FindIndex(
            entries,
            query,
            entry => entry.Kind == EntryKind.File,
            static (name, value) => name.StartsWith(
                value,
                StringComparison.OrdinalIgnoreCase));
        if (fileStartsWithIndex >= 0)
        {
            return fileStartsWithIndex;
        }

        return FindIndex(
            entries,
            query,
            entry => entry.Kind == EntryKind.File,
            static (name, value) => name.Contains(
                value,
                StringComparison.OrdinalIgnoreCase));
    }

    private static int FindIndex(
        IReadOnlyList<FileSystemEntry> entries,
        string query,
        Func<FileSystemEntry, bool> filter,
        Func<string, string, bool> nameMatches)
    {
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (filter(entry) && nameMatches(entry.Name, query))
            {
                return index;
            }
        }

        return -1;
    }
}

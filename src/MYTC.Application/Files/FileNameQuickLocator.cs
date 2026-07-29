using MYTC.Domain.Files;

namespace MYTC.Application.Files;

/// <summary>
/// Finds the item selected by the file-list type-ahead interaction.
/// Directories are preferred so a same-prefix file does not hide a folder.
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

        var normalizedPrefix = prefix.Trim();
        var directoryIndex = FindIndex(
            entries,
            normalizedPrefix,
            entry => entry.Kind == EntryKind.Directory);
        return directoryIndex >= 0
            ? directoryIndex
            : FindIndex(entries, normalizedPrefix, static _ => true);
    }

    private static int FindIndex(
        IReadOnlyList<FileSystemEntry> entries,
        string prefix,
        Func<FileSystemEntry, bool> filter)
    {
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (filter(entry) && entry.Name.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }
}

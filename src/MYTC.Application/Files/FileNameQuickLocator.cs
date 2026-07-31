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
        return FindMatchIndexes(entries, prefix).FirstOrDefault(-1);
    }

    public static IReadOnlyList<int> FindMatchIndexes(
        IReadOnlyList<FileSystemEntry> entries,
        string prefix)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return [];
        }

        var query = prefix.Trim();
        return [
            .. FindIndexes(
            entries,
            query,
            entry => entry.Kind == EntryKind.Directory,
            static (name, value) => name.StartsWith(
                value,
                StringComparison.OrdinalIgnoreCase)),
            .. FindIndexes(
            entries,
            query,
            entry => entry.Kind == EntryKind.Directory,
            static (name, value) => name.Contains(
                value,
                StringComparison.OrdinalIgnoreCase),
            excludeStartsWith: true),
            .. FindIndexes(
            entries,
            query,
            entry => entry.Kind == EntryKind.File,
            static (name, value) => name.StartsWith(
                value,
                StringComparison.OrdinalIgnoreCase)),
            .. FindIndexes(
            entries,
            query,
            entry => entry.Kind == EntryKind.File,
            static (name, value) => name.Contains(
                value,
                StringComparison.OrdinalIgnoreCase),
            excludeStartsWith: true),
        ];
    }

    private static IEnumerable<int> FindIndexes(
        IReadOnlyList<FileSystemEntry> entries,
        string query,
        Func<FileSystemEntry, bool> filter,
        Func<string, string, bool> nameMatches,
        bool excludeStartsWith = false)
    {
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (filter(entry) &&
                nameMatches(entry.Name, query) &&
                (!excludeStartsWith || !entry.Name.StartsWith(
                    query,
                    StringComparison.OrdinalIgnoreCase)))
            {
                yield return index;
            }
        }
    }
}

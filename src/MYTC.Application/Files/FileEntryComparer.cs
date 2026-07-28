using MYTC.Domain.Files;

namespace MYTC.Application.Files;

public sealed class FileEntryComparer(SortDescriptor sort) : IComparer<FileSystemEntry>
{
    public int Compare(FileSystemEntry? left, FileSystemEntry? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        if (sort.FoldersFirst && left.Kind != right.Kind)
        {
            return left.Kind == EntryKind.Directory ? -1 : 1;
        }

        var comparison = sort.Column switch
        {
            FileSortColumn.Name => StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name),
            FileSortColumn.ModifiedAt => left.ModifiedAt.CompareTo(right.ModifiedAt),
            FileSortColumn.Type => StringComparer.CurrentCultureIgnoreCase.Compare(
                left.TypeDisplayName,
                right.TypeDisplayName),
            FileSortColumn.Size => Nullable.Compare(left.Size, right.Size),
            _ => throw new ArgumentOutOfRangeException(nameof(sort)),
        };

        if (comparison == 0)
        {
            comparison = StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name);
        }

        return sort.Direction == SortDirection.Ascending ? comparison : -comparison;
    }
}

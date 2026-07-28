namespace MYTC.Domain.Files;

public sealed record SortDescriptor(
    FileSortColumn Column,
    SortDirection Direction,
    bool FoldersFirst = true)
{
    public static SortDescriptor Default { get; } =
        new(FileSortColumn.Name, SortDirection.Ascending);
}

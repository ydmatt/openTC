using MYTC.Application.Files;
using MYTC.Domain.Files;

namespace MYTC.Tests.Files;

public sealed class FileEntryComparerTests
{
    [Fact]
    public void Sort_KeepsFoldersFirstEvenWhenDescending()
    {
        var entries = new List<FileSystemEntry>
        {
            Entry("large.bin", EntryKind.File, size: 200),
            Entry("Folder", EntryKind.Directory),
            Entry("small.bin", EntryKind.File, size: 10),
        };
        var comparer = new FileEntryComparer(
            new SortDescriptor(FileSortColumn.Size, SortDirection.Descending));

        entries.Sort(comparer);

        Assert.Equal("Folder", entries[0].Name);
        Assert.Equal("large.bin", entries[1].Name);
        Assert.Equal("small.bin", entries[2].Name);
    }

    [Fact]
    public void Sort_NameUsesNaturalCultureAwareOrder()
    {
        var entries = new List<FileSystemEntry>
        {
            Entry("B.txt", EntryKind.File, size: 1),
            Entry("a.txt", EntryKind.File, size: 1),
        };
        var comparer = new FileEntryComparer(SortDescriptor.Default);

        entries.Sort(comparer);

        Assert.Equal(["a.txt", "B.txt"], entries.Select(entry => entry.Name));
    }

    private static FileSystemEntry Entry(
        string name,
        EntryKind kind,
        long? size = null)
    {
        return new FileSystemEntry(
            name,
            name,
            kind,
            new DateTime(2026, 7, 27),
            kind == EntryKind.Directory ? "文件夹" : "文件",
            size);
    }
}

using MYTC.Application.Files;
using MYTC.Domain.Files;

namespace MYTC.Tests.Files;

public sealed class FileNameQuickLocatorTests
{
    [Fact]
    public void FindMatchIndex_MatchesCaseInsensitivePrefixAndPrefersDirectory()
    {
        var entries = new[]
        {
            Entry("nfc-notes.txt", EntryKind.File),
            Entry("NFCdaka", EntryKind.Directory),
            Entry("素材", EntryKind.Directory),
        };

        Assert.Equal(1, FileNameQuickLocator.FindMatchIndex(entries, "nfc"));
    }

    [Fact]
    public void FindMatchIndex_MatchesChineseFolderName()
    {
        var entries = new[]
        {
            Entry("项目资料", EntryKind.Directory),
            Entry("素材", EntryKind.Directory),
        };

        Assert.Equal(1, FileNameQuickLocator.FindMatchIndex(entries, "素材"));
        Assert.Equal(-1, FileNameQuickLocator.FindMatchIndex(entries, "不存在"));
    }

    [Fact]
    public void FindMatchIndex_MatchesNameSegmentAfterDatePrefix()
    {
        var entries = new[]
        {
            Entry("甘肃说明.txt", EntryKind.File),
            Entry("2026甘肃", EntryKind.Directory),
            Entry("2026青海", EntryKind.Directory),
        };

        Assert.Equal(1, FileNameQuickLocator.FindMatchIndex(entries, "甘肃"));
    }

    [Fact]
    public void FindMatchIndexes_ReturnsAllMatchesInFolderFirstOrder()
    {
        var entries = new[]
        {
            Entry("烟草说明.txt", EntryKind.File),
            Entry("2026烟草乙", EntryKind.Directory),
            Entry("烟草甲", EntryKind.Directory),
            Entry("2026烟草丙", EntryKind.Directory),
        };

        Assert.Equal(
            [2, 1, 3, 0],
            FileNameQuickLocator.FindMatchIndexes(entries, "烟草"));
    }

    private static FileSystemEntry Entry(string name, EntryKind kind)
    {
        return new FileSystemEntry(
            name,
            name,
            kind,
            DateTime.UnixEpoch,
            kind == EntryKind.Directory ? "文件夹" : "文件",
            null);
    }
}

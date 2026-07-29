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

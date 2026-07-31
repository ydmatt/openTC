using MYTC.Domain.Operations;
using MYTC.Application.Files;
using MYTC.Infrastructure.Files;

namespace MYTC.Tests.Files;

public sealed class ManagedFileOperationServiceTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(
        Path.GetTempPath(),
        "MYTC.Tests",
        Guid.NewGuid().ToString("N"));
    private readonly ManagedFileOperationService _service = new();

    public ManagedFileOperationServiceTests()
    {
        Directory.CreateDirectory(_sandbox);
    }

    [Fact]
    public async Task CopyDirectory_PreservesNestedFiles()
    {
        var sourceRoot = CreateDirectory("source");
        var targetRoot = CreateDirectory("target");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "nested"));
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "nested", "one.txt"), "hello");

        var result = await _service.ExecuteAsync(
            new FileOperationRequest(
                FileOperationKind.Copy,
                [sourceRoot],
                targetRoot,
                CollisionBehavior.Skip));

        Assert.True(result.Succeeded);
        Assert.Equal(
            "hello",
            await File.ReadAllTextAsync(
                Path.Combine(targetRoot, "source", "nested", "one.txt")));
    }

    [Fact]
    public async Task KeepBoth_GeneratesAvailableName()
    {
        var sourceRoot = CreateDirectory("source");
        var targetRoot = CreateDirectory("target");
        var sourceFile = Path.Combine(sourceRoot, "note.txt");
        await File.WriteAllTextAsync(sourceFile, "new");
        await File.WriteAllTextAsync(Path.Combine(targetRoot, "note.txt"), "old");

        var result = await _service.ExecuteAsync(
            new FileOperationRequest(
                FileOperationKind.Copy,
                [sourceFile],
                targetRoot,
                CollisionBehavior.KeepBoth));

        Assert.True(result.Succeeded);
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(targetRoot, "note.txt")));
        Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(targetRoot, "note (2).txt")));
    }

    [Fact]
    public async Task CopyToSameDirectory_CreatesBackupName()
    {
        var directory = CreateDirectory("same-directory-copy");
        var sourceFile = Path.Combine(directory, "note.txt");
        await File.WriteAllTextAsync(sourceFile, "backup me");

        var result = await _service.ExecuteAsync(
            new FileOperationRequest(
                FileOperationKind.Copy,
                [sourceFile],
                directory,
                CollisionBehavior.KeepBoth));

        Assert.True(result.Succeeded);
        Assert.Equal(
            "backup me",
            await File.ReadAllTextAsync(
                Path.Combine(directory, "note (2).txt")));
    }

    [Fact]
    public async Task MoveRenameCreateAndPermanentDelete_WorkInsideSandbox()
    {
        var sourceRoot = CreateDirectory("source");
        var targetRoot = CreateDirectory("target");
        var original = Path.Combine(sourceRoot, "item.txt");
        await File.WriteAllTextAsync(original, "data");

        var moveResult = await _service.ExecuteAsync(
            new FileOperationRequest(
                FileOperationKind.Move,
                [original],
                targetRoot));
        var moved = Path.Combine(targetRoot, "item.txt");
        var renamed = await _service.RenameAsync(moved, "renamed.txt");
        var created = await _service.CreateDirectoryAsync(targetRoot, "new folder");
        var deleteResult = await _service.ExecuteAsync(
            new FileOperationRequest(
                FileOperationKind.PermanentDelete,
                [renamed, created]));

        Assert.True(moveResult.Succeeded);
        Assert.False(File.Exists(original));
        Assert.True(deleteResult.Succeeded);
        Assert.False(File.Exists(renamed));
        Assert.False(Directory.Exists(created));
    }

    [Fact]
    public async Task CreateTextDocument_UsesShellNewStyleNameAndKeepsBoth()
    {
        var directory = CreateDirectory("new-text-document");

        var first = await _service.CreateTextDocumentAsync(directory);
        var second = await _service.CreateTextDocumentAsync(directory);

        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.Equal(".txt", Path.GetExtension(first));
        Assert.Equal(".txt", Path.GetExtension(second));
        Assert.NotEqual(first, second);
        Assert.Empty(await File.ReadAllBytesAsync(first));
    }

    [Fact]
    public async Task RecycleDelete_CanUndoLatestDeletion()
    {
        var directory = CreateDirectory("recycle-undo");
        var sourceFile = Path.Combine(
            directory,
            "undo-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(sourceFile, "restore me");
        var deletedAtUtc = DateTime.UtcNow;

        var deleteResult = await _service.ExecuteAsync(
            new FileOperationRequest(
                FileOperationKind.RecycleDelete,
                [sourceFile]));
        Assert.True(deleteResult.Succeeded);
        Assert.False(File.Exists(sourceFile));

        var restoreResult =
            await new ShellRecycleBinRestoreService().RestoreAsync(
                new RecycleDeletionBatch([sourceFile], deletedAtUtc));

        Assert.Empty(restoreResult.Failures);
        Assert.Equal(sourceFile, Assert.Single(restoreResult.RestoredPaths));
        Assert.Equal("restore me", await File.ReadAllTextAsync(sourceFile));
    }

    [Fact]
    public void SameDirectoryDrop_IsRejectedBeforeCollisionHandling()
    {
        var sourceRoot = CreateDirectory("same-drop");
        var sourceFile = Path.Combine(sourceRoot, "a.txt");
        File.WriteAllText(sourceFile, "a");
        var otherRoot = CreateDirectory("other-drop");

        Assert.True(FileDropGuards.IsSameDirectoryDrop(
            sourceRoot,
            [sourceFile]));
        Assert.False(FileDropGuards.IsSameDirectoryDrop(
            otherRoot,
            [sourceFile]));
    }

    [Fact]
    public async Task ShellShortcutService_CreatesRealUniqueLinkFiles()
    {
        var sourceRoot = CreateDirectory("shortcut-source");
        var targetRoot = CreateDirectory("shortcut-target");
        var sourceFile = Path.Combine(sourceRoot, "a.txt");
        await File.WriteAllTextAsync(sourceFile, "shortcut target");
        var service = new ShellShortcutCreationService();

        var first = Assert.Single(
            await service.CreateAsync([sourceFile], targetRoot));
        var second = Assert.Single(
            await service.CreateAsync([sourceFile], targetRoot));

        Assert.EndsWith("a.txt - 快捷方式.lnk", first);
        Assert.EndsWith("a.txt - 快捷方式 (2).lnk", second);
        Assert.True(new FileInfo(first).Length > 0);
        Assert.True(new FileInfo(second).Length > 0);
        Assert.Equal(
            Path.GetFullPath(sourceFile),
            new ShellFileLauncher().TryResolveShortcutTarget(first));
    }

    public void Dispose()
    {
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        var resolved = Path.GetFullPath(_sandbox);
        Assert.StartsWith(tempRoot, resolved, StringComparison.OrdinalIgnoreCase);
        if (Directory.Exists(resolved))
        {
            Directory.Delete(resolved, recursive: true);
        }
    }

    private string CreateDirectory(string name)
    {
        return Directory.CreateDirectory(Path.Combine(_sandbox, name)).FullName;
    }
}

using MYTC.Infrastructure.Files;

namespace MYTC.Tests.Files;

public sealed class ManagedNetworkRecycleServiceTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(
        Path.GetTempPath(),
        "MYTC.ManagedRecycle.Tests",
        Guid.NewGuid().ToString("N"));
    private readonly string _recycleRoot;
    private readonly ManagedNetworkRecycleService _service;

    public ManagedNetworkRecycleServiceTests()
    {
        Directory.CreateDirectory(_sandbox);
        _recycleRoot = Path.Combine(_sandbox, ".MYTC-RecycleBin");
        _service = new ManagedNetworkRecycleService(
            _ => true,
            _ => _recycleRoot);
    }

    [Fact]
    public async Task RecycleAndRestore_FileAndDirectory_RoundTrips()
    {
        var sourceRoot = Directory.CreateDirectory(
            Path.Combine(_sandbox, "source")).FullName;
        var sourceFile = Path.Combine(sourceRoot, "note.txt");
        var sourceDirectory = Directory.CreateDirectory(
            Path.Combine(sourceRoot, "assets")).FullName;
        var nestedFile = Path.Combine(sourceDirectory, "image.txt");
        await File.WriteAllTextAsync(sourceFile, "note");
        await File.WriteAllTextAsync(nestedFile, "image");

        var deleted = await _service.RecycleAsync(
            [sourceFile, sourceDirectory]);

        Assert.Empty(deleted.Failures);
        Assert.Equal(2, deleted.Entries.Count);
        Assert.False(File.Exists(sourceFile));
        Assert.False(Directory.Exists(sourceDirectory));
        Assert.All(
            deleted.Entries,
            entry => Assert.True(
                File.Exists(entry.StoredPath) ||
                Directory.Exists(entry.StoredPath)));

        var restored = await _service.RestoreAsync(deleted.Entries);

        Assert.Empty(restored.Failures);
        Assert.Equal(2, restored.RestoredPaths.Count);
        Assert.Equal("note", await File.ReadAllTextAsync(sourceFile));
        Assert.Equal("image", await File.ReadAllTextAsync(nestedFile));
        Assert.False(Directory.Exists(_recycleRoot));
    }

    [Fact]
    public async Task Restore_WhenOriginalNameExists_KeepsRecycleEntry()
    {
        var sourceRoot = Directory.CreateDirectory(
            Path.Combine(_sandbox, "collision")).FullName;
        var sourceFile = Path.Combine(sourceRoot, "note.txt");
        await File.WriteAllTextAsync(sourceFile, "deleted");
        var deleted = await _service.RecycleAsync([sourceFile]);
        var entry = Assert.Single(deleted.Entries);
        await File.WriteAllTextAsync(sourceFile, "replacement");

        var restored = await _service.RestoreAsync([entry]);

        Assert.Empty(restored.RestoredPaths);
        Assert.Single(restored.Failures);
        Assert.True(File.Exists(entry.StoredPath));
        Assert.Equal("replacement", await File.ReadAllTextAsync(sourceFile));
    }

    [Fact]
    public void RequiresManagedRecycle_UsesClassifier()
    {
        Assert.True(_service.RequiresManagedRecycle(
            Path.Combine(_sandbox, "anything")));
    }

    public void Dispose()
    {
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        var resolved = Path.GetFullPath(_sandbox);
        Assert.StartsWith(
            tempRoot,
            resolved,
            StringComparison.OrdinalIgnoreCase);
        if (Directory.Exists(resolved))
        {
            Directory.Delete(resolved, recursive: true);
        }
    }
}

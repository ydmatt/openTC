using MYTC.Domain.Files;
using MYTC.Infrastructure.Files;

namespace MYTC.Tests.Files;

public sealed class DirectoryListingServiceTests
{
    [Fact]
    public async Task ListAsync_ReturnsFilesAndDirectoriesFromIsolatedSandbox()
    {
        var sandbox = CreateSandbox();

        try
        {
            var folderPath = Directory.CreateDirectory(
                Path.Combine(sandbox, "Folder")).FullName;
            var filePath = Path.Combine(sandbox, "sample.txt");
            await File.WriteAllTextAsync(filePath, "MYTC");
            var service = new DirectoryListingService();

            var entries = await service.ListAsync(sandbox, CancellationToken.None);

            var folder = Assert.Single(entries, entry => entry.FullPath == folderPath);
            var file = Assert.Single(entries, entry => entry.FullPath == filePath);
            Assert.Equal(EntryKind.Directory, folder.Kind);
            Assert.Equal(EntryKind.File, file.Kind);
            Assert.Equal(4, file.Size);
        }
        finally
        {
            DeleteSandbox(sandbox);
        }
    }

    [Fact]
    public async Task ListAsync_MissingDirectoryThrows()
    {
        var sandbox = CreateSandbox();
        DeleteSandbox(sandbox);
        var service = new DirectoryListingService();

        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => service.ListAsync(sandbox, CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_HidesMytcManagedRecycleDirectory()
    {
        var sandbox = CreateSandbox();
        try
        {
            Directory.CreateDirectory(
                Path.Combine(sandbox, ".MYTC-RecycleBin"));
            var service = new DirectoryListingService();

            var entries = await service.ListAsync(
                sandbox,
                CancellationToken.None);

            Assert.DoesNotContain(
                entries,
                entry => StringComparer.OrdinalIgnoreCase.Equals(
                    entry.Name,
                    ".MYTC-RecycleBin"));
        }
        finally
        {
            DeleteSandbox(sandbox);
        }
    }

    private static string CreateSandbox()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "MYTC.Tests");
        Directory.CreateDirectory(testRoot);
        return Directory.CreateDirectory(
            Path.Combine(testRoot, Guid.NewGuid().ToString("N"))).FullName;
    }

    private static void DeleteSandbox(string sandbox)
    {
        var testRoot = Path.GetFullPath(
            Path.Combine(Path.GetTempPath(), "MYTC.Tests")) +
            Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(sandbox);

        if (!resolved.StartsWith(testRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Refusing to delete a path outside the MYTC test root: {resolved}");
        }

        if (Directory.Exists(resolved))
        {
            Directory.Delete(resolved, recursive: true);
        }
    }
}

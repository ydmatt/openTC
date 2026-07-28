using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using MYTC.Application.Updates;
using MYTC.Infrastructure.Updates;

namespace MYTC.Tests.Updates;

public sealed class PortableUpdateTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(
        Path.GetTempPath(),
        "MYTC.Tests",
        Guid.NewGuid().ToString("N"));

    public PortableUpdateTests()
    {
        Directory.CreateDirectory(_sandbox);
    }

    [Fact]
    public async Task Prepare_ValidPackage_ExtractsAndVerifiesAllFiles()
    {
        var archive = await CreatePackageAsync(
            "1.1.0",
            new Dictionary<string, string>
            {
                ["MYTC.exe"] = "new-main",
                ["MYTC.Maintenance.exe"] = "new-maintenance",
                ["runtime.dll"] = "runtime",
            });

        var prepared = await new PortableUpdatePackageService().PrepareAsync(
            archive,
            Path.Combine(_sandbox, "staging"),
            new Version(1, 0, 0));

        Assert.Equal("1.1.0", prepared.Version);
        Assert.Equal(
            "new-main",
            await File.ReadAllTextAsync(
                Path.Combine(prepared.StagedRoot, "MYTC.exe")));
        Assert.Equal(3, prepared.Files.Count);
    }

    [Fact]
    public async Task Prepare_ArchiveTraversal_IsRejectedWithoutWritingOutsideStage()
    {
        var archivePath = Path.Combine(_sandbox, "traversal.zip");
        using (var archive = ZipFile.Open(
                   archivePath,
                   ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../escaped.txt");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("escape");
        }

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new PortableUpdatePackageService().PrepareAsync(
                archivePath,
                Path.Combine(_sandbox, "staging"),
                new Version(1, 0, 0)));
        Assert.False(File.Exists(
            Path.Combine(_sandbox, "escaped.txt")));
    }

    [Fact]
    public async Task Prepare_DataDirectoryInManifest_IsRejected()
    {
        var archive = await CreatePackageAsync(
            "1.1.0",
            new Dictionary<string, string>
            {
                ["MYTC.exe"] = "new-main",
                ["MYTC.Maintenance.exe"] = "new-maintenance",
                [@"data\session.json"] = "must-not-update",
            });

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => new PortableUpdatePackageService().PrepareAsync(
                archive,
                Path.Combine(_sandbox, "staging"),
                new Version(1, 0, 0)));

        Assert.Contains("data", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Apply_ReplacesProgram_PreservesData_AndCreatesBackup()
    {
        var installRoot = Directory.CreateDirectory(
            Path.Combine(_sandbox, "install")).FullName;
        var stagedRoot = Directory.CreateDirectory(
            Path.Combine(_sandbox, "staged")).FullName;
        var dataRoot = Directory.CreateDirectory(
            Path.Combine(installRoot, "data")).FullName;
        await File.WriteAllTextAsync(
            Path.Combine(installRoot, "MYTC.exe"),
            "old-main");
        await File.WriteAllTextAsync(
            Path.Combine(installRoot, "MYTC.Maintenance.exe"),
            "old-maintenance");
        await File.WriteAllTextAsync(
            Path.Combine(dataRoot, "session.json"),
            "user-data");
        await WritePackageDirectoryAsync(
            stagedRoot,
            "1.1.0",
            new Dictionary<string, string>
            {
                ["MYTC.exe"] = "new-main",
                ["MYTC.Maintenance.exe"] = "new-maintenance",
                ["runtime.dll"] = "new-runtime",
            });

        var result = await new PortableUpdateApplyService().ApplyAsync(
            installRoot,
            stagedRoot,
            Path.Combine(_sandbox, "backups"),
            Path.Combine(_sandbox, "logs"));

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(
            "new-main",
            await File.ReadAllTextAsync(
                Path.Combine(installRoot, "MYTC.exe")));
        Assert.Equal(
            "user-data",
            await File.ReadAllTextAsync(
                Path.Combine(dataRoot, "session.json")));
        Assert.Equal(
            "old-main",
            await File.ReadAllTextAsync(
                Path.Combine(result.BackupRoot, "MYTC.exe")));
        Assert.True(File.Exists(result.LogPath));
    }

    [Fact]
    public async Task Apply_WhenDestinationIsLocked_RollsBackEarlierFiles()
    {
        var installRoot = Directory.CreateDirectory(
            Path.Combine(_sandbox, "install-rollback")).FullName;
        var stagedRoot = Directory.CreateDirectory(
            Path.Combine(_sandbox, "staged-rollback")).FullName;
        await File.WriteAllTextAsync(
            Path.Combine(installRoot, "MYTC.exe"),
            "old-main");
        var lockedPath = Path.Combine(
            installRoot,
            "MYTC.Maintenance.exe");
        await File.WriteAllTextAsync(lockedPath, "old-maintenance");
        await WritePackageDirectoryAsync(
            stagedRoot,
            "1.1.0",
            new Dictionary<string, string>
            {
                ["MYTC.exe"] = "new-main",
                ["MYTC.Maintenance.exe"] = "new-maintenance",
            });

        PortableUpdateApplyResult result;
        await using (var lockStream = new FileStream(
                         lockedPath,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.None))
        {
            result = await new PortableUpdateApplyService().ApplyAsync(
                installRoot,
                stagedRoot,
                Path.Combine(_sandbox, "backups-rollback"),
                Path.Combine(_sandbox, "logs-rollback"));
        }

        Assert.False(result.Succeeded);
        Assert.Equal(
            "old-main",
            await File.ReadAllTextAsync(
                Path.Combine(installRoot, "MYTC.exe")));
        Assert.Equal(
            "old-maintenance",
            await File.ReadAllTextAsync(lockedPath));
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

    private async Task<string> CreatePackageAsync(
        string version,
        IReadOnlyDictionary<string, string> files)
    {
        var sourceRoot = Directory.CreateDirectory(
            Path.Combine(_sandbox, $"package-{Guid.NewGuid():N}")).FullName;
        var productRoot = Directory.CreateDirectory(
            Path.Combine(sourceRoot, $"MYTC-v{version}-win-x64")).FullName;
        await WritePackageDirectoryAsync(productRoot, version, files);
        var archivePath = Path.Combine(
            _sandbox,
            $"MYTC-v{version}-{Guid.NewGuid():N}.zip");
        ZipFile.CreateFromDirectory(sourceRoot, archivePath);
        return archivePath;
    }

    private static async Task WritePackageDirectoryAsync(
        string root,
        string version,
        IReadOnlyDictionary<string, string> files)
    {
        var manifestFiles = new List<PortableUpdateFile>();
        foreach (var pair in files)
        {
            var relative = pair.Key.Replace(
                Path.AltDirectorySeparatorChar,
                Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(root, relative);
            Directory.CreateDirectory(
                Path.GetDirectoryName(fullPath) ?? root);
            await File.WriteAllTextAsync(fullPath, pair.Value);
            var bytes = await File.ReadAllBytesAsync(fullPath);
            manifestFiles.Add(new PortableUpdateFile(
                pair.Key.Replace('\\', '/'),
                bytes.LongLength,
                Convert.ToHexString(SHA256.HashData(bytes))));
        }

        var manifest = new PortableUpdateManifest(
            PortableUpdateConstants.CurrentSchemaVersion,
            PortableUpdateConstants.ProductId,
            version,
            "win-x64",
            manifestFiles);
        await File.WriteAllTextAsync(
            Path.Combine(
                root,
                PortableUpdateConstants.ManifestFileName),
            JsonSerializer.Serialize(
                manifest,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                }));
    }
}

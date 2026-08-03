using System.Text.Json;
using MYTC.Domain.Files;
using MYTC.Domain.Workspaces;
using MYTC.Infrastructure.Configuration;

namespace MYTC.Tests.Configuration;

public sealed class JsonWorkspaceStoreTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(
        Path.GetTempPath(),
        "MYTC.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Session_RoundTrips_AllPaneAndTabState()
    {
        var store = new JsonWorkspaceStore(_sandbox);
        var expected = CreateSnapshot("上次会话", @"D:\项目");

        await store.SaveSessionAsync(expected);
        var actual = await store.LoadSessionAsync();

        Assert.NotNull(actual);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.ActivePaneId, actual.ActivePaneId);
        Assert.Equal(expected.Panes[0].Tabs[0].FixedPath, actual.Panes[0].Tabs[0].FixedPath);
        Assert.Equal(expected.Panes[0].Tabs[0].BackHistory, actual.Panes[0].Tabs[0].BackHistory);
    }

    [Fact]
    public async Task Workspace_RoundTripsTaskbarIconKey()
    {
        var store = new JsonWorkspaceStore(_sandbox);
        var expected = CreateSnapshot("work", @"D:\work") with
        {
            IconKey = "W",
        };

        await store.SaveWorkspaceAsync("work", expected);
        var actual = await store.LoadWorkspaceAsync("work");

        Assert.NotNull(actual);
        Assert.Equal(WorkspaceSnapshot.CurrentSchemaVersion, actual.SchemaVersion);
        Assert.Equal("W", actual.IconKey);
    }

    [Fact]
    public async Task Version1Workspace_MigratesToAutomaticIcon()
    {
        var workspaceRoot = Directory.CreateDirectory(
            Path.Combine(_sandbox, "workspaces"));
        var oldSnapshot = CreateSnapshot("work", @"D:\work") with
        {
            SchemaVersion = 1,
            IconKey = null,
        };
        await File.WriteAllTextAsync(
            Path.Combine(workspaceRoot.FullName, "work.json"),
            JsonSerializer.Serialize(oldSnapshot));

        var loaded = await new JsonWorkspaceStore(_sandbox)
            .LoadWorkspaceAsync("work");

        Assert.NotNull(loaded);
        Assert.Equal(WorkspaceSnapshot.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Null(loaded.IconKey);
    }

    [Fact]
    public async Task WorkspaceScopedSessions_DoNotOverwriteEachOther()
    {
        var workStore = new JsonWorkspaceStore(_sandbox, "work");
        var testStore = new JsonWorkspaceStore(_sandbox, "test");
        await workStore.SaveSessionAsync(
            CreateSnapshot("work-session", @"D:\work"));
        await testStore.SaveSessionAsync(
            CreateSnapshot("test-session", @"D:\test"));

        var work = await workStore.LoadSessionAsync();
        var test = await testStore.LoadSessionAsync();

        Assert.Equal("work-session", work?.Name);
        Assert.Equal("test-session", test?.Name);
        Assert.Equal(@"D:\work", work?.Panes[0].Tabs[0].CurrentPath);
        Assert.Equal(@"D:\test", test?.Panes[0].Tabs[0].CurrentPath);
    }

    [Fact]
    public async Task NamedWorkspace_SanitizesFilename_AndListsName()
    {
        var store = new JsonWorkspaceStore(_sandbox);

        await store.SaveWorkspaceAsync("投标:模板", CreateSnapshot("ignored", @"C:\"));
        var names = await store.ListWorkspaceNamesAsync();
        var loaded = await store.LoadWorkspaceAsync("投标:模板");

        Assert.Contains("投标_模板", names);
        Assert.NotNull(loaded);
        Assert.Equal("投标:模板", loaded.Name);
    }

    [Fact]
    public async Task CorruptCurrentSession_FallsBackToLastBackup()
    {
        var store = new JsonWorkspaceStore(_sandbox);
        await store.SaveSessionAsync(CreateSnapshot("first", @"C:\"));
        await store.SaveSessionAsync(CreateSnapshot("second", @"D:\"));
        await File.WriteAllTextAsync(
            Path.Combine(_sandbox, "session.json"),
            "{invalid json");

        var recovered = await store.LoadSessionAsync();

        Assert.NotNull(recovered);
        Assert.Equal("first", recovered.Name);
    }

    [Fact]
    public async Task Workspace_CanBeExportedImportedAndDeleted()
    {
        var sourceStore = new JsonWorkspaceStore(_sandbox);
        var exportPath = Path.Combine(_sandbox, "portable-workspace.json");
        await sourceStore.SaveWorkspaceAsync(
            "work",
            CreateSnapshot("work", @"D:\项目"));

        await sourceStore.ExportWorkspaceAsync("work", exportPath);
        Assert.True(File.Exists(exportPath));

        var importedStore = new JsonWorkspaceStore(
            Path.Combine(_sandbox, "other-data"));
        var importedName = await importedStore.ImportWorkspaceAsync(exportPath);
        var imported = await importedStore.LoadWorkspaceAsync(importedName);
        Assert.Equal("work", importedName);
        Assert.NotNull(imported);
        Assert.Equal(@"D:\项目", imported.Panes[0].Tabs[0].CurrentPath);

        await sourceStore.DeleteWorkspaceAsync("work");
        Assert.Null(await sourceStore.LoadWorkspaceAsync("work"));
        Assert.DoesNotContain(
            "work",
            await sourceStore.ListWorkspaceNamesAsync());
    }

    [Fact]
    public async Task ImportWorkspace_DuplicateNameKeepsExistingWorkspace()
    {
        var store = new JsonWorkspaceStore(_sandbox);
        await store.SaveWorkspaceAsync("work", CreateSnapshot("work", @"C:\"));
        var exportPath = Path.Combine(_sandbox, "work.json");
        await store.ExportWorkspaceAsync("work", exportPath);

        var importedName = await store.ImportWorkspaceAsync(exportPath);

        Assert.Equal("work (2)", importedName);
        Assert.NotNull(await store.LoadWorkspaceAsync("work"));
        Assert.NotNull(await store.LoadWorkspaceAsync("work (2)"));
    }

    [Fact]
    public async Task Workspace_CanBeRenamedWithoutLosingItsContents()
    {
        var store = new JsonWorkspaceStore(_sandbox);
        await store.SaveWorkspaceAsync(
            "work",
            CreateSnapshot("work", @"D:\项目"));

        await store.RenameWorkspaceAsync("work", "投标工作");

        Assert.Null(await store.LoadWorkspaceAsync("work"));
        var renamed = await store.LoadWorkspaceAsync("投标工作");
        Assert.NotNull(renamed);
        Assert.Equal("投标工作", renamed.Name);
        Assert.Equal(@"D:\项目", renamed.Panes[0].Tabs[0].CurrentPath);
        Assert.Contains(
            "投标工作",
            await store.ListWorkspaceNamesAsync());
    }

    [Fact]
    public async Task RenameWorkspace_DoesNotOverwriteAnExistingWorkspace()
    {
        var store = new JsonWorkspaceStore(_sandbox);
        await store.SaveWorkspaceAsync("work", CreateSnapshot("work", @"C:\"));
        await store.SaveWorkspaceAsync("video", CreateSnapshot("video", @"D:\"));

        await Assert.ThrowsAsync<IOException>(
            () => store.RenameWorkspaceAsync("work", "video"));

        Assert.NotNull(await store.LoadWorkspaceAsync("work"));
        Assert.NotNull(await store.LoadWorkspaceAsync("video"));
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

    private static WorkspaceSnapshot CreateSnapshot(string name, string path)
    {
        var tab = new TabSnapshot(
            "tab-1",
            "项目",
            TabMode.Fixed,
            path,
            path,
            [@"C:\older"],
            [],
            SortDescriptor.Default);
        var pane = new PaneSnapshot("top-left", [tab], tab.Id);
        return new WorkspaceSnapshot(
            WorkspaceSnapshot.CurrentSchemaVersion,
            name,
            0.45,
            0.55,
            [pane],
            "top-left",
            "top-right",
            DateTime.UtcNow);
    }
}

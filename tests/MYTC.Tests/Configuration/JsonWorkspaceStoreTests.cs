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

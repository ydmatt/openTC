using MYTC.App.Startup;

namespace MYTC.Tests.Startup;

public sealed class LaunchAndSingleInstanceTests : IDisposable
{
    private readonly string _sandbox = Directory.CreateDirectory(
        Path.Combine(
            Path.GetTempPath(),
            "MYTC.Tests",
            Guid.NewGuid().ToString("N"))).FullName;

    [Fact]
    public void LaunchRequest_OpenOption_NormalizesDirectory()
    {
        var request = LaunchRequest.Parse(
            ["--open", _sandbox]);

        Assert.Equal(
            Path.GetFullPath(_sandbox),
            request.OpenPath);
    }

    [Fact]
    public void LaunchRequest_DataDirectoryOption_IsNotTreatedAsOpenPath()
    {
        var request = LaunchRequest.Parse(
            ["--data-dir", _sandbox]);

        Assert.Null(request.OpenPath);
    }

    [Theory]
    [InlineData("/test", "test")]
    [InlineData("/work", "work")]
    public void LaunchRequest_SlashWorkspace_SelectsWorkspace(
        string argument,
        string expectedWorkspace)
    {
        var request = LaunchRequest.Parse([argument]);

        Assert.Null(request.OpenPath);
        Assert.Equal(expectedWorkspace, request.WorkspaceName);
    }

    [Fact]
    public void LaunchRequest_WorkspaceOption_CanBeCombinedWithOpenPath()
    {
        var request = LaunchRequest.Parse(
            ["--workspace", "work", "--open", _sandbox]);

        Assert.Equal("work", request.WorkspaceName);
        Assert.Equal(Path.GetFullPath(_sandbox), request.OpenPath);
    }

    [Fact]
    public async Task SecondInstance_ForwardsRequestToPrimary()
    {
        using var primary = new SingleInstanceCoordinator(_sandbox);
        using var secondary = new SingleInstanceCoordinator(_sandbox);
        Assert.True(primary.IsPrimary);
        Assert.False(secondary.IsPrimary);

        var received = new TaskCompletionSource<LaunchRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        primary.RequestReceived += request =>
            received.TrySetResult(request);
        primary.StartListening();
        var expected = new LaunchRequest(_sandbox, "test");

        var sent = await secondary.SendAsync(
            expected,
            TimeSpan.FromSeconds(5));
        var actual = await received.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        Assert.True(sent);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DifferentWorkspaceScopes_CanBothBecomePrimary()
    {
        using var work = new SingleInstanceCoordinator(_sandbox, "work");
        using var test = new SingleInstanceCoordinator(_sandbox, "test");

        Assert.True(work.IsPrimary);
        Assert.True(test.IsPrimary);
        Assert.NotEqual(
            SingleInstanceCoordinator.NormalizeWorkspaceScope("work"),
            SingleInstanceCoordinator.NormalizeWorkspaceScope("test"));
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

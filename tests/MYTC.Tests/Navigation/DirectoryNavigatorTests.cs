using MYTC.Application.Navigation;

namespace MYTC.Tests.Navigation;

public sealed class DirectoryNavigatorTests
{
    [Fact]
    public void GetParent_ReturnsContainingDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "MYTC.Tests");
        var child = Path.Combine(root, "Parent", "Child");

        var parent = DirectoryNavigator.GetParent(child);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(root, "Parent")),
            parent,
            ignoreCase: true);
    }

    [Fact]
    public void Normalize_ExpandsAndReturnsAbsolutePath()
    {
        var path = Path.Combine(Path.GetTempPath(), "MYTC.Tests", ".", "Example");

        var normalized = DirectoryNavigator.Normalize(path);

        Assert.True(Path.IsPathFullyQualified(normalized));
        Assert.DoesNotContain($"{Path.DirectorySeparatorChar}.{Path.DirectorySeparatorChar}", normalized);
    }
}

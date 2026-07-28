namespace MYTC.Application.Navigation;

public static class DirectoryNavigator
{
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
    }

    public static string? GetParent(string path)
    {
        var normalized = Normalize(path);
        return Directory.GetParent(normalized)?.FullName;
    }
}

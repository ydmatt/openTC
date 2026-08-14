namespace MYTC.Application.Files;

public static class FileDropGuards
{
    public static string ResolveDropDirectory(
        string currentDirectory,
        string? candidateDirectory)
    {
        var current = Normalize(currentDirectory);
        if (string.IsNullOrWhiteSpace(candidateDirectory))
        {
            return current;
        }

        try
        {
            var candidate = Normalize(candidateDirectory);
            if (!Directory.Exists(candidate) ||
                IsSamePath(candidate, current))
            {
                return current;
            }

            var parent = Path.GetDirectoryName(candidate);
            return parent is not null && IsSamePath(parent, current)
                ? candidate
                : current;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                NotSupportedException or
                PathTooLongException)
        {
            return current;
        }
    }

    public static bool IsSamePath(string left, string right)
    {
        try
        {
            return StringComparer.OrdinalIgnoreCase.Equals(
                Normalize(left),
                Normalize(right));
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                NotSupportedException or
                PathTooLongException)
        {
            return false;
        }
    }

    public static bool IsSameDirectoryDrop(
        string destinationDirectory,
        IReadOnlyList<string> sourcePaths)
    {
        if (string.IsNullOrWhiteSpace(destinationDirectory) ||
            sourcePaths.Count == 0)
        {
            return false;
        }

        try
        {
            var destination = Normalize(destinationDirectory);
            return sourcePaths.All(sourcePath =>
            {
                var normalizedSource = Normalize(sourcePath);
                var parent = Path.GetDirectoryName(normalizedSource);
                return parent is not null &&
                    StringComparer.OrdinalIgnoreCase.Equals(
                        Normalize(parent),
                        destination);
            });
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                NotSupportedException or
                PathTooLongException)
        {
            return false;
        }
    }

    private static string Normalize(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}

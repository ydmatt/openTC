namespace MYTC.Application.Files;

public static class FileDropGuards
{
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

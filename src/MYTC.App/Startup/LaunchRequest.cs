using System.IO;

namespace MYTC.App.Startup;

public sealed record LaunchRequest(string? OpenPath)
{
    public static LaunchRequest Parse(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(
                    arguments[index],
                    "--open") &&
                index + 1 < arguments.Count)
            {
                return new LaunchRequest(
                    NormalizeArgument(arguments[index + 1]));
            }
        }

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (StringComparer.OrdinalIgnoreCase.Equals(
                    argument,
                    "--data-dir"))
            {
                index++;
                continue;
            }

            if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var normalized = NormalizeArgument(argument);
            if (Directory.Exists(normalized))
            {
                return new LaunchRequest(normalized);
            }
        }

        return new LaunchRequest((string?)null);
    }

    private static string NormalizeArgument(string value)
    {
        var trimmed = value.Trim().Trim('"');
        return string.IsNullOrWhiteSpace(trimmed)
            ? string.Empty
            : Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(trimmed));
    }
}

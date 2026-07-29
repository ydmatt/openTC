using System.IO;

namespace MYTC.App.Startup;

public sealed record LaunchRequest(string? OpenPath, string? WorkspaceName)
{
    public static LaunchRequest Parse(IReadOnlyList<string> arguments)
    {
        string? openPath = null;
        string? workspaceName = null;
        for (var index = 0; index < arguments.Count; index++)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(
                    arguments[index],
                    "--open") &&
                index + 1 < arguments.Count)
            {
                openPath = NormalizeArgument(arguments[++index]);
                continue;
            }

            if (StringComparer.OrdinalIgnoreCase.Equals(
                    arguments[index],
                    "--workspace") &&
                index + 1 < arguments.Count)
            {
                workspaceName = NormalizeWorkspaceName(arguments[++index]);
            }
        }

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (StringComparer.OrdinalIgnoreCase.Equals(
                    argument,
                    "--data-dir") ||
                StringComparer.OrdinalIgnoreCase.Equals(
                    argument,
                    "--open") ||
                StringComparer.OrdinalIgnoreCase.Equals(
                    argument,
                    "--workspace"))
            {
                index++;
                continue;
            }

            if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            if (argument.Length > 1 &&
                argument[0] == '/' &&
                !argument.StartsWith("//", StringComparison.Ordinal))
            {
                workspaceName ??= NormalizeWorkspaceName(argument[1..]);
                continue;
            }

            var normalized = NormalizeArgument(argument);
            if (Directory.Exists(normalized))
            {
                openPath ??= normalized;
            }
        }

        return new LaunchRequest(openPath, workspaceName);
    }

    private static string NormalizeArgument(string value)
    {
        var trimmed = value.Trim().Trim('"');
        return string.IsNullOrWhiteSpace(trimmed)
            ? string.Empty
            : Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(trimmed));
    }

    private static string? NormalizeWorkspaceName(string value)
    {
        var trimmed = value.Trim().Trim('"');
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}

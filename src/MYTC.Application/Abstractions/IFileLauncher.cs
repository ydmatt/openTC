namespace MYTC.Application.Abstractions;

public interface IFileLauncher
{
    void Open(string path);

    string? TryResolveShortcutTarget(string path);
}

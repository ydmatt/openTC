namespace MYTC.Application.Abstractions;

public interface IShortcutCreationService
{
    Task<IReadOnlyList<string>> CreateAsync(
        IReadOnlyList<string> sourcePaths,
        string destinationDirectory,
        CancellationToken cancellationToken = default);
}

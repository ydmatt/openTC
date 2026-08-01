namespace MYTC.Application.Abstractions;

/// <summary>
/// Starts a supported archive extractor for a selected archive.
/// </summary>
public interface IArchiveExtractionService
{
    string? FindSuggestedExecutablePath();

    bool CanExtract(string archivePath, string? executablePath);

    Task ExtractToDirectoryAsync(
        string archivePath,
        string destinationDirectory,
        string? executablePath,
        CancellationToken cancellationToken = default);
}

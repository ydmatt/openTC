using MYTC.Domain.Files;

namespace MYTC.Application.Abstractions;

public interface IDirectoryListingService
{
    Task<IReadOnlyList<FileSystemEntry>> ListAsync(
        string path,
        CancellationToken cancellationToken);
}

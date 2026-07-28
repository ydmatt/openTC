using MYTC.Domain.Operations;

namespace MYTC.Application.Abstractions;

public interface IManagedRecycleService
{
    bool RequiresManagedRecycle(string path);

    Task<ManagedRecycleDeleteResult> RecycleAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default);

    Task<RecycleBinRestoreResult> RestoreAsync(
        IReadOnlyList<ManagedRecycleEntry> entries,
        CancellationToken cancellationToken = default);
}

using MYTC.Domain.Operations;

namespace MYTC.Application.Abstractions;

public interface IRecycleBinRestoreService
{
    Task<RecycleBinRestoreResult> RestoreAsync(
        RecycleDeletionBatch deletion,
        CancellationToken cancellationToken = default);
}

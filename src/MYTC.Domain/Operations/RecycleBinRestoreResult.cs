namespace MYTC.Domain.Operations;

public sealed record RecycleDeletionBatch(
    IReadOnlyList<string> OriginalPaths,
    DateTime DeletedAtUtc,
    IReadOnlyList<ManagedRecycleEntry>? ManagedEntries = null);

public sealed record ManagedRecycleEntry(
    string OriginalPath,
    string StoredPath,
    string RecycleRoot);

public sealed record ManagedRecycleDeleteResult(
    IReadOnlyList<ManagedRecycleEntry> Entries,
    IReadOnlyList<FileOperationFailure> Failures);

public sealed record RecycleBinRestoreResult(
    IReadOnlyList<string> RestoredPaths,
    IReadOnlyList<FileOperationFailure> Failures);

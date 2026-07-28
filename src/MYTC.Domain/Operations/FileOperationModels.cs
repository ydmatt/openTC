namespace MYTC.Domain.Operations;

public enum FileOperationKind
{
    Copy,
    Move,
    RecycleDelete,
    PermanentDelete,
}

public enum CollisionBehavior
{
    Skip,
    Replace,
    KeepBoth,
}

public sealed record FileOperationRequest(
    FileOperationKind Kind,
    IReadOnlyList<string> SourcePaths,
    string? DestinationDirectory = null,
    CollisionBehavior CollisionBehavior = CollisionBehavior.Skip);

public sealed record FileOperationProgress(
    int CompletedSources,
    int TotalSources,
    string CurrentPath);

public sealed record FileOperationFailure(
    string Path,
    string Message);

public sealed record FileOperationResult(
    int CompletedCount,
    int SkippedCount,
    IReadOnlyList<FileOperationFailure> Failures,
    bool WasCancelled)
{
    public bool Succeeded => !WasCancelled && Failures.Count == 0;
}

using MYTC.Domain.Workspaces;

namespace MYTC.Application.Abstractions;

public interface IWorkspaceStore
{
    Task<WorkspaceSnapshot?> LoadSessionAsync(CancellationToken cancellationToken = default);

    Task SaveSessionAsync(
        WorkspaceSnapshot snapshot,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListWorkspaceNamesAsync(
        CancellationToken cancellationToken = default);

    Task<WorkspaceSnapshot?> LoadWorkspaceAsync(
        string name,
        CancellationToken cancellationToken = default);

    Task SaveWorkspaceAsync(
        string name,
        WorkspaceSnapshot snapshot,
        CancellationToken cancellationToken = default);

    Task DeleteWorkspaceAsync(
        string name,
        CancellationToken cancellationToken = default);

    Task ExportWorkspaceAsync(
        string name,
        string destinationPath,
        CancellationToken cancellationToken = default);

    Task<string> ImportWorkspaceAsync(
        string sourcePath,
        CancellationToken cancellationToken = default);
}

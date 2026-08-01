using MYTC.Domain.Operations;

namespace MYTC.Application.Abstractions;

public interface IFileOperationService
{
    Task<FileOperationResult> ExecuteAsync(
        FileOperationRequest request,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<string> CreateDirectoryAsync(
        string parentDirectory,
        string requestedName,
        CancellationToken cancellationToken = default);

    string GetNewTextDocumentDefaultName();

    Task<string> CreateTextDocumentAsync(
        string parentDirectory,
        string requestedName,
        CancellationToken cancellationToken = default);

    Task<string> RenameAsync(
        string sourcePath,
        string requestedName,
        CancellationToken cancellationToken = default);
}

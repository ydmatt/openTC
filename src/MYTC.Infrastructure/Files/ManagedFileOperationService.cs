using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text;
using MYTC.Application.Abstractions;
using MYTC.Domain.Operations;

namespace MYTC.Infrastructure.Files;

public sealed class ManagedFileOperationService : IFileOperationService
{
    public Task<FileOperationResult> ExecuteAsync(
        FileOperationRequest request,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        return Task.Run(
            () => ExecuteCore(request, progress, cancellationToken),
            cancellationToken);
    }

    public Task<string> CreateDirectoryAsync(
        string parentDirectory,
        string requestedName,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var parent = RequireExistingDirectory(parentDirectory);
                var name = ValidateLeafName(requestedName);
                var destination = Path.Combine(parent, name);
                if (File.Exists(destination) || Directory.Exists(destination))
                {
                    throw new IOException($"“{name}”已经存在。");
                }

                return Directory.CreateDirectory(destination).FullName;
            },
            cancellationToken);
    }

    public string GetNewTextDocumentDefaultName()
    {
        return ReadTextDocumentShellNew().DisplayName + ".txt";
    }

    public Task<string> CreateTextDocumentAsync(
        string parentDirectory,
        string requestedName,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var parent = RequireExistingDirectory(parentDirectory);
                var name = ValidateLeafName(requestedName);
                var destination = Path.Combine(parent, name);
                if (PathExists(destination))
                {
                    throw new IOException($"“{name}”已经存在。");
                }

                var shellNew = ReadTextDocumentShellNew();
                if (Path.GetExtension(name).Equals(".txt", StringComparison.OrdinalIgnoreCase) &&
                    shellNew.TemplateFile is { } templateFile)
                {
                    File.Copy(templateFile, destination);
                }
                else if (Path.GetExtension(name).Equals(".txt", StringComparison.OrdinalIgnoreCase) &&
                    shellNew.TemplateData is { } templateData)
                {
                    File.WriteAllBytes(destination, templateData);
                }
                else
                {
                    File.WriteAllBytes(destination, []);
                }

                return destination;
            },
            cancellationToken);
    }

    public Task<string> RenameAsync(
        string sourcePath,
        string requestedName,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = Path.GetFullPath(sourcePath);
                var parent = Path.GetDirectoryName(source)
                    ?? throw new InvalidOperationException("无法确定项目的上级目录。");
                var destination = Path.Combine(parent, ValidateLeafName(requestedName));
                if (File.Exists(destination) || Directory.Exists(destination))
                {
                    throw new IOException($"“{Path.GetFileName(destination)}”已经存在。");
                }

                if (File.Exists(source))
                {
                    File.Move(source, destination);
                }
                else if (Directory.Exists(source))
                {
                    Directory.Move(source, destination);
                }
                else
                {
                    throw new FileNotFoundException("要重命名的项目不存在。", source);
                }

                return destination;
            },
            cancellationToken);
    }

    private static FileOperationResult ExecuteCore(
        FileOperationRequest request,
        IProgress<FileOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var failures = new List<FileOperationFailure>();
        var completed = 0;
        var skipped = 0;
        var total = request.SourcePaths.Count;
        var destinationRoot = request.DestinationDirectory is null
            ? null
            : RequireExistingDirectory(request.DestinationDirectory);

        for (var index = 0; index < total; index++)
        {
            var source = Path.GetFullPath(request.SourcePaths[index]);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new FileOperationProgress(index, total, source));

                var outcome = request.Kind switch
                {
                    FileOperationKind.Copy => CopyOrMove(
                        source,
                        destinationRoot!,
                        move: false,
                        request.CollisionBehavior,
                        cancellationToken),
                    FileOperationKind.Move => CopyOrMove(
                        source,
                        destinationRoot!,
                        move: true,
                        request.CollisionBehavior,
                        cancellationToken),
                    FileOperationKind.RecycleDelete => Recycle(source),
                    FileOperationKind.PermanentDelete => DeletePermanent(source),
                    _ => throw new ArgumentOutOfRangeException(nameof(request)),
                };

                if (outcome)
                {
                    completed++;
                }
                else
                {
                    skipped++;
                }
            }
            catch (OperationCanceledException)
            {
                return new FileOperationResult(completed, skipped, failures, true);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                InvalidOperationException or
                NotSupportedException)
            {
                failures.Add(new FileOperationFailure(source, exception.Message));
            }
        }

        progress?.Report(new FileOperationProgress(total, total, string.Empty));
        return new FileOperationResult(completed, skipped, failures, false);
    }

    private static bool CopyOrMove(
        string source,
        string destinationRoot,
        bool move,
        CollisionBehavior collisionBehavior,
        CancellationToken cancellationToken)
    {
        var sourceName = Path.GetFileName(
            source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            throw new InvalidOperationException("不支持直接复制或移动驱动器根目录。");
        }

        var destination = Path.Combine(destinationRoot, sourceName);
        if (PathsEqual(source, destination))
        {
            if (move ||
                collisionBehavior != CollisionBehavior.KeepBoth)
            {
                return false;
            }

            destination = FindAvailableName(destination);
        }

        if (Directory.Exists(source) && IsDescendant(destination, source))
        {
            throw new InvalidOperationException("不能把文件夹复制或移动到它自身内部。");
        }

        if (PathExists(destination))
        {
            if (collisionBehavior == CollisionBehavior.Skip)
            {
                return false;
            }

            if (collisionBehavior == CollisionBehavior.KeepBoth)
            {
                destination = FindAvailableName(destination);
            }
        }

        if (File.Exists(source))
        {
            PrepareDestinationForFile(destination, collisionBehavior);
            if (move)
            {
                File.Move(source, destination, overwrite: collisionBehavior == CollisionBehavior.Replace);
            }
            else
            {
                File.Copy(source, destination, overwrite: collisionBehavior == CollisionBehavior.Replace);
            }

            return true;
        }

        if (!Directory.Exists(source))
        {
            throw new FileNotFoundException("源项目不存在。", source);
        }

        PrepareDestinationForDirectory(destination, collisionBehavior);
        if (move && !Directory.Exists(destination))
        {
            try
            {
                Directory.Move(source, destination);
                return true;
            }
            catch (IOException)
            {
                // Cross-volume moves and directory merges use copy-then-delete.
            }
        }

        CopyDirectoryContents(
            source,
            destination,
            collisionBehavior,
            cancellationToken);
        if (move)
        {
            Directory.Delete(source, recursive: true);
        }

        return true;
    }

    private static void CopyDirectoryContents(
        string source,
        string destination,
        CollisionBehavior collisionBehavior,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var childDestination = Path.Combine(destination, Path.GetFileName(directory));
            if (File.Exists(childDestination))
            {
                if (collisionBehavior == CollisionBehavior.Skip)
                {
                    continue;
                }

                File.Delete(childDestination);
            }

            if (Directory.Exists(childDestination) &&
                collisionBehavior == CollisionBehavior.KeepBoth)
            {
                childDestination = FindAvailableName(childDestination);
            }

            CopyDirectoryContents(
                directory,
                childDestination,
                collisionBehavior,
                cancellationToken);
        }

        foreach (var file in Directory.EnumerateFiles(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var childDestination = Path.Combine(destination, Path.GetFileName(file));
            if (Directory.Exists(childDestination))
            {
                if (collisionBehavior == CollisionBehavior.Skip)
                {
                    continue;
                }

                Directory.Delete(childDestination, recursive: true);
            }

            if (File.Exists(childDestination) &&
                collisionBehavior == CollisionBehavior.KeepBoth)
            {
                childDestination = FindAvailableName(childDestination);
            }

            File.Copy(
                file,
                childDestination,
                overwrite: collisionBehavior == CollisionBehavior.Replace);
        }
    }

    private static bool Recycle(string path)
    {
        if (File.Exists(path))
        {
            FileSystem.DeleteFile(
                path,
                UIOption.OnlyErrorDialogs,
                RecycleOption.SendToRecycleBin,
                UICancelOption.ThrowException);
            return true;
        }

        if (Directory.Exists(path))
        {
            FileSystem.DeleteDirectory(
                path,
                UIOption.OnlyErrorDialogs,
                RecycleOption.SendToRecycleBin,
                UICancelOption.ThrowException);
            return true;
        }

        throw new FileNotFoundException("要删除的项目不存在。", path);
    }

    private static bool DeletePermanent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
            return true;
        }

        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
            return true;
        }

        throw new FileNotFoundException("要删除的项目不存在。", path);
    }

    private static void PrepareDestinationForFile(
        string destination,
        CollisionBehavior collisionBehavior)
    {
        if (Directory.Exists(destination))
        {
            if (collisionBehavior != CollisionBehavior.Replace)
            {
                throw new IOException("目标位置存在同名文件夹。");
            }

            Directory.Delete(destination, recursive: true);
        }
    }

    private static void PrepareDestinationForDirectory(
        string destination,
        CollisionBehavior collisionBehavior)
    {
        if (Directory.Exists(destination))
        {
            if (collisionBehavior != CollisionBehavior.Replace)
            {
                throw new IOException("目标位置存在同名文件夹。");
            }

            Directory.Delete(destination, recursive: true);
        }

        if (File.Exists(destination))
        {
            if (collisionBehavior != CollisionBehavior.Replace)
            {
                throw new IOException("目标位置存在同名文件。");
            }

            File.Delete(destination);
        }
    }

    private static string FindAvailableName(string originalPath)
    {
        var parent = Path.GetDirectoryName(originalPath)
            ?? throw new InvalidOperationException("无法确定目标目录。");
        var extension = File.Exists(originalPath) || Path.HasExtension(originalPath)
            ? Path.GetExtension(originalPath)
            : string.Empty;
        var name = Path.GetFileNameWithoutExtension(originalPath);

        for (var index = 2; index < 10_000; index++)
        {
            var candidate = Path.Combine(parent, $"{name} ({index}){extension}");
            if (!PathExists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("无法生成不冲突的目标名称。");
    }

    private static string FindAvailableFileName(
        string parentDirectory,
        string preferredName)
    {
        var safeName = SanitizeTextDocumentName(preferredName);
        var initialPath = Path.Combine(parentDirectory, safeName);
        if (!PathExists(initialPath))
        {
            return initialPath;
        }

        var name = Path.GetFileNameWithoutExtension(safeName);
        var extension = Path.GetExtension(safeName);
        for (var index = 2; index < 10_000; index++)
        {
            var candidate = Path.Combine(
                parentDirectory,
                $"{name} ({index}){extension}");
            if (!PathExists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("无法生成不冲突的文本文档名称。");
    }

    private static TextDocumentShellNew ReadTextDocumentShellNew()
    {
        const string fallbackName = "新建文本文档";
        try
        {
            using var shellNew = Registry.ClassesRoot.OpenSubKey(
                @".txt\ShellNew");
            if (shellNew is null)
            {
                return new TextDocumentShellNew(fallbackName, null, null);
            }

            var itemName = shellNew.GetValue("ItemName") as string;
            var displayName = ResolveIndirectString(itemName) ?? fallbackName;
            var templateData = shellNew.GetValue("Data") as byte[];
            var templateFile = ResolveShellNewTemplate(
                shellNew.GetValue("FileName") as string);
            return new TextDocumentShellNew(
                SanitizeTextDocumentName(displayName, includeExtension: false),
                templateFile,
                templateData);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException)
        {
            return new TextDocumentShellNew(fallbackName, null, null);
        }
    }

    private static string? ResolveShellNewTemplate(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(fileName);
        var resolved = Path.IsPathFullyQualified(expanded)
            ? expanded
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "ShellNew",
                expanded);
        return File.Exists(resolved) ? resolved : null;
    }

    private static string? ResolveIndirectString(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        var buffer = new StringBuilder(32_768);
        return SHLoadIndirectString(source, buffer, (uint)buffer.Capacity, 0) == 0 &&
            !string.IsNullOrWhiteSpace(buffer.ToString())
            ? buffer.ToString()
            : source.StartsWith('@') ? null : source;
    }

    private static string SanitizeTextDocumentName(
        string value,
        bool includeExtension = true)
    {
        var fileName = Path.GetFileName(value.Trim());
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName
            .Where(character => !invalid.Contains(character))
            .ToArray())
            .Trim()
            .TrimEnd('.', ' ');
        if (!includeExtension && Path.HasExtension(sanitized))
        {
            sanitized = Path.GetFileNameWithoutExtension(sanitized);
        }

        return string.IsNullOrWhiteSpace(sanitized) || sanitized is "." or ".."
            ? "新建文本文档"
            : sanitized;
    }

    private sealed record TextDocumentShellNew(
        string DisplayName,
        string? TemplateFile,
        byte[]? TemplateData);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHLoadIndirectString(
        string source,
        StringBuilder buffer,
        uint bufferLength,
        nint reserved);

    private static bool IsDescendant(string candidate, string parent)
    {
        var relative = Path.GetRelativePath(
            Path.GetFullPath(parent),
            Path.GetFullPath(candidate));
        return !relative.StartsWith("..", StringComparison.Ordinal) &&
            !Path.IsPathRooted(relative);
    }

    private static bool PathsEqual(string left, string right)
    {
        return StringComparer.OrdinalIgnoreCase.Equals(
            Path.GetFullPath(left).TrimEnd('\\', '/'),
            Path.GetFullPath(right).TrimEnd('\\', '/'));
    }

    private static bool PathExists(string path) =>
        File.Exists(path) || Directory.Exists(path);

    private static string RequireExistingDirectory(string path)
    {
        var resolved = Path.GetFullPath(path);
        if (!Directory.Exists(resolved))
        {
            throw new DirectoryNotFoundException($"目标目录不存在：{resolved}");
        }

        return resolved;
    }

    private static string ValidateLeafName(string requestedName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedName);
        var name = requestedName.Trim();
        if (name is "." or ".." ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            !StringComparer.Ordinal.Equals(name, Path.GetFileName(name)))
        {
            throw new ArgumentException("名称包含无效字符。", nameof(requestedName));
        }

        return name;
    }

    private static void ValidateRequest(FileOperationRequest request)
    {
        if (request.SourcePaths.Count == 0)
        {
            throw new ArgumentException("没有选择任何项目。", nameof(request));
        }

        if (request.Kind is FileOperationKind.Copy or FileOperationKind.Move &&
            string.IsNullOrWhiteSpace(request.DestinationDirectory))
        {
            throw new ArgumentException("复制或移动操作需要目标目录。", nameof(request));
        }
    }
}

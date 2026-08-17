using System.Globalization;
using System.Runtime.InteropServices;
using MYTC.Application.Abstractions;
using MYTC.Domain.Operations;

namespace MYTC.Infrastructure.Files;

public sealed class ShellRecycleBinRestoreService : IRecycleBinRestoreService
{
    private const int RecycleBinNamespace = 10;

    public Task<RecycleBinRestoreResult> RestoreAsync(
        RecycleDeletionBatch deletion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deletion);
        var completion =
            new TaskCompletionSource<RecycleBinRestoreResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.TrySetResult(RestoreCore(deletion, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "openTC Recycle Bin Restore",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static RecycleBinRestoreResult RestoreCore(
        RecycleDeletionBatch deletion,
        CancellationToken cancellationToken)
    {
        var restored = new List<string>();
        var failures = new List<FileOperationFailure>();
        object? shellObject = null;
        object? recycleFolderObject = null;

        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application")
                ?? throw new InvalidOperationException(
                    "当前系统不支持访问 Windows 回收站。");
            shellObject = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException(
                    "无法启动 Windows Shell 服务。");
            dynamic shell = shellObject;
            recycleFolderObject = shell.NameSpace(RecycleBinNamespace);
            if (recycleFolderObject is null)
            {
                throw new InvalidOperationException("无法打开 Windows 回收站。");
            }

            foreach (var originalPath in deletion.OriginalPaths
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    RestoreOne(
                        recycleFolderObject,
                        originalPath,
                        deletion.DeletedAtUtc,
                        cancellationToken);
                    restored.Add(originalPath);
                }
                catch (Exception exception) when (
                    exception is not OperationCanceledException)
                {
                    failures.Add(new FileOperationFailure(
                        originalPath,
                        exception.Message));
                }
            }
        }
        finally
        {
            ReleaseComObject(recycleFolderObject);
            ReleaseComObject(shellObject);
        }

        return new RecycleBinRestoreResult(restored, failures);
    }

    private static void RestoreOne(
        object recycleFolderObject,
        string originalPath,
        DateTime deletedAtUtc,
        CancellationToken cancellationToken)
    {
        var normalizedPath = Path.GetFullPath(originalPath);
        if (File.Exists(normalizedPath) || Directory.Exists(normalizedPath))
        {
            throw new IOException("原位置已有同名项目，未执行撤销。");
        }

        var trimmedPath = Path.TrimEndingDirectorySeparator(normalizedPath);
        var expectedName = Path.GetFileName(trimmedPath);
        var expectedParent = Path.GetDirectoryName(trimmedPath) ?? string.Empty;
        object? matchedItem = null;
        DateTime? matchedDeletedAt = null;
        object? itemsObject = null;

        try
        {
            dynamic recycleFolder = recycleFolderObject;
            itemsObject = recycleFolder.Items();
            dynamic items = itemsObject;
            var count = Convert.ToInt32(items.Count, CultureInfo.InvariantCulture);
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                object? itemObject = null;
                try
                {
                    itemObject = items.Item(index);
                    if (itemObject is null)
                    {
                        continue;
                    }

                    dynamic item = itemObject;
                    var name = Convert.ToString(
                        item.ExtendedProperty("System.FileName"),
                        CultureInfo.CurrentCulture);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = Convert.ToString(
                            item.Name,
                            CultureInfo.CurrentCulture);
                    }
                    var deletedFrom = Convert.ToString(
                        item.ExtendedProperty("System.Recycle.DeletedFrom"),
                        CultureInfo.CurrentCulture);
                    if (!StringComparer.OrdinalIgnoreCase.Equals(
                            name,
                            expectedName) ||
                        !PathsEqual(deletedFrom, expectedParent))
                    {
                        continue;
                    }

                    var dateDeleted = ToUtcDateTime(
                        item.ExtendedProperty("System.Recycle.DateDeleted"));
                    if (dateDeleted is null ||
                        dateDeleted < deletedAtUtc.AddSeconds(-10) ||
                        matchedDeletedAt is not null &&
                        dateDeleted <= matchedDeletedAt)
                    {
                        continue;
                    }

                    ReleaseComObject(matchedItem);
                    matchedItem = itemObject;
                    itemObject = null;
                    matchedDeletedAt = dateDeleted;
                }
                finally
                {
                    ReleaseComObject(itemObject);
                }
            }
        }
        finally
        {
            ReleaseComObject(itemsObject);
        }

        if (matchedItem is null)
        {
            throw new FileNotFoundException(
                "回收站中找不到本次删除的对应项目。");
        }

        try
        {
            InvokeRestoreVerb(matchedItem);
        }
        finally
        {
            ReleaseComObject(matchedItem);
        }

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(normalizedPath) || Directory.Exists(normalizedPath))
            {
                return;
            }

            Thread.Sleep(50);
        }

        throw new IOException("Windows 已接收还原命令，但原位置尚未出现该项目。");
    }

    private static void InvokeRestoreVerb(object itemObject)
    {
        object? verbsObject = null;
        try
        {
            dynamic item = itemObject;
            verbsObject = item.Verbs();
            dynamic verbs = verbsObject;
            var count = Convert.ToInt32(verbs.Count, CultureInfo.InvariantCulture);
            for (var index = 0; index < count; index++)
            {
                object? verbObject = null;
                try
                {
                    verbObject = verbs.Item(index);
                    if (verbObject is null)
                    {
                        continue;
                    }

                    dynamic verb = verbObject;
                    var name = NormalizeVerbName(Convert.ToString(
                        verb.Name,
                        CultureInfo.CurrentCulture));
                    if (!IsRestoreVerb(name))
                    {
                        continue;
                    }

                    verb.DoIt();
                    return;
                }
                finally
                {
                    ReleaseComObject(verbObject);
                }
            }
        }
        finally
        {
            ReleaseComObject(verbsObject);
        }

        throw new InvalidOperationException(
            "Windows 回收站未提供“还原”命令。");
    }

    private static bool IsRestoreVerb(string value)
    {
        return value.Contains("还原", StringComparison.CurrentCultureIgnoreCase) ||
            value.Contains("還原", StringComparison.CurrentCultureIgnoreCase) ||
            value.Contains("restore", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("undelete", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVerbName(string? value)
    {
        return (value ?? string.Empty)
            .Replace("&", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private static bool PathsEqual(string? first, string second)
    {
        if (string.IsNullOrWhiteSpace(first))
        {
            return false;
        }

        try
        {
            return StringComparer.OrdinalIgnoreCase.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)));
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                NotSupportedException or
                PathTooLongException)
        {
            return false;
        }
    }

    private static DateTime? ToUtcDateTime(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is DateTime dateTime)
        {
            return dateTime.Kind switch
            {
                DateTimeKind.Utc => dateTime,
                DateTimeKind.Local => dateTime.ToUniversalTime(),
                _ => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc),
            };
        }

        return DateTime.TryParse(
            Convert.ToString(value, CultureInfo.CurrentCulture),
            CultureInfo.CurrentCulture,
            DateTimeStyles.AssumeUniversal |
                DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }
}

using System.IO;

namespace MYTC.Application.Updates;

public static class InstallationPathPolicy
{
    public static bool IsSupportedFixedLocalPath(
        string path,
        out string reason)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            reason = "程序目录为空。";
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            reason = $"程序目录无效：{exception.Message}";
            return false;
        }

        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            reason = "程序位于网络共享路径。请先复制到本机固定磁盘，例如 E:\\port\\MYTC。";
            return false;
        }

        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            reason = "无法识别程序所在磁盘。";
            return false;
        }

        try
        {
            var drive = new DriveInfo(root);
            if (drive.DriveType != DriveType.Fixed)
            {
                reason = $"程序所在磁盘类型为“{drive.DriveType}”；生产接管只允许本机固定磁盘。";
                return false;
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            IOException or
            UnauthorizedAccessException)
        {
            reason = $"无法读取程序所在磁盘：{exception.Message}";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}

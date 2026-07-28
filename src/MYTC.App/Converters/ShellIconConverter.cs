using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MYTC.Domain.Files;

namespace MYTC.App.Converters;

public sealed class ShellIconConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, ImageSource> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        if (value is not FileSystemEntry entry)
        {
            return null;
        }

        var cacheKey = CreateCacheKey(entry);
        return Cache.GetOrAdd(cacheKey, _ => LoadIcon(entry));
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static string CreateCacheKey(FileSystemEntry entry)
    {
        if (entry.Kind == EntryKind.Directory)
        {
            return "<folder>";
        }

        var extension = Path.GetExtension(entry.Name).ToLowerInvariant();
        return extension is ".exe" or ".lnk" or ".ico" or ".cur"
            ? entry.FullPath
            : string.IsNullOrWhiteSpace(extension)
                ? "<file>"
                : extension;
    }

    private static ImageSource LoadIcon(FileSystemEntry entry)
    {
        var isDirectory = entry.Kind == EntryKind.Directory;
        var extension = Path.GetExtension(entry.Name).ToLowerInvariant();
        var requiresActualFile =
            !isDirectory &&
            extension is ".exe" or ".lnk" or ".ico" or ".cur";
        var queryPath = requiresActualFile
            ? entry.FullPath
            : isDirectory
                ? "folder"
                : "file" + extension;
        var attributes = isDirectory
            ? FileAttributeDirectory
            : FileAttributeNormal;
        var flags = ShgfiIcon | ShgfiSmallIcon |
            (requiresActualFile ? 0u : ShgfiUseFileAttributes);

        var result = SHGetFileInfo(
            queryPath,
            attributes,
            out var fileInfo,
            (uint)Marshal.SizeOf<ShellFileInfo>(),
            flags);
        if (result == IntPtr.Zero || fileInfo.IconHandle == IntPtr.Zero)
        {
            return CreateFallbackIcon(isDirectory);
        }

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                fileInfo.IconHandle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(16, 16));
            source.Freeze();
            return source;
        }
        finally
        {
            _ = DestroyIcon(fileInfo.IconHandle);
        }
    }

    private static ImageSource CreateFallbackIcon(bool isDirectory)
    {
        var drawing = new GeometryDrawing(
            isDirectory ? Brushes.Goldenrod : Brushes.SlateGray,
            null,
            isDirectory
                ? Geometry.Parse("M1,4 L6,4 8,6 15,6 15,14 1,14 Z")
                : Geometry.Parse("M3,1 L11,1 15,5 15,15 3,15 Z"));
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }

    private const uint FileAttributeDirectory = 0x10;
    private const uint FileAttributeNormal = 0x80;
    private const uint ShgfiIcon = 0x100;
    private const uint ShgfiSmallIcon = 0x1;
    private const uint ShgfiUseFileAttributes = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr IconHandle;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string path,
        uint fileAttributes,
        out ShellFileInfo fileInfo,
        uint fileInfoSize,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr iconHandle);
}

namespace MYTC.Domain.Files;

public sealed record FileSystemEntry(
    string FullPath,
    string Name,
    EntryKind Kind,
    DateTime ModifiedAt,
    string TypeDisplayName,
    long? Size)
{
    public string IconGlyph => Kind == EntryKind.Directory ? "📁" : "📄";

    public string SizeDisplay
    {
        get
        {
            if (Kind == EntryKind.Directory || Size is null)
            {
                return string.Empty;
            }

            var bytes = Size.Value;
            if (bytes < 1024)
            {
                return $"{bytes:N0} B";
            }

            if (bytes < 1024 * 1024)
            {
                return $"{bytes / 1024d:N0} KB";
            }

            if (bytes < 1024L * 1024 * 1024)
            {
                return $"{bytes / 1024d / 1024:N1} MB";
            }

            return $"{bytes / 1024d / 1024 / 1024:N1} GB";
        }
    }
}

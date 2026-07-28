namespace MYTC.Application.Updates;

public static class PortableUpdateConstants
{
    public const int CurrentSchemaVersion = 1;
    public const string ProductId = "MYTC";
    public const string ManifestFileName = "MYTC.update.json";
    public const string MaintenanceExecutableName = "MYTC.Maintenance.exe";
    public const string MainExecutableName = "MYTC.exe";
}

public sealed record PortableUpdateManifest(
    int SchemaVersion,
    string ProductId,
    string Version,
    string Architecture,
    IReadOnlyList<PortableUpdateFile> Files);

public sealed record PortableUpdateFile(
    string Path,
    long Length,
    string Sha256);

public sealed record PreparedPortableUpdate(
    string ArchivePath,
    string StagedRoot,
    string Version,
    IReadOnlyList<PortableUpdateFile> Files);

public sealed record PortableUpdateApplyResult(
    bool Succeeded,
    string? PreviousVersion,
    string NewVersion,
    string BackupRoot,
    string LogPath,
    string? ErrorMessage);

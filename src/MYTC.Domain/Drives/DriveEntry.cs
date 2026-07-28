namespace MYTC.Domain.Drives;

public sealed record DriveEntry(
    string RootPath,
    string DisplayName,
    DriveTypeKind Kind,
    bool IsReady);

public enum DriveTypeKind
{
    Unknown,
    Fixed,
    Removable,
    Network,
    Optical,
    Ram,
}

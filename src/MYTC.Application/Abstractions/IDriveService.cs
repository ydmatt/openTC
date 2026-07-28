using MYTC.Domain.Drives;

namespace MYTC.Application.Abstractions;

public interface IDriveService
{
    IReadOnlyList<DriveEntry> GetDrives();
}

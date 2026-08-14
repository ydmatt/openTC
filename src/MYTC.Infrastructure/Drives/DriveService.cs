using MYTC.Application.Abstractions;
using MYTC.Domain.Drives;

namespace MYTC.Infrastructure.Drives;

public sealed class DriveService : IDriveService
{
    public IReadOnlyList<DriveEntry> GetDrives()
    {
        var drives = new List<DriveEntry>();

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                var isReady = drive.IsReady;
                long? totalSize = null;
                long? availableFreeSpace = null;
                if (isReady)
                {
                    try
                    {
                        totalSize = drive.TotalSize;
                        availableFreeSpace = drive.AvailableFreeSpace;
                    }
                    catch (IOException)
                    {
                        // Keep the drive selectable even if capacity is unavailable.
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Keep the drive selectable even if capacity is unavailable.
                    }
                }

                var label = isReady && !string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? $" {drive.VolumeLabel}"
                    : string.Empty;

                drives.Add(new DriveEntry(
                    drive.RootDirectory.FullName,
                    $"{drive.Name.TrimEnd('\\')}{label}",
                    MapKind(drive.DriveType),
                    isReady,
                    totalSize,
                    availableFreeSpace));
            }
            catch (IOException)
            {
                drives.Add(new DriveEntry(
                    drive.Name,
                    drive.Name.TrimEnd('\\'),
                    MapKind(drive.DriveType),
                    false));
            }
            catch (UnauthorizedAccessException)
            {
                drives.Add(new DriveEntry(
                    drive.Name,
                    drive.Name.TrimEnd('\\'),
                    MapKind(drive.DriveType),
                    false));
            }
        }

        return drives
            .OrderBy(drive => drive.RootPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DriveTypeKind MapKind(DriveType type)
    {
        return type switch
        {
            DriveType.Fixed => DriveTypeKind.Fixed,
            DriveType.Removable => DriveTypeKind.Removable,
            DriveType.Network => DriveTypeKind.Network,
            DriveType.CDRom => DriveTypeKind.Optical,
            DriveType.Ram => DriveTypeKind.Ram,
            _ => DriveTypeKind.Unknown,
        };
    }
}

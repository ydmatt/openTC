using MYTC.Domain.Drives;

namespace MYTC.Tests.Drives;

public sealed class DriveEntryTests
{
    [Fact]
    public void CapacityDisplay_ReportsUsageAndLowFreeSpace()
    {
        var drive = new DriveEntry(
            "T:\\",
            "T:",
            DriveTypeKind.Network,
            true,
            TotalSize: 100,
            AvailableFreeSpace: 9);

        Assert.True(drive.HasCapacity);
        Assert.Equal(91d, drive.UsedPercentage, precision: 3);
        Assert.True(drive.IsLowFreeSpace);
        Assert.Equal("9 B 可用 / 100 B", drive.CapacityDisplay);
        Assert.Contains("已用 91%", drive.UsageTooltip);
    }

    [Fact]
    public void CapacityDisplay_HandlesUnavailableCapacity()
    {
        var drive = new DriveEntry(
            "Y:\\",
            "Y:",
            DriveTypeKind.Network,
            true);

        Assert.False(drive.HasCapacity);
        Assert.Equal("容量未知", drive.CapacityDisplay);
        Assert.Equal(0d, drive.UsedPercentage);
        Assert.False(drive.IsLowFreeSpace);
    }
}

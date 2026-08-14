namespace MYTC.Domain.Drives;

public sealed record DriveEntry(
    string RootPath,
    string DisplayName,
    DriveTypeKind Kind,
    bool IsReady,
    long? TotalSize = null,
    long? AvailableFreeSpace = null)
{
    public bool HasCapacity =>
        TotalSize is > 0 && AvailableFreeSpace is >= 0;

    public double UsedPercentage
    {
        get
        {
            if (!HasCapacity)
            {
                return 0;
            }

            var used = TotalSize!.Value -
                Math.Min(AvailableFreeSpace!.Value, TotalSize.Value);
            return Math.Clamp(used * 100d / TotalSize.Value, 0d, 100d);
        }
    }

    public bool IsLowFreeSpace =>
        HasCapacity &&
        AvailableFreeSpace!.Value * 100d / TotalSize!.Value < 10d;

    public string CapacityDisplay => HasCapacity
        ? $"{FormatBytes(AvailableFreeSpace!.Value)} 可用 / {FormatBytes(TotalSize!.Value)}"
        : IsReady ? "容量未知" : "不可用";

    public string UsageTooltip => HasCapacity
        ? $"已用 {UsedPercentage:0.#}% · {CapacityDisplay}"
        : CapacityDisplay;

    private static string FormatBytes(long bytes)
    {
        const double unit = 1024d;
        var value = Math.Max(0, bytes);
        return value switch
        {
            < 1024 => $"{value:N0} B",
            < 1024 * 1024 => $"{value / unit:0.#} KB",
            < 1024 * 1024 * 1024 => $"{value / unit / unit:0.#} MB",
            < 1024L * 1024 * 1024 * 1024 => $"{value / unit / unit / unit:0.#} GB",
            _ => $"{value / unit / unit / unit / unit:0.#} TB",
        };
    }
}

public enum DriveTypeKind
{
    Unknown,
    Fixed,
    Removable,
    Network,
    Optical,
    Ram,
}

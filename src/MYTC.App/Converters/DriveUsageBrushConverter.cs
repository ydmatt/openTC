using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using MYTC.Domain.Drives;

namespace MYTC.App.Converters;

public sealed class DriveUsageBrushConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        return value is DriveEntry { HasCapacity: true } drive
            ? drive.IsLowFreeSpace
                ? Brushes.Firebrick
                : drive.UsedPercentage >= 75d
                    ? Brushes.DarkOrange
                    : Brushes.SeaGreen
            : Brushes.Gray;
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}

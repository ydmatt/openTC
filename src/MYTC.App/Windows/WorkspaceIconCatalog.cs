using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MYTC.App.Windows;

public static class WorkspaceIconCatalog
{
    public const string AutomaticKey = "AUTO";
    public const string BaseIconKey = "TC";
    public const string OneIconKey = "1";
    public const string WorkIconKey = "W";

    private static readonly HashSet<string> BadgeKeys =
        new([OneIconKey, WorkIconKey], StringComparer.OrdinalIgnoreCase);

    public static string? ResolveBadge(
        string? workspaceName,
        string? configuredIconKey)
    {
        var configured = configuredIconKey?.Trim().ToUpperInvariant();
        if (StringComparer.Ordinal.Equals(configured, BaseIconKey))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(configured) &&
            !StringComparer.Ordinal.Equals(configured, AutomaticKey))
        {
            return BadgeKeys.Contains(configured) ? configured : null;
        }

        var first = workspaceName?
            .Trim()
            .FirstOrDefault(character => char.IsLetterOrDigit(character));
        if (first is null || first == default(char))
        {
            return null;
        }

        var key = char.ToUpperInvariant(first.Value).ToString();
        return BadgeKeys.Contains(key) ? key : null;
    }

    public static ImageSource CreateImage(
        string? workspaceName,
        string? configuredIconKey)
    {
        var baseImage = LoadBaseImage();
        var badge = ResolveBadge(workspaceName, configuredIconKey);
        if (badge is null)
        {
            return baseImage;
        }

        const double size = 256;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawImage(baseImage, new Rect(0, 0, size, size));
            var badgeRect = new Rect(139, 2, 115, 115);
            drawing.DrawRoundedRectangle(
                new SolidColorBrush(Color.FromRgb(255, 153, 0)),
                new Pen(Brushes.White, 7),
                badgeRect,
                27,
                27);

            var text = new FormattedText(
                badge,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(
                    new FontFamily("Segoe UI"),
                    FontStyles.Normal,
                    FontWeights.Black,
                    FontStretches.Normal),
                badge.Length == 1 ? 76 : 62,
                Brushes.White,
                1.0);
            drawing.DrawText(
                text,
                new Point(
                    badgeRect.Left + (badgeRect.Width - text.Width) / 2,
                    badgeRect.Top + (badgeRect.Height - text.Height) / 2 - 4));
        }

        var bitmap = new RenderTargetBitmap(
            (int)size,
            (int)size,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public static ImageSource? CreateTaskbarOverlay(
        string? workspaceName,
        string? configuredIconKey)
    {
        var badge = ResolveBadge(workspaceName, configuredIconKey);
        if (badge is null)
        {
            return null;
        }

        const double size = 64;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var badgeRect = new Rect(2, 2, 60, 60);
            drawing.DrawRoundedRectangle(
                new SolidColorBrush(Color.FromRgb(255, 139, 0)),
                new Pen(Brushes.White, 4),
                badgeRect,
                15,
                15);

            var text = new FormattedText(
                badge,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(
                    new FontFamily("Segoe UI"),
                    FontStyles.Normal,
                    FontWeights.Black,
                    FontStretches.Normal),
                42,
                Brushes.White,
                1.0);
            drawing.DrawText(
                text,
                new Point(
                    (size - text.Width) / 2,
                    (size - text.Height) / 2 - 2));
        }

        var bitmap = new RenderTargetBitmap(
            (int)size,
            (int)size,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapFrame LoadBaseImage()
    {
        var resource = System.Windows.Application.GetResourceStream(
            new Uri(
                "pack://application:,,,/MYTC;component/Assets/mytc.ico",
                UriKind.Absolute))
            ?? throw new InvalidOperationException(
                "无法加载 MYTC 基础图标资源。");
        using var stream = resource.Stream;
        var decoder = new IconBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames
            .OrderByDescending(item => item.PixelWidth)
            .First();
        frame.Freeze();
        return frame;
    }
}

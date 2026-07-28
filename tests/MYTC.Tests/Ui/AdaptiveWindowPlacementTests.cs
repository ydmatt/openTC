using System.Windows;
using MYTC.App.Windows;

namespace MYTC.Tests.Ui;

public sealed class AdaptiveWindowPlacementTests
{
    [Theory]
    [InlineData(1366, 728)]
    [InlineData(1024, 576)]
    [InlineData(800, 450)]
    public void Calculate_KeepsEntireWindowInsideSmallWorkingArea(
        double workingWidth,
        double workingHeight)
    {
        var workingArea = new Rect(
            0,
            0,
            workingWidth,
            workingHeight);

        var result = AdaptiveWindowPlacement.Calculate(
            workingArea,
            desiredWidth: 1440,
            desiredHeight: 900,
            requestedMinimumWidth: 640,
            requestedMinimumHeight: 400);

        Assert.True(result.Left >= workingArea.Left);
        Assert.True(result.Top >= workingArea.Top);
        Assert.True(
            result.Left + result.Width <= workingArea.Right);
        Assert.True(
            result.Top + result.Height <= workingArea.Bottom);
        Assert.True(result.Width <= workingWidth);
        Assert.True(result.Height <= workingHeight);
    }

    [Fact]
    public void Calculate_CentersDesiredSizeOnLargeWorkingArea()
    {
        var result = AdaptiveWindowPlacement.Calculate(
            new Rect(0, 0, 1920, 1040),
            desiredWidth: 1440,
            desiredHeight: 900,
            requestedMinimumWidth: 640,
            requestedMinimumHeight: 400);

        Assert.Equal(1440, result.Width);
        Assert.Equal(900, result.Height);
        Assert.Equal(240, result.Left);
        Assert.Equal(70, result.Top);
    }

    [Fact]
    public void Calculate_ReducesEffectiveMinimumBelowTinyWorkArea()
    {
        var result = AdaptiveWindowPlacement.Calculate(
            new Rect(100, 50, 600, 360),
            desiredWidth: 1440,
            desiredHeight: 900,
            requestedMinimumWidth: 640,
            requestedMinimumHeight: 400);

        Assert.Equal(584, result.Width);
        Assert.Equal(344, result.Height);
        Assert.Equal(result.Width, result.MinimumWidth);
        Assert.Equal(result.Height, result.MinimumHeight);
        Assert.Equal(108, result.Left);
        Assert.Equal(58, result.Top);
    }
}

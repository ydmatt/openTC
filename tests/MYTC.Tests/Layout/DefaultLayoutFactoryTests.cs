using MYTC.Domain.Layout;

namespace MYTC.Tests.Layout;

public sealed class DefaultLayoutFactoryTests
{
    [Fact]
    public void CreateQuad_ContainsFourExpectedPanes()
    {
        var layout = DefaultLayoutFactory.CreateQuad();

        var paneIds = DefaultLayoutFactory.GetPaneIds(layout);

        Assert.Equal(
            ["top-left", "bottom-left", "top-right", "bottom-right"],
            paneIds);
        Assert.Equal(4, paneIds.Distinct(StringComparer.Ordinal).Count());
    }
}

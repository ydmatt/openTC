namespace MYTC.Domain.Layout;

public static class DefaultLayoutFactory
{
    public static LayoutNode CreateQuad()
    {
        return new SplitLayoutNode(
            SplitOrientation.Horizontal,
            0.5,
            new SplitLayoutNode(
                SplitOrientation.Vertical,
                0.5,
                new PaneLayoutNode("top-left"),
                new PaneLayoutNode("bottom-left")),
            new SplitLayoutNode(
                SplitOrientation.Vertical,
                0.5,
                new PaneLayoutNode("top-right"),
                new PaneLayoutNode("bottom-right")));
    }

    public static IReadOnlyList<string> GetPaneIds(LayoutNode node)
    {
        var paneIds = new List<string>();
        Visit(node, paneIds);
        return paneIds;
    }

    private static void Visit(LayoutNode node, ICollection<string> paneIds)
    {
        switch (node)
        {
            case PaneLayoutNode pane:
                paneIds.Add(pane.PaneId);
                break;
            case SplitLayoutNode split:
                Visit(split.First, paneIds);
                Visit(split.Second, paneIds);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(node));
        }
    }
}

namespace MYTC.Domain.Layout;

public abstract record LayoutNode;

public sealed record PaneLayoutNode(string PaneId) : LayoutNode;

public sealed record SplitLayoutNode(
    SplitOrientation Orientation,
    double Ratio,
    LayoutNode First,
    LayoutNode Second) : LayoutNode;

public enum SplitOrientation
{
    Horizontal,
    Vertical,
}

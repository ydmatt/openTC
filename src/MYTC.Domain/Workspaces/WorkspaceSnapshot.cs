using MYTC.Domain.Files;

namespace MYTC.Domain.Workspaces;

public sealed record WorkspaceSnapshot(
    int SchemaVersion,
    string Name,
    double HorizontalRatio,
    double VerticalRatio,
    IReadOnlyList<PaneSnapshot> Panes,
    string ActivePaneId,
    string TargetPaneId,
    DateTime UpdatedAtUtc,
    string? IconKey = null)
{
    public const int CurrentSchemaVersion = 2;
}

public sealed record PaneSnapshot(
    string Id,
    IReadOnlyList<TabSnapshot> Tabs,
    string ActiveTabId);

public sealed record TabSnapshot(
    string Id,
    string CustomTitle,
    TabMode Mode,
    string CurrentPath,
    string? FixedPath,
    IReadOnlyList<string> BackHistory,
    IReadOnlyList<string> ForwardHistory,
    SortDescriptor Sort);

public enum TabMode
{
    Normal,
    Fixed,
}

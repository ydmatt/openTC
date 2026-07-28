namespace MYTC.Domain.Configuration;

public sealed record TabContextMenuConfiguration(
    int SchemaVersion,
    IReadOnlyList<TabContextMenuItemDefinition> Items)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record TabContextMenuItemDefinition(
    string Id,
    TabContextMenuItemKind Kind,
    string Label,
    TabContextMenuAction? Action,
    bool IsVisible);

public enum TabContextMenuItemKind
{
    BuiltIn,
    Separator,
}

public enum TabContextMenuAction
{
    PinCurrentDirectory,
    Configure,
    CopyToTargetPane,
    MoveLeft,
    MoveRight,
    Close,
}

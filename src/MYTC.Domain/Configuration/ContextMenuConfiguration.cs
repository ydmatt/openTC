namespace MYTC.Domain.Configuration;

public sealed record ContextMenuConfiguration(
    int SchemaVersion,
    IReadOnlyList<ContextMenuItemDefinition> Items)
{
    public const int CurrentSchemaVersion = 7;
}

public sealed record ContextMenuItemDefinition(
    string Id,
    ContextMenuItemKind Kind,
    string Label,
    ContextMenuAction? Action,
    string? ProgramPath,
    string? Arguments,
    bool IsVisible,
    string? ParentId = null);

public enum ContextMenuItemKind
{
    BuiltIn,
    ExternalProgram,
    Separator,
    Submenu,
}

public enum ContextMenuAction
{
    Open,
    OpenWith,
    Properties,
    CopyToTarget,
    MoveToTarget,
    CopyToClipboard,
    CutToClipboard,
    PasteFromClipboard,
    CopyFullPath,
    CreateDirectory,
    CreateTextDocument,
    Rename,
    RecycleDelete,
    UndoDelete,
    PermanentDelete,
    Refresh,
}

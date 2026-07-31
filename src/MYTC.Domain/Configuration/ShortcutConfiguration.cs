namespace MYTC.Domain.Configuration;

public sealed record ShortcutConfiguration(
    int SchemaVersion,
    IReadOnlyList<ShortcutBinding> Bindings)
{
    public const int CurrentSchemaVersion = 5;
}

public sealed record ShortcutBinding(
    ShortcutAction Action,
    string Gesture);

public enum ShortcutAction
{
    CopyToTarget,
    MoveToTarget,
    CreateDirectory,
    RecycleDelete,
    PermanentDelete,
    Rename,
    CopyToClipboard,
    CutToClipboard,
    PasteFromClipboard,
    ActivatePane1,
    ActivatePane2,
    ActivatePane3,
    ActivatePane4,
    NewTab,
    CloseTab,
    RestoreClosedTab,
    RestoreFourPanes,
    FocusAddressBar,
    NavigateUp,
    ShowProperties,
}

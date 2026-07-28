using MYTC.Domain.Configuration;

namespace MYTC.Application.Configuration;

public static class ShortcutDefaults
{
    public static ShortcutConfiguration Create()
    {
        return new ShortcutConfiguration(
            ShortcutConfiguration.CurrentSchemaVersion,
            [
                new(ShortcutAction.CopyToTarget, "F5"),
                new(ShortcutAction.MoveToTarget, "F6"),
                new(ShortcutAction.CreateDirectory, "F7"),
                new(ShortcutAction.RecycleDelete, "F8"),
                new(ShortcutAction.RecycleDelete, "Del"),
                new(ShortcutAction.RecycleDelete, "Ctrl+D"),
                new(ShortcutAction.PermanentDelete, "Shift+F8"),
                new(ShortcutAction.PermanentDelete, "Shift+Del"),
                new(ShortcutAction.Rename, "F2"),
                new(ShortcutAction.CopyToClipboard, "Ctrl+C"),
                new(ShortcutAction.CutToClipboard, "Ctrl+X"),
                new(ShortcutAction.PasteFromClipboard, "Ctrl+V"),
                new(ShortcutAction.ActivatePane1, "Ctrl+1"),
                new(ShortcutAction.ActivatePane2, "Ctrl+2"),
                new(ShortcutAction.ActivatePane3, "Ctrl+3"),
                new(ShortcutAction.ActivatePane4, "Ctrl+4"),
                new(ShortcutAction.NewTab, "Ctrl+T"),
                new(ShortcutAction.CloseTab, "Ctrl+W"),
                new(ShortcutAction.RestoreClosedTab, "Ctrl+Shift+T"),
                new(ShortcutAction.RestoreFourPanes, "Esc"),
                new(ShortcutAction.FocusAddressBar, "Alt+D"),
                new(ShortcutAction.NavigateUp, "Backspace"),
            ]);
    }
}

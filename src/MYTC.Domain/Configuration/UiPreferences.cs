namespace MYTC.Domain.Configuration;

public sealed record UiPreferences(
    int SchemaVersion,
    bool IsOperationToolbarVisible,
    bool ConfirmRecycleDelete = true,
    bool StartWithWindows = false,
    bool IsWorkspaceToolbarVisible = true,
    bool IsSettingsToolbarVisible = true,
    string? LastWorkspaceName = null)
{
    public const int CurrentSchemaVersion = 3;

    public static UiPreferences CreateDefault()
    {
        return new UiPreferences(
            CurrentSchemaVersion,
            IsOperationToolbarVisible: false,
            ConfirmRecycleDelete: true,
            StartWithWindows: false,
            IsWorkspaceToolbarVisible: true,
            IsSettingsToolbarVisible: true,
            LastWorkspaceName: null);
    }
}

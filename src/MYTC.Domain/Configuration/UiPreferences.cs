namespace MYTC.Domain.Configuration;

public sealed record UiPreferences(
    int SchemaVersion,
    bool IsOperationToolbarVisible,
    bool ConfirmRecycleDelete = true,
    bool StartWithWindows = false,
    bool IsWorkspaceToolbarVisible = true,
    bool IsSettingsToolbarVisible = true,
    string? LastWorkspaceName = null,
    bool HasConfirmedWinRarPath = false,
    string? WinRarExecutablePath = null)
{
    public const int CurrentSchemaVersion = 4;

    public static UiPreferences CreateDefault()
    {
        return new UiPreferences(
            CurrentSchemaVersion,
            IsOperationToolbarVisible: false,
            ConfirmRecycleDelete: true,
            StartWithWindows: false,
            IsWorkspaceToolbarVisible: true,
            IsSettingsToolbarVisible: true,
            LastWorkspaceName: null,
            HasConfirmedWinRarPath: false,
            WinRarExecutablePath: null);
    }
}

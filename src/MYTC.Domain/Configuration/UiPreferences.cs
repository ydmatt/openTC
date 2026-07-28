namespace MYTC.Domain.Configuration;

public sealed record UiPreferences(
    int SchemaVersion,
    bool IsOperationToolbarVisible,
    bool ConfirmRecycleDelete = true,
    bool StartWithWindows = false)
{
    public const int CurrentSchemaVersion = 2;

    public static UiPreferences CreateDefault()
    {
        return new UiPreferences(
            CurrentSchemaVersion,
            IsOperationToolbarVisible: false,
            ConfirmRecycleDelete: true,
            StartWithWindows: false);
    }
}

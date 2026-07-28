namespace MYTC.Application.Shell;

public static class ShellIntegrationConstants
{
    public const string VerbName = "MYTC.Open";
    public const string BridgeRunValueName = "MYTC.WinEBridge";
    public const string BridgeEventName = @"Local\MYTC.WinEBridge.Exit";
}

public sealed record ShellIntegrationStatus(
    bool IsFolderDefault,
    bool IsWinEBridgeEnabled,
    string RegisteredExecutablePath,
    string Description);

namespace MYTC.Application.Shell;

public enum KeyboardTransition
{
    KeyDown,
    KeyUp,
}

public readonly record struct WinEKeyDecision(
    bool Suppress,
    int? ReplayWindowsKeyDown,
    bool LaunchMytc);

/// <summary>
/// Delays the physical Windows-key down event until the next key is known.
/// Win+E can therefore be consumed without Windows ever seeing a standalone
/// Windows key, while every other Windows shortcut is replayed normally.
/// </summary>
public sealed class WinEKeyInterceptor
{
    public const int LeftWindowsKey = 0x5B;
    public const int RightWindowsKey = 0x5C;
    public const int EKey = 0x45;

    private WinKeyState _state;
    private int _windowsKey;
    private bool _suppressEUntilKeyUp;

    public WinEKeyDecision Process(
        int virtualKey,
        KeyboardTransition transition)
    {
        var isKeyDown = transition == KeyboardTransition.KeyDown;
        if (IsWindowsKey(virtualKey))
        {
            return ProcessWindowsKey(virtualKey, isKeyDown);
        }

        if (virtualKey == EKey)
        {
            if (isKeyDown && _state == WinKeyState.Pending)
            {
                _state = WinKeyState.Intercepted;
                _suppressEUntilKeyUp = true;
                return new WinEKeyDecision(
                    Suppress: true,
                    ReplayWindowsKeyDown: null,
                    LaunchMytc: true);
            }

            if (_suppressEUntilKeyUp)
            {
                if (!isKeyDown)
                {
                    _suppressEUntilKeyUp = false;
                }

                return new WinEKeyDecision(
                    Suppress: true,
                    ReplayWindowsKeyDown: null,
                    LaunchMytc: false);
            }
        }

        if (_state is WinKeyState.Pending or WinKeyState.Intercepted)
        {
            var replayKey = _windowsKey;
            _state = WinKeyState.Passthrough;
            return new WinEKeyDecision(
                Suppress: false,
                ReplayWindowsKeyDown: replayKey,
                LaunchMytc: false);
        }

        return default;
    }

    private WinEKeyDecision ProcessWindowsKey(
        int virtualKey,
        bool isKeyDown)
    {
        if (isKeyDown)
        {
            if (_state == WinKeyState.Idle)
            {
                _windowsKey = virtualKey;
                _state = WinKeyState.Pending;
            }

            return _state is WinKeyState.Pending or
                WinKeyState.Intercepted
                ? new WinEKeyDecision(
                    Suppress: true,
                    ReplayWindowsKeyDown: null,
                    LaunchMytc: false)
                : default;
        }

        return _state switch
        {
            WinKeyState.Pending => CompleteStandaloneWindowsKey(),
            WinKeyState.Intercepted => CompleteInterceptedWindowsKey(),
            WinKeyState.Passthrough => CompletePassthroughWindowsKey(),
            _ => default,
        };
    }

    private WinEKeyDecision CompleteStandaloneWindowsKey()
    {
        var replayKey = _windowsKey;
        ResetWindowsKeyState();
        return new WinEKeyDecision(
            Suppress: false,
            ReplayWindowsKeyDown: replayKey,
            LaunchMytc: false);
    }

    private WinEKeyDecision CompleteInterceptedWindowsKey()
    {
        ResetWindowsKeyState();
        return new WinEKeyDecision(
            Suppress: true,
            ReplayWindowsKeyDown: null,
            LaunchMytc: false);
    }

    private WinEKeyDecision CompletePassthroughWindowsKey()
    {
        ResetWindowsKeyState();
        return default;
    }

    private void ResetWindowsKeyState()
    {
        _state = WinKeyState.Idle;
        _windowsKey = 0;
    }

    private static bool IsWindowsKey(int virtualKey)
    {
        return virtualKey is LeftWindowsKey or RightWindowsKey;
    }

    private enum WinKeyState
    {
        Idle,
        Pending,
        Intercepted,
        Passthrough,
    }
}

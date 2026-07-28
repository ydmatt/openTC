using System.Diagnostics;
using System.Runtime.InteropServices;
using MYTC.Application.Shell;

namespace MYTC.Maintenance;

internal static class WinEBridge
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const string MutexName = @"Local\MYTC.WinEBridge";
    private static readonly IntPtr ReplayMarker =
        new(unchecked((long)0x4D59544357494E45UL));

    public static int Run()
    {
        using var mutex = new Mutex(
            initiallyOwned: true,
            MutexName,
            out var isFirstInstance);
        if (!isFirstInstance)
        {
            return 0;
        }

        using var context = new BridgeApplicationContext();
        System.Windows.Forms.Application.Run(context);
        mutex.ReleaseMutex();
        return 0;
    }

    public static void SignalExit()
    {
        try
        {
            using var exitEvent = EventWaitHandle.OpenExisting(
                ShellIntegrationConstants.BridgeEventName);
            exitEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // Bridge is not currently running.
        }
    }

    private sealed class BridgeApplicationContext : ApplicationContext
    {
        private readonly LowLevelKeyboardProc _callback;
        private readonly IntPtr _hook;
        private readonly EventWaitHandle _exitEvent;
        private readonly System.Windows.Forms.Timer _exitTimer;
        private readonly WinEKeyInterceptor _keyInterceptor = new();
        private int _launchInProgress;

        public BridgeApplicationContext()
        {
            _callback = HookCallback;
            using var module = Process.GetCurrentProcess().MainModule;
            var moduleHandle = GetModuleHandle(module?.ModuleName);
            _hook = SetWindowsHookEx(
                WhKeyboardLl,
                _callback,
                moduleHandle,
                0);
            if (_hook == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"无法安装 Win+E 键盘桥接（错误 {Marshal.GetLastWin32Error()}）。");
            }

            _exitEvent = new EventWaitHandle(
                false,
                EventResetMode.ManualReset,
                ShellIntegrationConstants.BridgeEventName);
            _exitTimer = new System.Windows.Forms.Timer
            {
                Interval = 250,
            };
            _exitTimer.Tick += (_, _) =>
            {
                if (_exitEvent.WaitOne(0))
                {
                    ExitThread();
                }
            };
            _exitTimer.Start();
        }

        protected override void ExitThreadCore()
        {
            _exitTimer.Stop();
            _exitTimer.Dispose();
            _exitEvent.Dispose();
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
            }

            base.ExitThreadCore();
        }

        private IntPtr HookCallback(
            int code,
            IntPtr message,
            IntPtr data)
        {
            try
            {
                if (code < 0)
                {
                    return CallNextHookEx(
                        _hook,
                        code,
                        message,
                        data);
                }

                var keyboard = Marshal.PtrToStructure<KbdLlHookStruct>(data);
                if (keyboard.ExtraInfo == ReplayMarker)
                {
                    return CallNextHookEx(
                        _hook,
                        code,
                        message,
                        data);
                }

                var messageId = message.ToInt32();
                var transition = messageId switch
                {
                    WmKeyDown or WmSysKeyDown =>
                        KeyboardTransition.KeyDown,
                    WmKeyUp or WmSysKeyUp =>
                        KeyboardTransition.KeyUp,
                    _ => (KeyboardTransition?)null,
                };
                if (transition is null)
                {
                    return CallNextHookEx(
                        _hook,
                        code,
                        message,
                        data);
                }

                var decision = _keyInterceptor.Process(
                    keyboard.VirtualKeyCode,
                    transition.Value);
                if (decision.ReplayWindowsKeyDown is { } replayKey)
                {
                    ReplayWindowsKeyDown(replayKey);
                }

                if (decision.LaunchMytc)
                {
                    QueueLaunch();
                }

                if (decision.Suppress)
                {
                    return new IntPtr(1);
                }
            }
            catch
            {
                // A keyboard hook must never let an exception escape.
            }

            return CallNextHookEx(_hook, code, message, data);
        }

        private void QueueLaunch()
        {
            if (Interlocked.Exchange(ref _launchInProgress, 1) != 0)
            {
                return;
            }

            _ = Task.Run(() =>
            {
                try
                {
                    var executable = Path.Combine(
                        AppContext.BaseDirectory,
                        "MYTC.exe");
                    if (File.Exists(executable))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = executable,
                            UseShellExecute = false,
                        });
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _launchInProgress, 0);
                }
            });
        }

        private static void ReplayWindowsKeyDown(int virtualKey)
        {
            KeybdEvent(
                checked((byte)virtualKey),
                0,
                0,
                ReplayMarker);
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(
        int code,
        IntPtr message,
        IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct KbdLlHookStruct
    {
        public readonly int VirtualKeyCode;
        public readonly int ScanCode;
        public readonly int Flags;
        public readonly int Time;
        public readonly IntPtr ExtraInfo;
    }

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook,
        LowLevelKeyboardProc callback,
        IntPtr module,
        uint threadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hook,
        int code,
        IntPtr message,
        IntPtr data);

    [DllImport(
        "user32.dll",
        EntryPoint = "keybd_event")]
    private static extern void KeybdEvent(
        byte virtualKey,
        byte scanCode,
        uint flags,
        IntPtr extraInfo);

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}

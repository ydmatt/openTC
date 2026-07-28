using MYTC.Application.Shell;

namespace MYTC.Tests.Startup;

public sealed class WinEKeyInterceptorTests
{
    [Fact]
    public void WinE_SuppressesEveryPhysicalEvent_AndLaunchesOnce()
    {
        var interceptor = new WinEKeyInterceptor();

        var winDown = interceptor.Process(
            WinEKeyInterceptor.LeftWindowsKey,
            KeyboardTransition.KeyDown);
        var eDown = interceptor.Process(
            WinEKeyInterceptor.EKey,
            KeyboardTransition.KeyDown);
        var eRepeat = interceptor.Process(
            WinEKeyInterceptor.EKey,
            KeyboardTransition.KeyDown);
        var eUp = interceptor.Process(
            WinEKeyInterceptor.EKey,
            KeyboardTransition.KeyUp);
        var winUp = interceptor.Process(
            WinEKeyInterceptor.LeftWindowsKey,
            KeyboardTransition.KeyUp);

        Assert.True(winDown.Suppress);
        Assert.True(eDown.Suppress);
        Assert.True(eDown.LaunchMytc);
        Assert.True(eRepeat.Suppress);
        Assert.False(eRepeat.LaunchMytc);
        Assert.True(eUp.Suppress);
        Assert.True(winUp.Suppress);
        Assert.Null(winDown.ReplayWindowsKeyDown);
        Assert.Null(eDown.ReplayWindowsKeyDown);
    }

    [Fact]
    public void StandaloneWindowsKey_ReplaysDownBeforePhysicalUp()
    {
        var interceptor = new WinEKeyInterceptor();
        _ = interceptor.Process(
            WinEKeyInterceptor.RightWindowsKey,
            KeyboardTransition.KeyDown);

        var winUp = interceptor.Process(
            WinEKeyInterceptor.RightWindowsKey,
            KeyboardTransition.KeyUp);

        Assert.False(winUp.Suppress);
        Assert.Equal(
            WinEKeyInterceptor.RightWindowsKey,
            winUp.ReplayWindowsKeyDown);
    }

    [Fact]
    public void OtherWindowsShortcut_ReplaysWindowsDown_AndPassesThrough()
    {
        const int rKey = 0x52;
        var interceptor = new WinEKeyInterceptor();
        _ = interceptor.Process(
            WinEKeyInterceptor.LeftWindowsKey,
            KeyboardTransition.KeyDown);

        var rDown = interceptor.Process(
            rKey,
            KeyboardTransition.KeyDown);
        var winUp = interceptor.Process(
            WinEKeyInterceptor.LeftWindowsKey,
            KeyboardTransition.KeyUp);

        Assert.False(rDown.Suppress);
        Assert.Equal(
            WinEKeyInterceptor.LeftWindowsKey,
            rDown.ReplayWindowsKeyDown);
        Assert.False(winUp.Suppress);
        Assert.Null(winUp.ReplayWindowsKeyDown);
    }

    [Fact]
    public void AfterIntercept_OtherWindowsShortcutCanContinue()
    {
        const int rKey = 0x52;
        var interceptor = new WinEKeyInterceptor();
        _ = interceptor.Process(
            WinEKeyInterceptor.LeftWindowsKey,
            KeyboardTransition.KeyDown);
        _ = interceptor.Process(
            WinEKeyInterceptor.EKey,
            KeyboardTransition.KeyDown);

        var rDown = interceptor.Process(
            rKey,
            KeyboardTransition.KeyDown);

        Assert.False(rDown.Suppress);
        Assert.Equal(
            WinEKeyInterceptor.LeftWindowsKey,
            rDown.ReplayWindowsKeyDown);
    }
}

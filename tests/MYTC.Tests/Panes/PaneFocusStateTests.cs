using MYTC.Application.Panes;

namespace MYTC.Tests.Panes;

public sealed class PaneFocusStateTests
{
    [Fact]
    public void Activate_MakesPreviousActivePaneTheTarget()
    {
        var state = new PaneFocusState("top-left", "top-right");

        var changed = state.Activate("bottom-right");

        Assert.True(changed);
        Assert.Equal("bottom-right", state.ActivePaneId);
        Assert.Equal("top-left", state.TargetPaneId);
    }

    [Fact]
    public void Activate_SamePaneKeepsState()
    {
        var state = new PaneFocusState("top-left", "top-right");

        var changed = state.Activate("top-left");

        Assert.False(changed);
        Assert.Equal("top-left", state.ActivePaneId);
        Assert.Equal("top-right", state.TargetPaneId);
    }

    [Fact]
    public void SetTarget_RejectsActivePane()
    {
        var state = new PaneFocusState("top-left", "top-right");

        Assert.Throws<InvalidOperationException>(() => state.SetTarget("top-left"));
    }
}

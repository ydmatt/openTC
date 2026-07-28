namespace MYTC.Application.Panes;

public sealed class PaneFocusState(string activePaneId, string targetPaneId)
{
    public string ActivePaneId { get; private set; } = activePaneId;

    public string TargetPaneId { get; private set; } = targetPaneId;

    public bool Activate(string paneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paneId);

        if (StringComparer.Ordinal.Equals(ActivePaneId, paneId))
        {
            return false;
        }

        TargetPaneId = ActivePaneId;
        ActivePaneId = paneId;
        return true;
    }

    public void SetTarget(string paneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paneId);

        if (StringComparer.Ordinal.Equals(ActivePaneId, paneId))
        {
            throw new InvalidOperationException("The active pane cannot also be the target pane.");
        }

        TargetPaneId = paneId;
    }
}

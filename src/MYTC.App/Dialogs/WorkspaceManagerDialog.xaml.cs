using System.Collections.ObjectModel;
using System.Windows;

namespace MYTC.App.Dialogs;

public partial class WorkspaceManagerDialog
{
    public WorkspaceManagerDialog(
        IEnumerable<string> workspaceNames,
        string? selectedWorkspaceName)
    {
        WorkspaceNames = new ObservableCollection<string>(workspaceNames);
        SelectedWorkspaceName = selectedWorkspaceName;
        InitializeComponent();
        DataContext = this;
    }

    public ObservableCollection<string> WorkspaceNames { get; }

    public string? SelectedWorkspaceName { get; set; }

    public event EventHandler? ImportRequested;

    public event EventHandler<WorkspaceSelectionEventArgs>? ExportRequested;

    public event EventHandler<WorkspaceSelectionEventArgs>? DeleteRequested;

    public void ReplaceWorkspaceNames(
        IEnumerable<string> workspaceNames,
        string? selectedWorkspaceName)
    {
        WorkspaceNames.Clear();
        foreach (var name in workspaceNames)
        {
            WorkspaceNames.Add(name);
        }

        SelectedWorkspaceName = selectedWorkspaceName;
        WorkspaceListBox.SelectedItem = selectedWorkspaceName;
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        ImportRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(SelectedWorkspaceName))
        {
            ExportRequested?.Invoke(
                this,
                new WorkspaceSelectionEventArgs(SelectedWorkspaceName));
        }
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(SelectedWorkspaceName))
        {
            DeleteRequested?.Invoke(
                this,
                new WorkspaceSelectionEventArgs(SelectedWorkspaceName));
        }
    }
}

public sealed class WorkspaceSelectionEventArgs(string workspaceName) : EventArgs
{
    public string WorkspaceName { get; } = workspaceName;
}

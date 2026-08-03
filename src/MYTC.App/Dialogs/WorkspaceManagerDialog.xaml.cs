using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using MYTC.App.Windows;

namespace MYTC.App.Dialogs;

public partial class WorkspaceManagerDialog
{
    private readonly Dictionary<string, string?> _iconAssignments;

    public WorkspaceManagerDialog(
        IEnumerable<string> workspaceNames,
        string? selectedWorkspaceName,
        IReadOnlyDictionary<string, string?> iconAssignments)
    {
        _iconAssignments = new Dictionary<string, string?>(
            iconAssignments,
            StringComparer.OrdinalIgnoreCase);
        WorkspaceNames = new ObservableCollection<string>(workspaceNames);
        SelectedWorkspaceName = selectedWorkspaceName;
        InitializeComponent();
        DataContext = this;
    }

    public ObservableCollection<string> WorkspaceNames { get; }

    public string? SelectedWorkspaceName { get; set; }

    public string? SelectedIconKey =>
        WorkspaceIconComboBox.SelectedValue as string;

    public event EventHandler? ImportRequested;

    public event EventHandler<WorkspaceSelectionEventArgs>? ExportRequested;

    public event EventHandler<WorkspaceSelectionEventArgs>? RenameRequested;

    public event EventHandler<WorkspaceSelectionEventArgs>? DeleteRequested;

    public event EventHandler<WorkspaceIconSelectionEventArgs>?
        IconChangedRequested;

    public void ReplaceWorkspaceNames(
        IEnumerable<string> workspaceNames,
        string? selectedWorkspaceName,
        IReadOnlyDictionary<string, string?> iconAssignments)
    {
        _iconAssignments.Clear();
        foreach (var pair in iconAssignments)
        {
            _iconAssignments[pair.Key] = pair.Value;
        }

        WorkspaceNames.Clear();
        foreach (var name in workspaceNames)
        {
            WorkspaceNames.Add(name);
        }

        SelectedWorkspaceName = selectedWorkspaceName;
        WorkspaceListBox.SelectedItem = selectedWorkspaceName;
        UpdateSelectedIcon();
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

    private void OnRenameClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(SelectedWorkspaceName))
        {
            RenameRequested?.Invoke(
                this,
                new WorkspaceSelectionEventArgs(SelectedWorkspaceName));
        }
    }

    private void OnWorkspaceSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        SelectedWorkspaceName = WorkspaceListBox.SelectedItem as string;
        UpdateSelectedIcon();
    }

    private void OnApplyIconClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SelectedWorkspaceName) ||
            WorkspaceIconComboBox.SelectedValue is not string selectedKey)
        {
            return;
        }

        var iconKey = StringComparer.OrdinalIgnoreCase.Equals(
            selectedKey,
            WorkspaceIconCatalog.AutomaticKey)
            ? null
            : selectedKey;
        _iconAssignments[SelectedWorkspaceName] = iconKey;
        IconChangedRequested?.Invoke(
            this,
            new WorkspaceIconSelectionEventArgs(
                SelectedWorkspaceName,
                iconKey));
    }

    private void UpdateSelectedIcon()
    {
        var iconKey = !string.IsNullOrWhiteSpace(SelectedWorkspaceName) &&
            _iconAssignments.TryGetValue(SelectedWorkspaceName, out var value)
                ? value
                : null;
        WorkspaceIconComboBox.SelectedValue = iconKey ??
            WorkspaceIconCatalog.AutomaticKey;
    }
}

public sealed class WorkspaceSelectionEventArgs(string workspaceName) : EventArgs
{
    public string WorkspaceName { get; } = workspaceName;
}

public sealed class WorkspaceIconSelectionEventArgs(
    string workspaceName,
    string? iconKey) : EventArgs
{
    public string WorkspaceName { get; } = workspaceName;

    public string? IconKey { get; } = iconKey;
}

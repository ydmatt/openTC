using System.Windows;

namespace MYTC.App.Dialogs;

public partial class WorkspaceNameDialog
{
    public WorkspaceNameDialog(string? initialName)
    {
        InitializeComponent();
        NameTextBox.Text = initialName ?? string.Empty;
        NameTextBox.Focus();
        NameTextBox.SelectAll();
    }

    public string WorkspaceName => NameTextBox.Text.Trim();

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(WorkspaceName))
        {
            MessageBox.Show(
                this,
                "请输入工作区名称。",
                "MYTC",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            NameTextBox.Focus();
            return;
        }

        DialogResult = true;
    }
}

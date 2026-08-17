using System.Windows;

namespace MYTC.App.Dialogs;

public partial class WorkspaceNameDialog
{
    public WorkspaceNameDialog(
        string? initialName,
        string title = "保存工作区",
        string confirmButtonText = "保存")
    {
        InitializeComponent();
        Title = title;
        ConfirmButton.Content = confirmButtonText;
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
                "openTC",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            NameTextBox.Focus();
            return;
        }

        DialogResult = true;
    }
}

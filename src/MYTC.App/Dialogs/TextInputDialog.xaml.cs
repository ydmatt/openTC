using System.Windows;

namespace MYTC.App.Dialogs;

public partial class TextInputDialog
{
    public TextInputDialog(string title, string prompt, string? initialValue = null)
    {
        InitializeComponent();
        Title = title;
        PromptTextBlock.Text = prompt;
        ValueTextBox.Text = initialValue ?? string.Empty;
        ValueTextBox.Focus();
        ValueTextBox.SelectAll();
    }

    public string Value => ValueTextBox.Text.Trim();

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            MessageBox.Show(
                this,
                "名称不能为空。",
                "MYTC",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            ValueTextBox.Focus();
            return;
        }

        DialogResult = true;
    }
}

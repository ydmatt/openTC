using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace MYTC.App.Dialogs;

public partial class TabSettingsDialog
{
    public TabSettingsDialog(
        string title,
        bool isFixed,
        string currentPath,
        string? fixedPath)
    {
        InitializeComponent();
        TitleTextBox.Text = title;
        FixedCheckBox.IsChecked = isFixed;
        FixedPathTextBox.Text = fixedPath ?? currentPath;
        UpdateFixedControls();
        TitleTextBox.Focus();
        TitleTextBox.SelectAll();
    }

    public string TabTitle => TitleTextBox.Text;

    public bool IsFixed => FixedCheckBox.IsChecked == true;

    public string? FixedPath => IsFixed ? FixedPathTextBox.Text : null;

    private void OnFixedChanged(object sender, RoutedEventArgs e)
    {
        UpdateFixedControls();
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择固定目录",
            InitialDirectory = Directory.Exists(FixedPathTextBox.Text)
                ? FixedPathTextBox.Text
                : null,
        };

        if (dialog.ShowDialog(this) == true)
        {
            FixedPathTextBox.Text = dialog.FolderName;
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (IsFixed && !Directory.Exists(FixedPathTextBox.Text))
        {
            MessageBox.Show(
                this,
                "固定目录不存在或当前不可访问。",
                "openTC",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            FixedPathTextBox.Focus();
            return;
        }

        DialogResult = true;
    }

    private void UpdateFixedControls()
    {
        var enabled = FixedCheckBox.IsChecked == true;
        FixedPathTextBox.IsEnabled = enabled;
        BrowseButton.IsEnabled = enabled;
    }
}

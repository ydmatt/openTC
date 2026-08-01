using System.Windows;
using Microsoft.Win32;

namespace MYTC.App.Dialogs;

public partial class GlobalSettingsDialog
{
    public GlobalSettingsDialog(
        bool startWithWindows,
        bool confirmRecycleDelete,
        string? winRarExecutablePath)
    {
        InitializeComponent();
        StartWithWindowsCheckBox.IsChecked = startWithWindows;
        ConfirmRecycleDeleteCheckBox.IsChecked = confirmRecycleDelete;
        WinRarPathTextBox.Text = winRarExecutablePath ?? string.Empty;
    }

    public bool StartWithWindows =>
        StartWithWindowsCheckBox.IsChecked == true;

    public bool ConfirmRecycleDelete =>
        ConfirmRecycleDeleteCheckBox.IsChecked == true;

    public string? WinRarExecutablePath =>
        string.IsNullOrWhiteSpace(WinRarPathTextBox.Text)
            ? null
            : WinRarPathTextBox.Text.Trim().Trim('"');

    private void OnBrowseWinRarClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 WinRAR.exe",
            Filter = "WinRAR.exe|WinRAR.exe|应用程序|*.exe",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) == true)
        {
            WinRarPathTextBox.Text = dialog.FileName;
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}

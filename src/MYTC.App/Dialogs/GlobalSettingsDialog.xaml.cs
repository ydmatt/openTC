using System.Windows;

namespace MYTC.App.Dialogs;

public partial class GlobalSettingsDialog
{
    public GlobalSettingsDialog(
        bool startWithWindows,
        bool confirmRecycleDelete)
    {
        InitializeComponent();
        StartWithWindowsCheckBox.IsChecked = startWithWindows;
        ConfirmRecycleDeleteCheckBox.IsChecked = confirmRecycleDelete;
    }

    public bool StartWithWindows =>
        StartWithWindowsCheckBox.IsChecked == true;

    public bool ConfirmRecycleDelete =>
        ConfirmRecycleDeleteCheckBox.IsChecked == true;

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}

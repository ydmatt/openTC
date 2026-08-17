using System.Windows;

namespace MYTC.App.Dialogs;

public partial class TextInputDialog
{
    private readonly int? _initialSelectionStart;
    private readonly int? _initialSelectionLength;

    public TextInputDialog(
        string title,
        string prompt,
        string? initialValue = null,
        int? initialSelectionStart = null,
        int? initialSelectionLength = null)
    {
        InitializeComponent();
        Title = title;
        PromptTextBlock.Text = prompt;
        ValueTextBox.Text = initialValue ?? string.Empty;
        _initialSelectionStart = initialSelectionStart;
        _initialSelectionLength = initialSelectionLength;
        Loaded += OnLoaded;
    }

    public string Value => ValueTextBox.Text.Trim();

    internal (int Start, int Length) InitialSelection =>
        (ValueTextBox.SelectionStart, ValueTextBox.SelectionLength);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ValueTextBox.Focus();
        if (_initialSelectionStart is { } start &&
            _initialSelectionLength is { } length)
        {
            var clampedStart = Math.Clamp(start, 0, ValueTextBox.Text.Length);
            ValueTextBox.Select(
                clampedStart,
                Math.Clamp(length, 0, ValueTextBox.Text.Length - clampedStart));
            return;
        }

        ValueTextBox.SelectAll();
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            MessageBox.Show(
                this,
                "名称不能为空。",
                "openTC",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            ValueTextBox.Focus();
            return;
        }

        DialogResult = true;
    }
}

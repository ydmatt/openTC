using System.Windows;
using System.Windows.Input;
using MYTC.App.Shortcuts;

namespace MYTC.App.Dialogs;

public partial class ShortcutCaptureDialog
{
    private readonly List<string> _chords = [];

    public ShortcutCaptureDialog(string actionLabel)
    {
        InitializeComponent();
        Title = $"录入快捷键 — {actionLabel}";
    }

    public string Gesture { get; private set; } = string.Empty;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (e.IsRepeat)
        {
            return;
        }

        var chord = ShortcutManager.FormatKeyEvent(e);
        if (chord is null)
        {
            GestureTextBlock.Text = "请继续按下主键…";
            return;
        }

        if (SequenceCheckBox.IsChecked != true)
        {
            Gesture = chord;
            DialogResult = true;
            return;
        }

        _chords.Add(chord);
        Gesture = string.Join(", ", _chords);
        GestureTextBlock.Text = Gesture;
        FinishButton.IsEnabled = true;
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        _chords.Clear();
        Gesture = string.Empty;
        GestureTextBlock.Text = "等待按键…";
        FinishButton.IsEnabled = false;
    }

    private void OnFinishClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(Gesture))
        {
            DialogResult = true;
        }
    }
}

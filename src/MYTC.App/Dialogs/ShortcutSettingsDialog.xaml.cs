using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MYTC.App.Mvvm;
using MYTC.App.Shortcuts;
using MYTC.Domain.Configuration;

namespace MYTC.App.Dialogs;

public partial class ShortcutSettingsDialog
{
    private readonly ShortcutManager _manager;

    public ShortcutSettingsDialog(ShortcutManager manager)
    {
        _manager = manager;
        InitializeComponent();
        Rows = [];
        SchemeNames = [];
        ActionChoices = new ObservableCollection<ShortcutActionChoice>(
            Enum.GetValues<ShortcutAction>()
                .Select(action => new ShortcutActionChoice(
                    action,
                    ShortcutManager.GetActionLabel(action))));
        ReplaceRows(manager.Bindings);

        DataContext = this;
        ActionComboBox.SelectedIndex = 0;
        Loaded += async (_, _) => await RefreshSchemeNamesAsync();
    }

    public ObservableCollection<ShortcutRow> Rows { get; }

    public ObservableCollection<string> SchemeNames { get; }

    public ObservableCollection<ShortcutActionChoice> ActionChoices { get; }

    public IReadOnlyList<ShortcutBinding> Result { get; private set; } = [];

    private async void OnLoadSchemeClick(object sender, RoutedEventArgs e)
    {
        if (SchemeComboBox.SelectedItem is not string name)
        {
            MessageBox.Show(
                this,
                "请先选择一个快捷键方案。",
                "快捷键方案",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            var bindings = await _manager.LoadSchemeAsync(name);
            if (bindings is null)
            {
                MessageBox.Show(
                    this,
                    "找不到所选方案。",
                    "快捷键方案",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            ReplaceRows(bindings);
        }
        catch (ArgumentException exception)
        {
            ShowValidationError(exception);
        }
    }

    private async void OnSaveSchemeClick(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<ShortcutBinding> bindings;
        try
        {
            bindings = CaptureBindings();
        }
        catch (ArgumentException exception)
        {
            ShowValidationError(exception);
            return;
        }

        var dialog = new TextInputDialog(
            "保存快捷键方案",
            "方案名称",
            SchemeComboBox.SelectedItem as string)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await _manager.SaveSchemeAsync(dialog.Value, bindings);
            await RefreshSchemeNamesAsync(dialog.Value);
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "保存方案失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnAddShortcutClick(object sender, RoutedEventArgs e)
    {
        if (ActionComboBox.SelectedValue is not ShortcutAction action)
        {
            return;
        }

        var row = new ShortcutRow(
            action,
            ShortcutManager.GetActionLabel(action),
            string.Empty);
        var lastIndex = Rows
            .Select((item, index) => new { item, index })
            .Where(pair => pair.item.Action == action)
            .Select(pair => pair.index)
            .DefaultIfEmpty(Rows.Count - 1)
            .Last();
        Rows.Insert(Math.Min(lastIndex + 1, Rows.Count), row);
        ShortcutGrid.SelectedItem = row;
        ShortcutGrid.ScrollIntoView(row);
        if (!CaptureShortcut(row))
        {
            Rows.Remove(row);
        }
    }

    private void OnCaptureClick(object sender, RoutedEventArgs e)
    {
        if (ShortcutGrid.SelectedItem is ShortcutRow row)
        {
            CaptureShortcut(row);
        }
    }

    private void OnTextEditClick(object sender, RoutedEventArgs e)
    {
        if (ShortcutGrid.SelectedItem is not ShortcutRow row)
        {
            return;
        }

        var dialog = new TextInputDialog(
            "文字编辑快捷键",
            "快捷键文字（例如 Del、Ctrl+Shift+T、Ctrl+D, P）",
            row.Gesture)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            row.Gesture = ShortcutManager.NormalizeGesture(dialog.Value);
        }
        catch (ArgumentException exception)
        {
            ShowValidationError(exception);
        }
    }

    private void OnDeleteShortcutClick(object sender, RoutedEventArgs e)
    {
        if (ShortcutGrid.SelectedItem is ShortcutRow row)
        {
            Rows.Remove(row);
        }
    }

    private void OnRestoreDefaultsClick(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                this,
                "恢复 Windows + Total Commander 默认快捷键？当前未保存的修改会被替换。",
                "恢复默认快捷键",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No) == MessageBoxResult.Yes)
        {
            ReplaceRows(ShortcutManager.DefaultBindings);
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ShortcutGrid.SelectedItem is ShortcutRow row)
        {
            ActionComboBox.SelectedValue = row.Action;
        }
    }

    private void OnGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ShortcutGrid.SelectedItem is ShortcutRow row)
        {
            CaptureShortcut(row);
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Result = CaptureBindings();
            DialogResult = true;
        }
        catch (ArgumentException exception)
        {
            ShowValidationError(exception);
        }
    }

    private bool CaptureShortcut(ShortcutRow row)
    {
        var dialog = new ShortcutCaptureDialog(row.ActionLabel)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() == true)
        {
            row.Gesture = dialog.Gesture;
            return true;
        }

        return false;
    }

    private IReadOnlyList<ShortcutBinding> CaptureBindings()
    {
        var bindings = Rows
            .Where(row => !string.IsNullOrWhiteSpace(row.Gesture))
            .Select(row => new ShortcutBinding(
                row.Action,
                ShortcutManager.NormalizeGesture(row.Gesture)))
            .ToArray();
        ShortcutManager.Validate(
            new ShortcutConfiguration(
                ShortcutConfiguration.CurrentSchemaVersion,
                bindings));
        return bindings;
    }

    private void ReplaceRows(IEnumerable<ShortcutBinding> bindings)
    {
        Rows.Clear();
        foreach (var binding in bindings
                     .OrderBy(item => item.Action)
                     .ThenBy(item => item.Gesture, StringComparer.OrdinalIgnoreCase))
        {
            Rows.Add(new ShortcutRow(
                binding.Action,
                ShortcutManager.GetActionLabel(binding.Action),
                binding.Gesture));
        }

        if (ShortcutGrid is not null && Rows.Count > 0)
        {
            ShortcutGrid.SelectedIndex = 0;
        }
    }

    private async Task RefreshSchemeNamesAsync(string? selectName = null)
    {
        SchemeNames.Clear();
        foreach (var name in await _manager.ListSchemeNamesAsync())
        {
            SchemeNames.Add(name);
        }

        SchemeComboBox.SelectedItem =
            selectName is not null && SchemeNames.Contains(selectName)
                ? selectName
                : SchemeNames.FirstOrDefault();
    }

    private void ShowValidationError(ArgumentException exception)
    {
        MessageBox.Show(
            this,
            exception.Message,
            "快捷键无效",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}

public sealed class ShortcutRow : ObservableObject
{
    private string _gesture;

    public ShortcutRow(
        ShortcutAction action,
        string actionLabel,
        string gesture)
    {
        Action = action;
        ActionLabel = actionLabel;
        _gesture = gesture;
    }

    public ShortcutAction Action { get; }

    public string ActionLabel { get; }

    public string Gesture
    {
        get => _gesture;
        set => SetProperty(ref _gesture, value);
    }
}

public sealed record ShortcutActionChoice(
    ShortcutAction Action,
    string Label);

using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using MYTC.Application.Abstractions;
using MYTC.Application.Configuration;
using MYTC.Domain.Configuration;

namespace MYTC.App.Dialogs;

public partial class TabContextMenuSettingsDialog
{
    private readonly ITabContextMenuStore _store;

    public TabContextMenuSettingsDialog(
        TabContextMenuConfiguration configuration,
        ITabContextMenuStore store)
    {
        _store = store;
        InitializeComponent();
        Rows = new ObservableCollection<TabContextMenuRow>(
            configuration.Items.Select(TabContextMenuRow.FromDefinition));
        SchemeNames = [];
        MenuGrid.DataContext = Rows;
        SchemeComboBox.ItemsSource = SchemeNames;
        Loaded += async (_, _) => await RefreshSchemeNamesAsync();
    }

    public ObservableCollection<TabContextMenuRow> Rows { get; }

    public ObservableCollection<string> SchemeNames { get; }

    public TabContextMenuConfiguration? Result { get; private set; }

    private async void OnLoadSchemeClick(object sender, RoutedEventArgs e)
    {
        if (SchemeComboBox.SelectedItem is not string name)
        {
            MessageBox.Show(
                this,
                "请先选择一个标签菜单方案。",
                "标签菜单方案",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            var configuration = await _store.LoadSchemeAsync(name);
            if (configuration is null)
            {
                MessageBox.Show(
                    this,
                    "找不到所选方案。",
                    "标签菜单方案",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            ReplaceRows(configuration);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                IOException or
                UnauthorizedAccessException)
        {
            ShowSchemeError("载入方案失败", exception);
        }
    }

    private async void OnSaveSchemeClick(object sender, RoutedEventArgs e)
    {
        var configuration = CaptureConfiguration();
        var dialog = new TextInputDialog(
            "保存标签菜单方案",
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
            await _store.SaveSchemeAsync(dialog.Value, configuration);
            await RefreshSchemeNamesAsync(dialog.Value);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                IOException or
                UnauthorizedAccessException)
        {
            ShowSchemeError("保存方案失败", exception);
        }
    }

    private void OnAddSeparatorClick(object sender, RoutedEventArgs e)
    {
        var row = new TabContextMenuRow
        {
            Id = "separator-" + Guid.NewGuid().ToString("N"),
            Kind = TabContextMenuItemKind.Separator,
            IsVisible = true,
        };
        var index = MenuGrid.SelectedItem is TabContextMenuRow selected
            ? Rows.IndexOf(selected) + 1
            : Rows.Count;
        Rows.Insert(index, row);
        MenuGrid.SelectedItem = row;
    }

    private void OnMoveUpClick(object sender, RoutedEventArgs e)
    {
        MoveSelected(-1);
    }

    private void OnMoveDownClick(object sender, RoutedEventArgs e)
    {
        MoveSelected(1);
    }

    private void OnRemoveSeparatorClick(object sender, RoutedEventArgs e)
    {
        if (MenuGrid.SelectedItem is TabContextMenuRow
            {
                Kind: TabContextMenuItemKind.Separator,
            } row)
        {
            Rows.Remove(row);
        }
    }

    private void OnRestoreDefaultsClick(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                this,
                "恢复默认标签右键菜单？当前未保存的修改会被替换。",
                "恢复默认标签右键菜单",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No) == MessageBoxResult.Yes)
        {
            ReplaceRows(TabContextMenuDefaults.Create());
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        Result = CaptureConfiguration();
        DialogResult = true;
    }

    private void MoveSelected(int offset)
    {
        if (MenuGrid.SelectedItem is not TabContextMenuRow row)
        {
            return;
        }

        var oldIndex = Rows.IndexOf(row);
        var newIndex = oldIndex + offset;
        if (newIndex < 0 || newIndex >= Rows.Count)
        {
            return;
        }

        Rows.Move(oldIndex, newIndex);
        MenuGrid.SelectedItem = row;
    }

    private TabContextMenuConfiguration CaptureConfiguration()
    {
        MenuGrid.CommitEdit();
        MenuGrid.CommitEdit();
        return new TabContextMenuConfiguration(
            TabContextMenuConfiguration.CurrentSchemaVersion,
            Rows.Select(row => row.ToDefinition()).ToArray());
    }

    private void ReplaceRows(TabContextMenuConfiguration configuration)
    {
        Rows.Clear();
        foreach (var definition in configuration.Items)
        {
            Rows.Add(TabContextMenuRow.FromDefinition(definition));
        }

        if (Rows.Count > 0)
        {
            MenuGrid.SelectedIndex = 0;
        }
    }

    private async Task RefreshSchemeNamesAsync(string? selectName = null)
    {
        SchemeNames.Clear();
        foreach (var name in await _store.ListSchemeNamesAsync())
        {
            SchemeNames.Add(name);
        }

        SchemeComboBox.SelectedItem =
            selectName is not null && SchemeNames.Contains(selectName)
                ? selectName
                : SchemeNames.FirstOrDefault();
    }

    private void ShowSchemeError(string title, Exception exception)
    {
        MessageBox.Show(
            this,
            exception.Message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}

public sealed class TabContextMenuRow
{
    public required string Id { get; init; }

    public TabContextMenuItemKind Kind { get; init; }

    public string Label { get; set; } = string.Empty;

    public TabContextMenuAction? Action { get; init; }

    public bool IsVisible { get; set; }

    public string KindLabel => Kind switch
    {
        TabContextMenuItemKind.BuiltIn => "内置",
        TabContextMenuItemKind.Separator => "分隔线",
        _ => Kind.ToString(),
    };

    public static TabContextMenuRow FromDefinition(
        TabContextMenuItemDefinition definition)
    {
        return new TabContextMenuRow
        {
            Id = definition.Id,
            Kind = definition.Kind,
            Label = definition.Label,
            Action = definition.Action,
            IsVisible = definition.IsVisible,
        };
    }

    public TabContextMenuItemDefinition ToDefinition()
    {
        return new TabContextMenuItemDefinition(
            Id,
            Kind,
            Label,
            Action,
            IsVisible);
    }
}

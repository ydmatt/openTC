using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using MYTC.Application.Abstractions;
using MYTC.Application.Configuration;
using MYTC.Domain.Configuration;

namespace MYTC.App.Dialogs;

public partial class ContextMenuSettingsDialog
{
    private readonly IContextMenuStore _store;

    public ContextMenuSettingsDialog(
        ContextMenuConfiguration configuration,
        IContextMenuStore store)
    {
        _store = store;
        InitializeComponent();
        Rows = new ObservableCollection<ContextMenuRow>(
            configuration.Items.Select(ContextMenuRow.FromDefinition));
        SchemeNames = [];
        MenuGrid.DataContext = Rows;
        SchemeComboBox.ItemsSource = SchemeNames;
        Loaded += async (_, _) => await RefreshSchemeNamesAsync();
    }

    public ObservableCollection<ContextMenuRow> Rows { get; }

    public ObservableCollection<string> SchemeNames { get; }

    public ContextMenuConfiguration? Result { get; private set; }

    private async void OnLoadSchemeClick(object sender, RoutedEventArgs e)
    {
        if (SchemeComboBox.SelectedItem is not string name)
        {
            MessageBox.Show(
                this,
                "请先选择一个右键菜单方案。",
                "右键菜单方案",
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
                    "右键菜单方案",
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
        if (!TryCaptureConfiguration(out var configuration))
        {
            return;
        }

        var dialog = new TextInputDialog(
            "保存右键菜单方案",
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

    private void OnAddProgramClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要加入右键菜单的程序",
            Filter = "程序 (*.exe;*.bat;*.cmd)|*.exe;*.bat;*.cmd|所有文件 (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var row = new ContextMenuRow
        {
            Id = "external-" + Guid.NewGuid().ToString("N"),
            Kind = ContextMenuItemKind.ExternalProgram,
            Label = Path.GetFileNameWithoutExtension(dialog.FileName),
            ProgramPath = dialog.FileName,
            Arguments = "{path}",
            IsVisible = true,
        };
        Rows.Add(row);
        MenuGrid.SelectedItem = row;
        MenuGrid.ScrollIntoView(row);
    }

    private void OnAddSeparatorClick(object sender, RoutedEventArgs e)
    {
        var row = new ContextMenuRow
        {
            Id = "separator-" + Guid.NewGuid().ToString("N"),
            Kind = ContextMenuItemKind.Separator,
            IsVisible = true,
        };
        Rows.Add(row);
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

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (MenuGrid.SelectedItem is not ContextMenuRow row ||
            row.Kind is ContextMenuItemKind.BuiltIn or
                ContextMenuItemKind.Submenu)
        {
            return;
        }

        Rows.Remove(row);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (TryCaptureConfiguration(out var configuration))
        {
            Result = configuration;
            DialogResult = true;
        }
    }

    private void OnRestoreDefaultsClick(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                this,
                "恢复默认右键菜单？当前未保存的修改会被替换。",
                "恢复默认右键菜单",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No) == MessageBoxResult.Yes)
        {
            ReplaceRows(ContextMenuDefaults.Create());
        }
    }

    private void MoveSelected(int offset)
    {
        if (MenuGrid.SelectedItem is not ContextMenuRow row)
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

    private bool TryCaptureConfiguration(
        out ContextMenuConfiguration configuration)
    {
        MenuGrid.CommitEdit();
        MenuGrid.CommitEdit();

        var invalid = Rows.FirstOrDefault(row =>
            row.Kind == ContextMenuItemKind.ExternalProgram &&
            (string.IsNullOrWhiteSpace(row.Label) ||
             string.IsNullOrWhiteSpace(row.ProgramPath)));
        if (invalid is not null)
        {
            MessageBox.Show(
                this,
                "外部程序项必须填写菜单文字和程序路径。",
                "右键菜单设置",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            MenuGrid.SelectedItem = invalid;
            configuration = null!;
            return false;
        }

        configuration = new ContextMenuConfiguration(
            ContextMenuConfiguration.CurrentSchemaVersion,
            Rows.Select(row => row.ToDefinition()).ToArray());
        return true;
    }

    private void ReplaceRows(ContextMenuConfiguration configuration)
    {
        Rows.Clear();
        foreach (var definition in configuration.Items)
        {
            Rows.Add(ContextMenuRow.FromDefinition(definition));
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

public sealed class ContextMenuRow
{
    public required string Id { get; init; }

    public ContextMenuItemKind Kind { get; init; }

    public string Label { get; set; } = string.Empty;

    public ContextMenuAction? Action { get; init; }

    public string? ProgramPath { get; set; }

    public string? Arguments { get; set; }

    public bool IsVisible { get; set; }

    public string? ParentId { get; init; }

    public string KindLabel => Kind switch
    {
        ContextMenuItemKind.BuiltIn => "内置",
        ContextMenuItemKind.ExternalProgram => "外部程序",
        ContextMenuItemKind.Separator => "分隔线",
        ContextMenuItemKind.Submenu => "子菜单",
        _ => Kind.ToString(),
    };

    public string LevelLabel =>
        string.IsNullOrWhiteSpace(ParentId) ? "一级" : "　↳ 二级";

    public static ContextMenuRow FromDefinition(ContextMenuItemDefinition definition)
    {
        return new ContextMenuRow
        {
            Id = definition.Id,
            Kind = definition.Kind,
            Label = definition.Label,
            Action = definition.Action,
            ProgramPath = definition.ProgramPath,
            Arguments = definition.Arguments,
            IsVisible = definition.IsVisible,
            ParentId = definition.ParentId,
        };
    }

    public ContextMenuItemDefinition ToDefinition()
    {
        return new ContextMenuItemDefinition(
            Id,
            Kind,
            Label,
            Action,
            ProgramPath,
            Arguments,
            IsVisible,
            ParentId);
    }
}

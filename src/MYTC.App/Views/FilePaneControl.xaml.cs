using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MYTC.App.Dialogs;
using MYTC.App.ViewModels;
using MYTC.Application.Files;
using MYTC.Domain.Files;

namespace MYTC.App.Views;

public partial class FilePaneControl
{
    private const string RightDragDataFormat = "MYTC.RightDrag";
    private const string TabDragDataFormat = "MYTC.TabDrag";
    private Point _dragStart;
    private Point _tabDragStart;
    private FileTabViewModel? _tabDragCandidate;
    private FrameworkElement? _tabDragSource;
    private int _keyboardSelectionAnchorIndex = -1;
    private int _keyboardSelectionCaretIndex = -1;
    private bool _applyingKeyboardSelection;
    private IReadOnlyList<int> _quickLocateMatchIndexes = [];

    public FilePaneControl()
    {
        InitializeComponent();
    }

    private FilePaneViewModel? ViewModel => DataContext as FilePaneViewModel;

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.RequestActivation();
    }

    private async void OnAddressKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || ViewModel is null)
        {
            return;
        }

        e.Handled = true;
        await ViewModel.NavigateFromAddressAsync();
    }

    private void OnAddressPasteCanExecute(
        object sender,
        CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = TryGetAddressClipboardText(out _);
        e.Handled = true;
    }

    private void OnAddressPasteExecuted(
        object sender,
        ExecutedRoutedEventArgs e)
    {
        if (sender is TextBox textBox &&
            TryGetAddressClipboardText(out var clipboardText))
        {
            textBox.SelectedText = clipboardText;
            textBox.CaretIndex = textBox.SelectionStart +
                clipboardText.Length;
            textBox.SelectionLength = 0;
            e.Handled = true;
        }
    }

    private async void OnFileDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is null || FileGrid.SelectedItem is not FileSystemEntry entry)
        {
            return;
        }

        await ViewModel.OpenEntryAsync(entry);
    }

    private async void OnTabClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (ViewModel is null ||
            sender is not FrameworkElement { DataContext: FileTabViewModel tab })
        {
            return;
        }

        await ViewModel.SelectTabAsync(tab);
    }

    private void OnTabBarMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 ||
            e.ChangedButton != MouseButton.Left ||
            ViewModel is null ||
            HasButtonAncestor(e.OriginalSource as DependencyObject))
        {
            return;
        }

        e.Handled = true;
        ViewModel.NewTabCommand.Execute(null);
    }

    private async void OnTabMouseDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (ViewModel is null ||
            sender is not FrameworkElement
            {
                DataContext: FileTabViewModel tab,
            })
        {
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            _tabDragStart = e.GetPosition(this);
            _tabDragCandidate = tab;
            _tabDragSource = (FrameworkElement)sender;
            return;
        }

        if (e.ChangedButton != MouseButton.Middle)
        {
            return;
        }

        e.Handled = true;
        ClearTabDragCandidate();
        await ViewModel.CloseTabAsync(tab);
    }

    private void OnPanePreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            ViewModel is null ||
            _tabDragCandidate is null ||
            _tabDragSource is null)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _tabDragStart.X) <
                SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _tabDragStart.Y) <
                SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var payload = new TabDragPayload(
            ViewModel,
            _tabDragCandidate);
        var data = new DataObject();
        data.SetData(TabDragDataFormat, payload);
        data.SetData(typeof(TabDragPayload), payload);
        var dragSource = _tabDragSource;
        ClearTabDragCandidate();
        e.Handled = true;
        DragDrop.DoDragDrop(
            dragSource,
            data,
            DragDropEffects.Copy);
    }

    private void OnPanePreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        ClearTabDragCandidate();
    }

    private void OnPanePreviewDragOver(object sender, DragEventArgs e)
    {
        if (!TryGetTabDragPayload(e, out var payload) ||
            ViewModel is null ||
            ReferenceEquals(payload.SourcePane, ViewModel))
        {
            return;
        }

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void OnPanePreviewDrop(object sender, DragEventArgs e)
    {
        if (!TryGetTabDragPayload(e, out var payload) ||
            ViewModel is null ||
            ReferenceEquals(payload.SourcePane, ViewModel) ||
            Window.GetWindow(this) is not MainWindow window)
        {
            return;
        }

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
        await window.HandleTabDropAsync(
            payload.SourcePane,
            ViewModel,
            payload.Tab);
    }

    private static bool TryGetTabDragPayload(
        DragEventArgs e,
        out TabDragPayload payload)
    {
        if ((e.Data.GetDataPresent(typeof(TabDragPayload)) &&
             e.Data.GetData(typeof(TabDragPayload))
                 is TabDragPayload foundByType))
        {
            payload = foundByType;
            return true;
        }

        if (e.Data.GetDataPresent(
                TabDragDataFormat,
                autoConvert: false) &&
            e.Data.GetData(
                TabDragDataFormat,
                autoConvert: false) is TabDragPayload found)
        {
            payload = found;
            return true;
        }

        payload = null!;
        return false;
    }

    private void ClearTabDragCandidate()
    {
        _tabDragCandidate = null;
        _tabDragSource = null;
    }

    private void OnTabContextMenuOpened(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is ContextMenu
            {
                PlacementTarget: FrameworkElement
                {
                    DataContext: FileTabViewModel tab,
                },
            } menu &&
            ViewModel is not null &&
            Window.GetWindow(this) is MainWindow window)
        {
            window.PopulateTabContextMenu(menu, ViewModel, tab);
        }
    }

    private async void OnPinTabClick(
        object sender,
        RoutedEventArgs e)
    {
        var tab = GetContextTab(sender);
        if (ViewModel is not null && tab is not null)
        {
            await ViewModel.PinTabToCurrentDirectoryAsync(tab);
        }
    }

    private async void OnConfigureTabClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var tab = GetContextTab(sender);
        if (ViewModel is null || tab is null)
        {
            return;
        }

        var dialog = new TabSettingsDialog(
            tab.CustomTitle,
            tab.IsFixed,
            tab.CurrentPath,
            tab.FixedPath)
        {
            Owner = Window.GetWindow(this),
        };

        if (dialog.ShowDialog() == true)
        {
            await ViewModel.ApplyTabSettingsAsync(
                tab,
                dialog.TabTitle,
                dialog.IsFixed,
                dialog.FixedPath);
        }
    }

    private void OnMoveTabLeftClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var tab = GetContextTab(sender);
        if (ViewModel is not null && tab is not null)
        {
            ViewModel.MoveTab(tab, -1);
        }
    }

    private void OnMoveTabRightClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var tab = GetContextTab(sender);
        if (ViewModel is not null && tab is not null)
        {
            ViewModel.MoveTab(tab, 1);
        }
    }

    private async void OnCloseTabClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var tab = GetContextTab(sender);
        if (ViewModel is not null && tab is not null)
        {
            await ViewModel.CloseTabAsync(tab);
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_applyingKeyboardSelection)
        {
            _keyboardSelectionAnchorIndex = FileGrid.SelectedIndex;
            _keyboardSelectionCaretIndex = FileGrid.SelectedIndex;
            _quickLocateMatchIndexes = [];
        }

        if (ViewModel is not null)
        {
            ViewModel.SetSelectedItems(
                FileGrid.SelectedItems.Cast<FileSystemEntry>());
        }
    }

    private void OnFileGridPreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down))
        {
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
            e.Handled = true;
            ExtendFileSelectionFromKeyboard(e.Key == Key.Down ? 1 : -1);
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        e.Handled = true;
        if (TryCycleQuickLocate(e.Key == Key.Down ? 1 : -1))
        {
            return;
        }

        MoveFileGridSelection(e.Key == Key.Down ? 1 : -1);
    }

    private void OnFileGridMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(FileGrid);
    }

    private void OnFileGridMouseRightButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(FileGrid);
        if (ItemsControl.ContainerFromElement(
                FileGrid,
                e.OriginalSource as DependencyObject) is not DataGridRow row)
        {
            FileGrid.SelectedItems.Clear();
            return;
        }

        if (row.IsSelected)
        {
            return;
        }

        FileGrid.SelectedItems.Clear();
        row.IsSelected = true;
    }

    private void OnFileGridContextMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu &&
            ViewModel is not null &&
            Window.GetWindow(this) is MainWindow window)
        {
            window.PopulateContextMenu(menu, ViewModel);
        }
    }

    private async void OnFileGridMouseMove(object sender, MouseEventArgs e)
    {
        var isLeftDrag = e.LeftButton == MouseButtonState.Pressed;
        var isRightDrag = e.RightButton == MouseButtonState.Pressed;
        if ((!isLeftDrag && !isRightDrag) ||
            ViewModel?.SelectedItems is not { Count: > 0 } selected)
        {
            return;
        }

        var current = e.GetPosition(FileGrid);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var files = new System.Collections.Specialized.StringCollection();
        files.AddRange(selected.Select(entry => entry.FullPath).ToArray());
        var data = new DataObject();
        data.SetFileDropList(files);
        if (isRightDrag)
        {
            data.SetData(RightDragDataFormat, true);
        }

        var result = DragDrop.DoDragDrop(
            FileGrid,
            data,
            DragDropEffects.Copy |
                DragDropEffects.Move |
                DragDropEffects.Link);
        if (result == DragDropEffects.Move)
        {
            await ViewModel.RefreshCurrentAsync();
        }
    }

    private void OnFileGridDragOver(object sender, DragEventArgs e)
    {
        var isRightDrag = e.Data.GetDataPresent(RightDragDataFormat);
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) ||
            ViewModel is null ||
            !Directory.Exists(ViewModel.CurrentPath) ||
            e.Data.GetData(DataFormats.FileDrop) is not string[] paths ||
            (!isRightDrag && FileDropGuards.IsSameDirectoryDrop(
                GetDropDestinationDirectory(e),
                paths)))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var destinationDirectory = GetDropDestinationDirectory(e);
        var isFolderTarget = !FileDropGuards.IsSamePath(
            ViewModel.CurrentPath,
            destinationDirectory);
        e.Effects = isRightDrag
            ? DragDropEffects.Link
            : GetLeftDropEffect(e.KeyStates, isFolderTarget);
        e.Handled = true;
    }

    private async void OnFileGridDrop(object sender, DragEventArgs e)
    {
        if (ViewModel is null ||
            e.Data.GetData(DataFormats.FileDrop) is not string[] paths ||
            Window.GetWindow(this) is not MainWindow window)
        {
            return;
        }

        var isRightDrag = e.Data.GetDataPresent(RightDragDataFormat);
        var destinationDirectory = GetDropDestinationDirectory(e);
        if (!isRightDrag && FileDropGuards.IsSameDirectoryDrop(
                destinationDirectory,
                paths))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Handled = true;
        if (isRightDrag)
        {
            e.Effects = DragDropEffects.Link;
            await window.HandleRightFileDropAsync(
                ViewModel,
                paths,
                destinationDirectory);
            return;
        }

        var isFolderTarget = !FileDropGuards.IsSamePath(
            ViewModel.CurrentPath,
            destinationDirectory);
        var move = GetLeftDropEffect(e.KeyStates, isFolderTarget) ==
            DragDropEffects.Move;
        e.Effects = move ? DragDropEffects.Move : DragDropEffects.Copy;
        await window.HandleFileDropAsync(
            ViewModel,
            paths,
            move,
            destinationDirectory: destinationDirectory);
    }

    private string GetDropDestinationDirectory(DragEventArgs e)
    {
        if (ViewModel is null)
        {
            return string.Empty;
        }

        var row = ItemsControl.ContainerFromElement(
            FileGrid,
            e.OriginalSource as DependencyObject) as DataGridRow;
        var candidate = row?.DataContext is FileSystemEntry
            {
                Kind: EntryKind.Directory,
                FullPath: var path,
            }
            ? path
            : null;
        return FileDropGuards.ResolveDropDirectory(
            ViewModel.CurrentPath,
            candidate);
    }

    private static DragDropEffects GetLeftDropEffect(
        DragDropKeyStates keyStates,
        bool isFolderTarget)
    {
        if ((keyStates & DragDropKeyStates.ControlKey) != 0)
        {
            return DragDropEffects.Copy;
        }

        if ((keyStates & DragDropKeyStates.ShiftKey) != 0)
        {
            return DragDropEffects.Move;
        }

        return isFolderTarget
            ? DragDropEffects.Move
            : DragDropEffects.Copy;
    }

    private void OnSorting(object sender, DataGridSortingEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        e.Handled = true;
        var column = e.Column.SortMemberPath switch
        {
            "Name" => FileSortColumn.Name,
            "ModifiedAt" => FileSortColumn.ModifiedAt,
            "TypeDisplayName" => FileSortColumn.Type,
            "Size" => FileSortColumn.Size,
            _ => FileSortColumn.Name,
        };

        ViewModel.SortBy(column);
    }

    private static FileTabViewModel? GetContextTab(object sender)
    {
        if (sender is not MenuItem menuItem ||
            menuItem.Parent is not ContextMenu contextMenu ||
            contextMenu.PlacementTarget is not FrameworkElement placementTarget)
        {
            return null;
        }

        return placementTarget.DataContext as FileTabViewModel;
    }

    public void FocusAddressBar()
    {
        AddressTextBox.Focus();
        Keyboard.Focus(AddressTextBox);
        AddressTextBox.SelectAll();
    }

    public void FocusFirstFileItem()
    {
        FileGrid.UpdateLayout();
        if (FileGrid.Items.Count == 0)
        {
            FileGrid.Focus();
            Keyboard.Focus(FileGrid);
            return;
        }

        SelectAndFocusFileItem(0);
    }

    public bool TryQuickLocate(string prefix)
    {
        if (ViewModel is null)
        {
            return false;
        }

        _quickLocateMatchIndexes = FileNameQuickLocator.FindMatchIndexes(
            ViewModel.Items,
            prefix);
        if (_quickLocateMatchIndexes.Count == 0)
        {
            return false;
        }

        ViewModel.RequestActivation();
        SelectAndFocusFileItem(_quickLocateMatchIndexes[0]);
        return true;
    }

    public bool TryCycleQuickLocate(int offset)
    {
        if (_quickLocateMatchIndexes.Count == 0 || offset == 0)
        {
            return false;
        }

        var currentMatchIndex = -1;
        for (var index = 0; index < _quickLocateMatchIndexes.Count; index++)
        {
            if (_quickLocateMatchIndexes[index] == FileGrid.SelectedIndex)
            {
                currentMatchIndex = index;
                break;
            }
        }

        var nextMatchIndex = currentMatchIndex < 0
            ? offset > 0 ? 0 : _quickLocateMatchIndexes.Count - 1
            : (currentMatchIndex + offset + _quickLocateMatchIndexes.Count) %
                _quickLocateMatchIndexes.Count;
        SelectAndFocusFileItem(_quickLocateMatchIndexes[nextMatchIndex]);
        return true;
    }

    public bool FocusFileItemByPath(string fullPath)
    {
        if (ViewModel is null || string.IsNullOrWhiteSpace(fullPath))
        {
            return false;
        }

        var index = -1;
        for (var itemIndex = 0; itemIndex < ViewModel.Items.Count; itemIndex++)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(
                    ViewModel.Items[itemIndex].FullPath,
                    fullPath))
            {
                index = itemIndex;
                break;
            }
        }

        if (index < 0 || index >= FileGrid.Items.Count)
        {
            return false;
        }

        SelectAndFocusFileItem(index);
        return true;
    }

    public void MoveFileSelectionFromKeyboard(int offset)
    {
        MoveFileGridSelection(offset);
    }

    public void ExtendFileSelectionFromKeyboard(int offset)
    {
        if (FileGrid.Items.Count == 0 || offset == 0)
        {
            return;
        }

        var currentIndex = FileGrid.SelectedIndex;
        if (_keyboardSelectionAnchorIndex < 0 ||
            _keyboardSelectionAnchorIndex >= FileGrid.Items.Count ||
            _keyboardSelectionCaretIndex < 0 ||
            _keyboardSelectionCaretIndex >= FileGrid.Items.Count ||
            !FileGrid.SelectedItems.Contains(
                FileGrid.Items[_keyboardSelectionAnchorIndex]))
        {
            var initialIndex = currentIndex >= 0
                ? currentIndex
                : offset > 0
                    ? 0
                    : FileGrid.Items.Count - 1;
            _keyboardSelectionAnchorIndex = initialIndex;
            _keyboardSelectionCaretIndex = initialIndex;
        }

        var nextIndex = Math.Clamp(
            _keyboardSelectionCaretIndex + offset,
            0,
            FileGrid.Items.Count - 1);
        _applyingKeyboardSelection = true;
        try
        {
            FileGrid.SelectedItems.Clear();
            for (var index = Math.Min(
                     _keyboardSelectionAnchorIndex,
                     nextIndex);
                 index <= Math.Max(
                     _keyboardSelectionAnchorIndex,
                     nextIndex);
                 index++)
            {
                FileGrid.SelectedItems.Add(FileGrid.Items[index]);
            }

            FileGrid.CurrentItem = FileGrid.Items[nextIndex];
            _keyboardSelectionCaretIndex = nextIndex;
            if (ViewModel is not null)
            {
                ViewModel.UpdateSelectedItems(
                    FileGrid.SelectedItems.Cast<FileSystemEntry>());
            }
        }
        finally
        {
            _applyingKeyboardSelection = false;
        }

        FocusFileItem(nextIndex);
    }

    private void MoveFileGridSelection(int offset)
    {
        if (FileGrid.Items.Count == 0)
        {
            FileGrid.Focus();
            Keyboard.Focus(FileGrid);
            return;
        }

        var currentIndex = FileGrid.SelectedIndex;
        if (currentIndex < 0)
        {
            currentIndex = offset > 0
                ? -1
                : FileGrid.Items.Count;
        }

        SelectAndFocusFileItem(Math.Clamp(
            currentIndex + offset,
            0,
            FileGrid.Items.Count - 1));
    }

    private void SelectAndFocusFileItem(int index)
    {
        _applyingKeyboardSelection = true;
        try
        {
            FileGrid.SelectedItems.Clear();
            FileGrid.SelectedIndex = index;
            FileGrid.CurrentItem = FileGrid.Items[index];
            _keyboardSelectionAnchorIndex = index;
            _keyboardSelectionCaretIndex = index;
        }
        finally
        {
            _applyingKeyboardSelection = false;
        }

        FocusFileItem(index);
    }

    private void FocusFileItem(int index)
    {
        if (FileGrid.Columns.Count > 0)
        {
            FileGrid.CurrentColumn = FileGrid.Columns[0];
        }

        FileGrid.ScrollIntoView(
            FileGrid.Items[index],
            FileGrid.CurrentColumn);
        FileGrid.UpdateLayout();
        if (FileGrid.ItemContainerGenerator.ContainerFromIndex(index)
            is DataGridRow row)
        {
            row.Focus();
            Keyboard.Focus(row);
        }
        else
        {
            FileGrid.Focus();
            Keyboard.Focus(FileGrid);
        }
    }

    public bool PasteClipboardIntoAddressBar()
    {
        if (!TryGetAddressClipboardText(out var clipboardText))
        {
            return false;
        }

        AddressTextBox.SelectedText = clipboardText;
        AddressTextBox.CaretIndex = AddressTextBox.SelectionStart +
            clipboardText.Length;
        AddressTextBox.SelectionLength = 0;
        return true;
    }

    private static bool TryGetAddressClipboardText(out string text)
    {
        try
        {
            if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
            {
                text = Clipboard.GetText(TextDataFormat.UnicodeText);
                return !string.IsNullOrEmpty(text);
            }

            if (Clipboard.ContainsFileDropList())
            {
                var paths = Clipboard.GetFileDropList();
                if (paths.Count > 0)
                {
                    text = paths[0]!;
                    return !string.IsNullOrEmpty(text);
                }
            }
        }
        catch (ExternalException)
        {
        }

        text = string.Empty;
        return false;
    }

    private static bool HasButtonAncestor(DependencyObject? source)
    {
        for (var current = source;
             current is not null;
             current = GetParent(current))
        {
            if (current is Button)
            {
                return true;
            }
        }

        return false;
    }

    private static DependencyObject? GetParent(DependencyObject child)
    {
        if (child is Visual)
        {
            return VisualTreeHelper.GetParent(child);
        }

        return LogicalTreeHelper.GetParent(child);
    }

    private sealed record TabDragPayload(
        FilePaneViewModel SourcePane,
        FileTabViewModel Tab);
}

using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using MYTC.App.Dialogs;
using MYTC.App.Menus;
using MYTC.App.Shortcuts;
using MYTC.App.ViewModels;
using MYTC.App.Views;
using MYTC.App.Windows;
using MYTC.Application.Abstractions;
using MYTC.Application.Files;
using MYTC.Domain.Configuration;
using MYTC.Domain.Files;
using MYTC.Domain.Operations;
using MYTC.Application.Updates;
using MYTC.Infrastructure.Updates;

namespace MYTC.App;

public partial class MainWindow
{
    private MainWindowViewModel? _subscribedViewModel;
    private readonly ShortcutManager _shortcutManager;
    private readonly IContextMenuStore _contextMenuStore;
    private readonly ITabContextMenuStore _tabContextMenuStore;
    private readonly IUiPreferencesStore _uiPreferencesStore;
    private readonly IShortcutCreationService _shortcutCreationService;
    private readonly IOpenWithService _openWithService;
    private readonly IPropertiesService _propertiesService;
    private readonly IArchiveExtractionService _archiveExtractionService;
    private readonly IAutoStartService _autoStartService;
    private readonly IManagedRecycleService _managedRecycleService;
    private readonly IRecycleBinRestoreService _recycleBinRestoreService;
    private readonly SemaphoreSlim _uiPreferenceSaveGate = new(1, 1);
    private readonly Func<bool, int, bool>? _deleteConfirmationOverride;
    private ContextMenuConfiguration _contextMenuConfiguration =
        new(ContextMenuConfiguration.CurrentSchemaVersion, []);
    private TabContextMenuConfiguration _tabContextMenuConfiguration =
        new(TabContextMenuConfiguration.CurrentSchemaVersion, []);
    private UiPreferences _uiPreferences = UiPreferences.CreateDefault();
    private readonly Stack<RecycleDeletionBatch> _recycleUndoStack = [];
    private readonly System.Windows.Threading.DispatcherTimer _quickLocateTimer;
    private bool _deleteKeyReady = true;
    private bool _suppressAltMenuActivation;
    private bool _allowClose;
    private FilePaneViewModel? _fileListKeyboardPane;
    private string _quickLocatePrefix = string.Empty;

    public MainWindow(
        IShortcutStore shortcutStore,
        IContextMenuStore contextMenuStore,
        ITabContextMenuStore tabContextMenuStore,
        IUiPreferencesStore uiPreferencesStore,
        IShortcutCreationService shortcutCreationService,
        IOpenWithService openWithService,
        IPropertiesService propertiesService,
        IArchiveExtractionService archiveExtractionService,
        IAutoStartService autoStartService,
        IManagedRecycleService managedRecycleService,
        IRecycleBinRestoreService recycleBinRestoreService,
        Func<bool, int, bool>? deleteConfirmationOverride = null)
    {
        _shortcutManager = new ShortcutManager(shortcutStore);
        _contextMenuStore = contextMenuStore;
        _tabContextMenuStore = tabContextMenuStore;
        _uiPreferencesStore = uiPreferencesStore;
        _shortcutCreationService = shortcutCreationService;
        _openWithService = openWithService;
        _propertiesService = propertiesService;
        _archiveExtractionService = archiveExtractionService;
        _autoStartService = autoStartService;
        _managedRecycleService = managedRecycleService;
        _recycleBinRestoreService = recycleBinRestoreService;
        _deleteConfirmationOverride = deleteConfirmationOverride;
        InitializeComponent();
        _quickLocateTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.2),
        };
        _quickLocateTimer.Tick += (_, _) => ClearQuickLocatePrefix();
        SourceInitialized += (_, _) =>
            AdaptiveWindowPlacement.FitInitialWindowToWorkingArea(
                this,
                desiredWidth: 1440,
                desiredHeight: 900,
                requestedMinimumWidth: 640,
                requestedMinimumHeight: 400);
        VersionTextBlock.Text =
            $"v{typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "未知"}";
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => ApplyLayoutRatios();
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    public void ApplyWorkspaceAppearance(
        string? workspaceName,
        string? configuredIconKey)
    {
        Icon = WorkspaceIconCatalog.CreateImage(
            workspaceName,
            configuredIconKey);
        TaskbarItemInfo ??= new System.Windows.Shell.TaskbarItemInfo();
        TaskbarItemInfo.Overlay = WorkspaceIconCatalog.CreateTaskbarOverlay(
            workspaceName,
            configuredIconKey);
        Title = string.IsNullOrWhiteSpace(workspaceName)
            ? "MYTC"
            : $"MYTC - {workspaceName}";
        _ = TaskbarIdentity.TryApplyWindowProperties(this, workspaceName);
    }

    public string? PreferredWorkspaceName => _uiPreferences.LastWorkspaceName;

    public async Task ConfirmWinRarExecutableAsync()
    {
        if (_uiPreferences.HasConfirmedWinRarPath)
        {
            return;
        }

        var suggestedPath = _archiveExtractionService.FindSuggestedExecutablePath();
        if (!string.IsNullOrWhiteSpace(suggestedPath))
        {
            var useSuggested = MessageBox.Show(
                this,
                $"检测到 WinRAR：\n{suggestedPath}\n\n是否使用这个位置？",
                "WinRAR 设置",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (useSuggested == MessageBoxResult.Yes)
            {
                _uiPreferences = _uiPreferences with
                {
                    HasConfirmedWinRarPath = true,
                    WinRarExecutablePath = suggestedPath,
                };
                await SaveUiPreferencesAsync(_uiPreferences);
                return;
            }
        }
        else if (MessageBox.Show(
                     this,
                     "未检测到 WinRAR。是否现在手动选择 WinRAR.exe？",
                     "WinRAR 设置",
                     MessageBoxButton.YesNo,
                     MessageBoxImage.Question) == MessageBoxResult.No)
        {
            _uiPreferences = _uiPreferences with
            {
                HasConfirmedWinRarPath = true,
                WinRarExecutablePath = null,
            };
            await SaveUiPreferencesAsync(_uiPreferences);
            return;
        }

        var fileDialog = new OpenFileDialog
        {
            Title = "选择 WinRAR.exe",
            Filter = "WinRAR.exe|WinRAR.exe|应用程序|*.exe",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (fileDialog.ShowDialog(this) != true)
        {
            return;
        }

        _uiPreferences = _uiPreferences with
        {
            HasConfirmedWinRarPath = true,
            WinRarExecutablePath = fileDialog.FileName,
        };
        await SaveUiPreferencesAsync(_uiPreferences);
    }

    public async Task InitializeSettingsAsync()
    {
        var contextMenuTask = _contextMenuStore.LoadAsync();
        var tabContextMenuTask = _tabContextMenuStore.LoadAsync();
        var uiPreferencesTask = _uiPreferencesStore.LoadAsync();
        await _shortcutManager.InitializeAsync();
        _contextMenuConfiguration = await contextMenuTask;
        _tabContextMenuConfiguration = await tabContextMenuTask;
        _uiPreferences = await uiPreferencesTask;
        try
        {
            _uiPreferences = _uiPreferences with
            {
                StartWithWindows = _autoStartService.IsEnabled(),
            };
        }
        catch
        {
            // Keep the saved preference if the Run key cannot currently be read.
        }

        ApplyOperationToolbarPreference(
            _uiPreferences.IsOperationToolbarVisible);
        ApplyWorkspaceToolbarPreference(
            _uiPreferences.IsWorkspaceToolbarVisible);
        ApplySettingsToolbarPreference(
            _uiPreferences.IsSettingsToolbarVisible);
    }

    public async Task HandleExternalLaunchAsync(
        string? openPath,
        string? workspaceName = null)
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        if (!IsVisible)
        {
            Show();
        }

        BringWindowToForeground();

        if (ViewModel is not null)
        {
            if (!string.IsNullOrWhiteSpace(workspaceName))
            {
                _ = await ViewModel.LoadWorkspaceAsync(workspaceName);
            }

            if (!string.IsNullOrWhiteSpace(openPath))
            {
                await ViewModel.OpenExternalPathAsync(openPath);
            }
        }
    }

    private async void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var eventKey = GetEventKey(e);
        var eventModifiers = GetEventModifiers(e);
        if (await TryHandleFileListKeyboardCommandAsync(
                e,
                eventKey,
                eventModifiers))
        {
            return;
        }

        SynchronizeFocusedFilePaneSelection();

        if (eventModifiers == ModifierKeys.None &&
            eventKey == Key.Enter &&
            !e.IsRepeat &&
            TryGetFocusedFileGrid(out var focusedPane, out var focusedGrid) &&
            focusedGrid.SelectedItem is FileSystemEntry entry)
        {
            e.Handled = true;
            focusedPane.RequestActivation();
            focusedPane.SetSelectedItems(
                focusedGrid.SelectedItems.Cast<FileSystemEntry>());
            var previousPath = focusedPane.CurrentPath;
            await focusedPane.OpenEntryAsync(entry);
            if (!StringComparer.OrdinalIgnoreCase.Equals(
                    previousPath,
                    focusedPane.CurrentPath))
            {
                await Dispatcher.InvokeAsync(
                    () => RestoreFileListKeyboardNavigation(focusedPane),
                    System.Windows.Threading.DispatcherPriority.ContextIdle);
            }

            return;
        }

        var isPhysicalDelete = eventKey == Key.Delete;
        if (e.IsRepeat && !isPhysicalDelete ||
            Keyboard.FocusedElement is TextBox or ComboBox &&
            !_shortcutManager.IsExactBinding(
                e,
                ShortcutAction.FocusAddressBar))
        {
            return;
        }

        var match = _shortcutManager.Process(e);
        if (!match.Handled)
        {
            return;
        }

        e.Handled = true;
        if (match.Waiting)
        {
            ViewModel.SetStatusMessage(match.StatusText);
            return;
        }

        if (match.Action is { } action)
        {
            if (action == ShortcutAction.FocusAddressBar &&
                (e.Key == Key.System ||
                 eventModifiers.HasFlag(ModifierKeys.Alt)))
            {
                _suppressAltMenuActivation = true;
            }

            var isDeleteAction = action is
                ShortcutAction.RecycleDelete or
                ShortcutAction.PermanentDelete;
            if (isPhysicalDelete && isDeleteAction)
            {
                if (!_deleteKeyReady)
                {
                    return;
                }

                _deleteKeyReady = false;
                try
                {
                    await ExecuteShortcutAsync(action);
                }
                finally
                {
                    await WaitForDeleteKeyReleaseAsync();
                    _deleteKeyReady = true;
                }

                return;
            }

            await ExecuteShortcutAsync(action);
        }
    }

    private void OnWindowPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (ViewModel?.ActivePane is null ||
            string.IsNullOrWhiteSpace(e.Text) ||
            e.Text.All(char.IsControl) ||
            IsFileListKeyboardInputFocused() ||
            HasCommandModifier())
        {
            return;
        }

        var control = FindActiveFilePaneControl();
        if (control is null)
        {
            return;
        }

        _fileListKeyboardPane = ViewModel.ActivePane;
        var candidate = _quickLocatePrefix + e.Text;
        if (!control.TryQuickLocate(candidate))
        {
            candidate = e.Text;
            if (!control.TryQuickLocate(candidate))
            {
                ClearQuickLocatePrefix();
                e.Handled = true;
                return;
            }
        }

        _quickLocatePrefix = candidate;
        _quickLocateTimer.Stop();
        _quickLocateTimer.Start();
        e.Handled = true;
    }

    private void OnWindowKeyUp(object sender, KeyEventArgs e)
    {
        var eventKey = GetEventKey(e);
        if (eventKey == Key.Delete)
        {
            _deleteKeyReady = true;
        }

        if (!_suppressAltMenuActivation ||
            eventKey is not (Key.LeftAlt or Key.RightAlt))
        {
            return;
        }

        _suppressAltMenuActivation = false;
        e.Handled = true;
        _ = Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            FocusActivePaneAddressBar);
    }

    private async Task ExecuteShortcutAsync(ShortcutAction action)
    {
        if (ViewModel is null)
        {
            return;
        }

        switch (action)
        {
            case ShortcutAction.CopyToTarget:
                await TransferToTargetAsync(FileOperationKind.Copy);
                break;
            case ShortcutAction.MoveToTarget:
                await TransferToTargetAsync(FileOperationKind.Move);
                break;
            case ShortcutAction.CreateDirectory:
                await CreateDirectoryAsync();
                break;
            case ShortcutAction.RecycleDelete:
                await DeleteAsync(permanent: false);
                break;
            case ShortcutAction.PermanentDelete:
                await DeleteAsync(permanent: true);
                break;
            case ShortcutAction.Rename:
                await RenameAsync();
                break;
            case ShortcutAction.CopyToClipboard:
                CopySelectionToClipboard(move: false);
                break;
            case ShortcutAction.CutToClipboard:
                CopySelectionToClipboard(move: true);
                break;
            case ShortcutAction.PasteFromClipboard:
                await PasteFromClipboardAsync();
                break;
            case ShortcutAction.ActivatePane1:
            case ShortcutAction.ActivatePane2:
            case ShortcutAction.ActivatePane3:
            case ShortcutAction.ActivatePane4:
                ViewModel.ActivatePaneByIdCommand.Execute(action switch
                {
                    ShortcutAction.ActivatePane1 => "top-left",
                    ShortcutAction.ActivatePane2 => "top-right",
                    ShortcutAction.ActivatePane3 => "bottom-left",
                    _ => "bottom-right",
                });
                break;
            case ShortcutAction.NewTab:
                ViewModel.ActivePane?.NewTabCommand.Execute(null);
                break;
            case ShortcutAction.CloseTab:
                if (ViewModel.ActivePane is { } closePane)
                {
                    await closePane.CloseTabAsync(closePane.ActiveTab);
                }

                break;
            case ShortcutAction.RestoreClosedTab:
                ViewModel.ActivePane?.RestoreClosedTabCommand.Execute(null);
                break;
            case ShortcutAction.RestoreFourPanes:
                ViewModel.RestoreLayoutCommand.Execute(null);
                break;
            case ShortcutAction.FocusAddressBar:
                FocusActivePaneAddressBar();
                _ = Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Input,
                    FocusActivePaneAddressBar);
                break;
            case ShortcutAction.NavigateUp:
                if (ViewModel.ActivePane is { } upPane)
                {
                    var previousPath = upPane.CurrentPath;
                    await upPane.NavigateUpAsync();
                    var restorePath =
                        upPane.ConsumeParentNavigationChildPath();
                    if (!StringComparer.OrdinalIgnoreCase.Equals(
                            previousPath,
                            upPane.CurrentPath))
                    {
                        await Dispatcher.InvokeAsync(
                            () => RestoreFileListKeyboardNavigation(
                                upPane,
                                restorePath),
                            System.Windows.Threading.DispatcherPriority.ContextIdle);
                    }
                }

                break;
            case ShortcutAction.ShowProperties:
                if (ViewModel.ActivePane is { } propertiesPane)
                {
                    ShowProperties(propertiesPane);
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    private async void OnShortcutSettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ShortcutSettingsDialog(_shortcutManager)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await _shortcutManager.SaveAsync(dialog.Result);
            ViewModel?.SetStatusMessage("快捷键设置已保存并立即生效。");
        }
        catch (Exception exception)
        {
            ShowOperationError("保存快捷键失败", exception);
        }
    }

    private async void OnContextMenuSettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContextMenuSettingsDialog(
            _contextMenuConfiguration,
            _contextMenuStore)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true || dialog.Result is null)
        {
            return;
        }

        try
        {
            await _contextMenuStore.SaveAsync(dialog.Result);
            _contextMenuConfiguration = dialog.Result;
            ViewModel?.SetStatusMessage("右键菜单设置已保存并立即生效。");
        }
        catch (Exception exception)
        {
            ShowOperationError("保存右键菜单失败", exception);
        }
    }

    private async void OnTabContextMenuSettingsClick(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new TabContextMenuSettingsDialog(
            _tabContextMenuConfiguration,
            _tabContextMenuStore)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true || dialog.Result is null)
        {
            return;
        }

        try
        {
            await _tabContextMenuStore.SaveAsync(dialog.Result);
            _tabContextMenuConfiguration = dialog.Result;
            ViewModel?.SetStatusMessage(
                "标签右键菜单设置已保存并立即生效。");
        }
        catch (Exception exception)
        {
            ShowOperationError("保存标签右键菜单失败", exception);
        }
    }

    public void PopulateContextMenu(
        ContextMenu menu,
        FilePaneViewModel pane)
    {
        InputMethod.SetIsInputMethodEnabled(menu, false);
        menu.Items.Clear();
        var visibleItems = _contextMenuConfiguration.Items
            .Where(item => item.IsVisible)
            .ToArray();
        AddContextMenuDefinitions(
            menu.Items,
            menu,
            pane,
            visibleItems.Where(item =>
                string.IsNullOrWhiteSpace(item.ParentId)));
    }

    private void AddContextMenuDefinitions(
        ItemCollection targetItems,
        ContextMenu rootMenu,
        FilePaneViewModel pane,
        IEnumerable<ContextMenuItemDefinition> definitions)
    {
        var previousWasSeparator = true;
        foreach (var definition in definitions)
        {
            if (definition.Kind == ContextMenuItemKind.Separator)
            {
                if (!previousWasSeparator)
                {
                    targetItems.Add(new Separator());
                    previousWasSeparator = true;
                }

                continue;
            }

            var item = new MenuItem
            {
                Header = AccessKeyFormatter.ToWpfHeader(definition.Label),
            };
            InputMethod.SetIsInputMethodEnabled(item, false);
            if (definition.Kind == ContextMenuItemKind.Submenu)
            {
                var children = _contextMenuConfiguration.Items
                    .Where(child =>
                        child.IsVisible &&
                        StringComparer.Ordinal.Equals(
                            child.ParentId,
                            definition.Id))
                    .ToArray();
                AddContextMenuDefinitions(
                    item.Items,
                    rootMenu,
                    pane,
                    children);
                item.IsEnabled = item.Items.Count > 0;
            }
            else
            {
                item.IsEnabled = IsContextItemEnabled(definition, pane);
                item.Click += (_, _) =>
                    QueueContextItemExecution(rootMenu, definition, pane);
            }

            targetItems.Add(item);
            previousWasSeparator = false;
        }

        if (targetItems.Count > 0 && targetItems[^1] is Separator)
        {
            targetItems.RemoveAt(targetItems.Count - 1);
        }
    }

    private void QueueContextItemExecution(
        ContextMenu menu,
        ContextMenuItemDefinition definition,
        FilePaneViewModel pane)
    {
        menu.IsOpen = false;
        _ = Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ContextIdle,
            new Action(() =>
                _ = ExecuteContextItemSafelyAsync(definition, pane)));
    }

    private async Task ExecuteContextItemSafelyAsync(
        ContextMenuItemDefinition definition,
        FilePaneViewModel pane)
    {
        try
        {
            await ExecuteContextItemAsync(definition, pane);
        }
        catch (Exception exception)
        {
            ShowOperationError("右键菜单操作失败", exception);
        }
    }

    private bool IsContextItemEnabled(
        ContextMenuItemDefinition definition,
        FilePaneViewModel pane)
    {
        if (definition.Kind == ContextMenuItemKind.ExternalProgram)
        {
            return !string.IsNullOrWhiteSpace(definition.ProgramPath);
        }

        return definition.Action switch
        {
            ContextMenuAction.PasteFromClipboard => Clipboard.ContainsFileDropList(),
            ContextMenuAction.Open => pane.SelectedItems.Count > 0,
            ContextMenuAction.OpenWith =>
                pane.SelectedItems is
                [
                {
                    Kind: EntryKind.File,
                },
                ],
            ContextMenuAction.Properties => pane.SelectedItems.Count == 1,
            ContextMenuAction.ExtractHereWithWinRar or
                ContextMenuAction.ExtractToNamedDirectoryWithWinRar =>
                pane.SelectedItems is
                [
                {
                    Kind: EntryKind.File,
                } archive,
                ] && _archiveExtractionService.CanExtract(
                    archive.FullPath,
                    _uiPreferences.WinRarExecutablePath),
            ContextMenuAction.Rename => pane.SelectedItems.Count == 1,
            ContextMenuAction.UndoDelete => _recycleUndoStack.Count > 0,
            ContextMenuAction.CopyToTarget or
                ContextMenuAction.MoveToTarget or
                ContextMenuAction.CopyToClipboard or
                ContextMenuAction.CutToClipboard or
                ContextMenuAction.RecycleDelete or
                ContextMenuAction.PermanentDelete => pane.SelectedItems.Count > 0,
            _ => true,
        };
    }

    private async Task ExecuteContextItemAsync(
        ContextMenuItemDefinition definition,
        FilePaneViewModel pane)
    {
        pane.RequestActivation();
        if (definition.Kind == ContextMenuItemKind.ExternalProgram)
        {
            LaunchExternalProgram(definition, pane);
            return;
        }

        switch (definition.Action)
        {
            case ContextMenuAction.Open:
                foreach (var entry in pane.SelectedItems.ToArray())
                {
                    await pane.OpenEntryAsync(entry);
                }

                break;
            case ContextMenuAction.OpenWith:
                if (pane.SelectedItems is
                    [
                    {
                        Kind: EntryKind.File,
                    } selectedFile,
                    ])
                {
                    _openWithService.Show(
                        selectedFile.FullPath,
                        new WindowInteropHelper(this).Handle);
                }

                break;
            case ContextMenuAction.Properties:
                ShowProperties(pane);
                break;
            case ContextMenuAction.ExtractHereWithWinRar:
                await ExtractArchiveHereAsync(pane);
                break;
            case ContextMenuAction.ExtractToNamedDirectoryWithWinRar:
                await ExtractArchiveToNamedDirectoryAsync(pane);
                break;
            case ContextMenuAction.CopyToTarget:
                await TransferToTargetAsync(FileOperationKind.Copy);
                break;
            case ContextMenuAction.MoveToTarget:
                await TransferToTargetAsync(FileOperationKind.Move);
                break;
            case ContextMenuAction.CopyToClipboard:
                CopySelectionToClipboard(move: false);
                break;
            case ContextMenuAction.CutToClipboard:
                CopySelectionToClipboard(move: true);
                break;
            case ContextMenuAction.PasteFromClipboard:
                await PasteFromClipboardAsync();
                break;
            case ContextMenuAction.CopyFullPath:
                CopyFullPathToClipboard(pane);
                break;
            case ContextMenuAction.CreateDirectory:
                await CreateDirectoryAsync();
                break;
            case ContextMenuAction.CreateTextDocument:
                await CreateTextDocumentAsync();
                break;
            case ContextMenuAction.Rename:
                await RenameAsync();
                break;
            case ContextMenuAction.RecycleDelete:
                await DeleteAsync(permanent: false);
                break;
            case ContextMenuAction.UndoDelete:
                await UndoDeleteAsync();
                break;
            case ContextMenuAction.PermanentDelete:
                await DeleteAsync(permanent: true);
                break;
            case ContextMenuAction.Refresh:
                await pane.RefreshCurrentAsync();
                ViewModel?.SetStatusMessage("已刷新当前目录");
                break;
        }
    }

    private async Task ExtractArchiveHereAsync(FilePaneViewModel pane)
    {
        if (pane.SelectedItems is not [var archive] ||
            archive.Kind != EntryKind.File)
        {
            return;
        }

        await _archiveExtractionService.ExtractToDirectoryAsync(
            archive.FullPath,
            pane.CurrentPath,
            _uiPreferences.WinRarExecutablePath);
        await pane.RefreshCurrentAsync();
        ViewModel?.SetStatusMessage($"已解压：{archive.Name}");
    }

    private async Task ExtractArchiveToNamedDirectoryAsync(
        FilePaneViewModel pane)
    {
        if (pane.SelectedItems is not [var archive] ||
            archive.Kind != EntryKind.File)
        {
            return;
        }

        await _archiveExtractionService.ExtractToNamedDirectoryAsync(
            archive.FullPath,
            pane.CurrentPath,
            _uiPreferences.WinRarExecutablePath);
        await pane.RefreshCurrentAsync();
        ViewModel?.SetStatusMessage($"已解压到同名文件夹：{archive.Name}");
    }

    public void PopulateTabContextMenu(
        ContextMenu menu,
        FilePaneViewModel pane,
        FileTabViewModel tab)
    {
        menu.Items.Clear();
        var previousWasSeparator = true;
        foreach (var definition in _tabContextMenuConfiguration.Items
                     .Where(item => item.IsVisible))
        {
            if (definition.Kind == TabContextMenuItemKind.Separator)
            {
                if (!previousWasSeparator)
                {
                    menu.Items.Add(new Separator());
                    previousWasSeparator = true;
                }

                continue;
            }

            var item = new MenuItem
            {
                Header = AccessKeyFormatter.ToWpfHeader(definition.Label),
                IsEnabled = IsTabContextItemEnabled(
                    definition.Action,
                    pane,
                    tab),
            };
            item.Click += async (_, _) =>
            {
                try
                {
                    await ExecuteTabContextItemAsync(
                        definition.Action,
                        pane,
                        tab);
                }
                catch (Exception exception)
                {
                    ShowOperationError("标签右键菜单操作失败", exception);
                }
            };
            menu.Items.Add(item);
            previousWasSeparator = false;
        }

        if (menu.Items.Count > 0 && menu.Items[^1] is Separator)
        {
            menu.Items.RemoveAt(menu.Items.Count - 1);
        }
    }

    private bool IsTabContextItemEnabled(
        TabContextMenuAction? action,
        FilePaneViewModel pane,
        FileTabViewModel tab)
    {
        var index = pane.Tabs.IndexOf(tab);
        return action switch
        {
            TabContextMenuAction.CopyToTargetPane =>
                ViewModel?.TargetPane is { } target &&
                !ReferenceEquals(target, pane),
            TabContextMenuAction.MoveLeft => index > 0,
            TabContextMenuAction.MoveRight =>
                index >= 0 && index < pane.Tabs.Count - 1,
            TabContextMenuAction.Close => pane.Tabs.Count > 1,
            _ => true,
        };
    }

    private async Task ExecuteTabContextItemAsync(
        TabContextMenuAction? action,
        FilePaneViewModel pane,
        FileTabViewModel tab)
    {
        pane.RequestActivation();
        switch (action)
        {
            case TabContextMenuAction.PinCurrentDirectory:
                await pane.PinTabToCurrentDirectoryAsync(tab);
                break;
            case TabContextMenuAction.Configure:
                await ConfigureTabAsync(pane, tab);
                break;
            case TabContextMenuAction.CopyToTargetPane:
                if (ViewModel?.TargetPane is { } target &&
                    !ReferenceEquals(target, pane))
                {
                    var copied = await target.CopyTabFromAsync(tab);
                    ViewModel.SetStatusMessage(
                        $"已将标签“{copied.DisplayTitle}”复制到目标窗格。");
                }

                break;
            case TabContextMenuAction.MoveLeft:
                pane.MoveTab(tab, -1);
                break;
            case TabContextMenuAction.MoveRight:
                pane.MoveTab(tab, 1);
                break;
            case TabContextMenuAction.Close:
                await pane.CloseTabAsync(tab);
                break;
        }
    }

    public async Task HandleTabDropAsync(
        FilePaneViewModel sourcePane,
        FilePaneViewModel targetPane,
        FileTabViewModel tab)
    {
        if (ViewModel is null ||
            ReferenceEquals(sourcePane, targetPane) ||
            !sourcePane.Tabs.Contains(tab) ||
            !ViewModel.Panes.Contains(targetPane))
        {
            return;
        }

        targetPane.RequestActivation();
        var copied = await targetPane.CopyTabFromAsync(tab);
        ViewModel.SetStatusMessage(
            $"已将标签“{copied.DisplayTitle}”拖放复制到 {targetPane.Id}。");
    }

    private async Task ConfigureTabAsync(
        FilePaneViewModel pane,
        FileTabViewModel tab)
    {
        var dialog = new TabSettingsDialog(
            tab.CustomTitle,
            tab.IsFixed,
            tab.CurrentPath,
            tab.FixedPath)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() == true)
        {
            await pane.ApplyTabSettingsAsync(
                tab,
                dialog.TabTitle,
                dialog.IsFixed,
                dialog.FixedPath);
        }
    }

    private static void LaunchExternalProgram(
        ContextMenuItemDefinition definition,
        FilePaneViewModel pane)
    {
        var programPath = Environment.ExpandEnvironmentVariables(
            definition.ProgramPath ?? string.Empty);
        var selectedPaths = pane.SelectedItems
            .Select(entry => entry.FullPath)
            .ToArray();
        var firstPath = selectedPaths.FirstOrDefault() ?? string.Empty;
        var arguments = definition.Arguments ?? "{path}";
        arguments = arguments
            .Replace(
                "{paths}",
                string.Join(" ", selectedPaths.Select(QuoteCommandArgument)),
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "{path}",
                QuoteCommandArgument(firstPath),
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "{directory}",
                QuoteCommandArgument(pane.CurrentPath),
                StringComparison.OrdinalIgnoreCase);

        Process.Start(new ProcessStartInfo
        {
            FileName = programPath,
            Arguments = arguments,
            WorkingDirectory = pane.CurrentPath,
            UseShellExecute = true,
        });
    }

    private static string QuoteCommandArgument(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private async void OnCopyToTargetClick(object sender, RoutedEventArgs e)
    {
        await TransferToTargetAsync(FileOperationKind.Copy);
    }

    private async void OnMoveToTargetClick(object sender, RoutedEventArgs e)
    {
        await TransferToTargetAsync(FileOperationKind.Move);
    }

    private async void OnCreateDirectoryClick(object sender, RoutedEventArgs e)
    {
        await CreateDirectoryAsync();
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        await DeleteAsync(permanent: false);
    }

    private async void OnUndoDeleteClick(object sender, RoutedEventArgs e)
    {
        await UndoDeleteAsync();
    }

    private async void OnRenameClick(object sender, RoutedEventArgs e)
    {
        await RenameAsync();
    }

    private void OnCopyClipboardClick(object sender, RoutedEventArgs e)
    {
        CopySelectionToClipboard(move: false);
    }

    private void OnCutClipboardClick(object sender, RoutedEventArgs e)
    {
        CopySelectionToClipboard(move: true);
    }

    private async void OnPasteClipboardClick(object sender, RoutedEventArgs e)
    {
        await PasteFromClipboardAsync();
    }

    private void OnCopyFullPathClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.ActivePane is not { } pane)
        {
            return;
        }

        try
        {
            CopyFullPathToClipboard(pane);
        }
        catch (Exception exception)
        {
            ShowOperationError("复制完整路径失败", exception);
        }
    }

    private async void OnPermanentDeleteClick(object sender, RoutedEventArgs e)
    {
        await DeleteAsync(permanent: true);
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.ActivePane is { } pane)
        {
            await pane.RefreshCurrentAsync();
        }
    }

    private void OnRestoreLayoutClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.RestoreLayoutCommand.Execute(null);
    }

    private async void OnOperationToolbarToggleClick(
        object sender,
        RoutedEventArgs e)
    {
        var isVisible = OperationToolbarMenuItem.IsChecked;
        var previous = _uiPreferences;
        ApplyOperationToolbarPreference(isVisible);
        try
        {
            _uiPreferences = _uiPreferences with
            {
                IsOperationToolbarVisible = isVisible,
            };
            await SaveUiPreferencesAsync(_uiPreferences);
            ViewModel?.SetStatusMessage(
                isVisible ? "操作工具栏已显示。" : "操作工具栏已隐藏。");
        }
        catch (Exception exception)
        {
            _uiPreferences = previous;
            ApplyOperationToolbarPreference(!isVisible);
            ShowOperationError("保存界面设置失败", exception);
        }
    }

    private async void OnWorkspaceToolbarToggleClick(
        object sender,
        RoutedEventArgs e)
    {
        await SetWorkspaceToolbarVisibleAsync(
            WorkspaceToolbarMenuItem.IsChecked);
    }

    private async void OnSettingsToolbarToggleClick(
        object sender,
        RoutedEventArgs e)
    {
        await SetSettingsToolbarVisibleAsync(
            SettingsToolbarMenuItem.IsChecked);
    }

    private async void OnGlobalSettingsClick(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new GlobalSettingsDialog(
            _uiPreferences.StartWithWindows,
            _uiPreferences.ConfirmRecycleDelete,
            _uiPreferences.WinRarExecutablePath)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var previous = _uiPreferences;
        try
        {
            _autoStartService.SetEnabled(dialog.StartWithWindows);
            _uiPreferences = _uiPreferences with
            {
                StartWithWindows = dialog.StartWithWindows,
                ConfirmRecycleDelete = dialog.ConfirmRecycleDelete,
                HasConfirmedWinRarPath = true,
                WinRarExecutablePath = dialog.WinRarExecutablePath,
            };
            await SaveUiPreferencesAsync(_uiPreferences);
            ViewModel?.SetStatusMessage("全局设置已保存并立即生效。");
        }
        catch (Exception exception)
        {
            _uiPreferences = previous;
            try
            {
                _autoStartService.SetEnabled(previous.StartWithWindows);
            }
            catch
            {
                // Preserve the original error shown below.
            }

            ShowOperationError("保存全局设置失败", exception);
        }
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnShellIntegrationClick(
        object sender,
        RoutedEventArgs e)
    {
        var maintenancePath = Path.Combine(
            AppContext.BaseDirectory,
            PortableUpdateConstants.MaintenanceExecutableName);
        if (!File.Exists(maintenancePath))
        {
            MessageBox.Show(
                this,
                "当前程序目录缺少 MYTC.Maintenance.exe。\n\n" +
                "请使用完整的 MYTC 生产发布包。",
                "无法启动 Win+E 设置工具",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = maintenancePath,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
            });
        }
        catch (Exception exception)
        {
            ShowOperationError("启动 Win+E 设置工具失败", exception);
        }
    }

    private async void OnInstallUpdateClick(
        object sender,
        RoutedEventArgs e)
    {
        if (ViewModel?.IsOperationRunning == true)
        {
            MessageBox.Show(
                this,
                "当前有文件操作正在执行。请等待操作完成后再升级。",
                "暂时不能升级",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!InstallationPathPolicy.IsSupportedFixedLocalPath(
                AppContext.BaseDirectory,
                out var pathReason))
        {
            MessageBox.Show(
                this,
                pathReason,
                "当前安装位置不支持就地升级",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var updateDirectory = @"T:\mytc-updates";
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 MYTC 升级包",
            Filter = "MYTC 升级包 (*.zip)|*.zip",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = Directory.Exists(updateDirectory)
                ? updateDirectory
                : null,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var localUpdateRoot = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "MYTC",
            "updates");
        PreparedPortableUpdate? prepared = null;
        var handedOffToUpdater = false;
        try
        {
            IsEnabled = false;
            ViewModel?.SetStatusMessage("正在校验并暂存升级包…");
            var currentVersion =
                typeof(MainWindow).Assembly.GetName().Version
                ?? new Version(0, 0);
            prepared =
                await new PortableUpdatePackageService().PrepareAsync(
                    dialog.FileName,
                    Path.Combine(localUpdateRoot, "staging"),
                    currentVersion);

            var confirmation = MessageBox.Show(
                this,
                $"升级包已通过完整性校验。\n\n" +
                $"当前版本：{currentVersion}\n" +
                $"目标版本：{prepared.Version}\n\n" +
                "升级时 MYTC 会自动退出，外部更新器将备份旧程序、保留 data 配置目录，完成后自动重启。\n\n" +
                "现在开始升级吗？",
                "确认升级 MYTC",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                ViewModel?.SetStatusMessage("已取消升级。");
                return;
            }

            var maintenancePath = Path.Combine(
                AppContext.BaseDirectory,
                PortableUpdateConstants.MaintenanceExecutableName);
            if (!File.Exists(maintenancePath))
            {
                throw new FileNotFoundException(
                    "当前程序目录缺少 MYTC.Maintenance.exe，无法安全执行就地升级。",
                    maintenancePath);
            }

            var updaterHostDirectory = Path.Combine(
                localUpdateRoot,
                "updater-hosts",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(updaterHostDirectory);
            var updaterHost = Path.Combine(
                updaterHostDirectory,
                PortableUpdateConstants.MaintenanceExecutableName);
            File.Copy(
                maintenancePath,
                updaterHost,
                overwrite: false);

            var startInfo = new ProcessStartInfo
            {
                FileName = updaterHost,
                WorkingDirectory = updaterHostDirectory,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("--apply-update");
            startInfo.ArgumentList.Add("--install-root");
            startInfo.ArgumentList.Add(AppContext.BaseDirectory);
            startInfo.ArgumentList.Add("--staged-root");
            startInfo.ArgumentList.Add(prepared.StagedRoot);
            startInfo.ArgumentList.Add("--pid");
            startInfo.ArgumentList.Add(
                Environment.ProcessId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
            _ = Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                    "无法启动 MYTC 外部更新器。");

            handedOffToUpdater = true;
            ViewModel?.SetStatusMessage("升级器已启动，正在退出 MYTC…");
            Close();
        }
        catch (Exception exception)
        {
            ShowOperationError("升级包处理失败", exception);
            ViewModel?.SetStatusMessage("升级未开始，现有程序未被修改。");
        }
        finally
        {
            if (!handedOffToUpdater && prepared is not null)
            {
                TryDeleteStagedUpdate(
                    prepared.StagedRoot,
                    Path.Combine(localUpdateRoot, "staging"));
            }

            IsEnabled = true;
        }
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        var version = typeof(MainWindow).Assembly
            .GetName()
            .Version?
            .ToString(3) ?? "未知";
        MessageBox.Show(
            this,
            $"MYTC v{version}\nWindows 10/11 四窗格绿色资源管理器",
            "关于 MYTC",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OnShowFailuresClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || ViewModel.OperationFailures.Count == 0)
        {
            MessageBox.Show(
                this,
                "最近一次文件操作没有失败项目。",
                "文件操作结果",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var lines = ViewModel.OperationFailures
            .Take(20)
            .Select(failure => $"{failure.Path}\n  {failure.Message}");
        var suffix = ViewModel.OperationFailures.Count > 20
            ? $"\n\n其余 {ViewModel.OperationFailures.Count - 20} 项未显示。"
            : string.Empty;
        MessageBox.Show(
            this,
            string.Join("\n\n", lines) + suffix,
            "失败项目",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    public async Task HandleFileDropAsync(
        FilePaneViewModel destinationPane,
        IReadOnlyList<string> sourcePaths,
        bool move,
        bool allowSameDirectory = false)
    {
        if (ViewModel is null || ViewModel.IsOperationRunning)
        {
            return;
        }

        if (!allowSameDirectory && FileDropGuards.IsSameDirectoryDrop(
                destinationPane.CurrentPath,
                sourcePaths))
        {
            ViewModel.SetStatusMessage("源目录与目标目录相同，已取消拖放。");
            return;
        }

        var behavior = ChooseCollisionBehavior(destinationPane, sourcePaths);
        if (behavior is null)
        {
            return;
        }

        try
        {
            var sourcePane = ViewModel.Panes.FirstOrDefault(pane =>
                sourcePaths.Any(path =>
                    StringComparer.OrdinalIgnoreCase.Equals(
                        Path.GetDirectoryName(path),
                        pane.CurrentPath)));
            var result = await ViewModel.ExecutePathsToPaneAsync(
                destinationPane,
                sourcePaths,
                move ? FileOperationKind.Move : FileOperationKind.Copy,
                behavior.Value,
                sourcePane);
            ShowFailuresIfNeeded(result);
        }
        catch (Exception exception)
        {
            ShowOperationError("拖放操作失败", exception);
        }
    }

    public async Task HandleRightFileDropAsync(
        FilePaneViewModel destinationPane,
        IReadOnlyList<string> sourcePaths)
    {
        if (ViewModel is null ||
            ViewModel.IsOperationRunning ||
            sourcePaths.Count == 0)
        {
            return;
        }

        var isSameDirectory = FileDropGuards.IsSameDirectoryDrop(
            destinationPane.CurrentPath,
            sourcePaths);
        var choice = await ShowRightDragMenuAsync(
            includeMove: !isSameDirectory);
        switch (choice)
        {
            case RightDragChoice.Copy:
                await HandleFileDropAsync(
                    destinationPane,
                    sourcePaths,
                    move: false,
                    allowSameDirectory: true);
                break;
            case RightDragChoice.Move:
                await HandleFileDropAsync(
                    destinationPane,
                    sourcePaths,
                    move: true);
                break;
            case RightDragChoice.CreateShortcut:
                try
                {
                    var created = await _shortcutCreationService.CreateAsync(
                        sourcePaths,
                        destinationPane.CurrentPath);
                    await destinationPane.RefreshCurrentAsync();
                    ViewModel.SetStatusMessage(
                        $"已在目标窗格创建 {created.Count} 个快捷方式。");
                }
                catch (Exception exception)
                {
                    ShowOperationError("创建快捷方式失败", exception);
                }

                break;
        }
    }

    private Task<RightDragChoice?> ShowRightDragMenuAsync(bool includeMove = true)
    {
        var completion =
            new TaskCompletionSource<RightDragChoice?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var menu = new ContextMenu
        {
            Placement = PlacementMode.MousePoint,
            PlacementTarget = this,
        };

        AddChoice("复制到此处", RightDragChoice.Copy);
        if (includeMove)
        {
            AddChoice("移动到此处", RightDragChoice.Move);
        }
        menu.Items.Add(new Separator());
        AddChoice(
            "在当前位置创建快捷方式",
            RightDragChoice.CreateShortcut);
        menu.Items.Add(new Separator());
        var cancel = new MenuItem { Header = "取消" };
        cancel.Click += (_, _) =>
        {
            completion.TrySetResult(null);
            menu.IsOpen = false;
        };
        menu.Items.Add(cancel);
        menu.Closed += (_, _) => completion.TrySetResult(null);
        menu.IsOpen = true;
        return completion.Task;

        void AddChoice(string label, RightDragChoice choice)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) =>
            {
                completion.TrySetResult(choice);
                menu.IsOpen = false;
            };
            menu.Items.Add(item);
        }
    }

    private async Task TransferToTargetAsync(FileOperationKind kind)
    {
        if (ViewModel is null || ViewModel.IsOperationRunning ||
            ViewModel.ActivePane is null ||
            ViewModel.TargetPane is null)
        {
            return;
        }

        var paths = ViewModel.ActivePane.SelectedItems
            .Select(entry => entry.FullPath)
            .ToArray();
        if (paths.Length == 0)
        {
            MessageBox.Show(
                this,
                "请先在活动窗格中选择文件或文件夹。",
                "MYTC",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var behavior = ChooseCollisionBehavior(ViewModel.TargetPane, paths);
        if (behavior is null)
        {
            return;
        }

        try
        {
            var result = await ViewModel.TransferSelectionAsync(kind, behavior.Value);
            ShowFailuresIfNeeded(result);
        }
        catch (Exception exception)
        {
            ShowOperationError(
                kind == FileOperationKind.Copy ? "复制失败" : "移动失败",
                exception);
        }
    }

    private async Task CreateDirectoryAsync()
    {
        if (ViewModel is null || ViewModel.IsOperationRunning)
        {
            return;
        }

        var dialog = new TextInputDialog("新建文件夹", "文件夹名称", "新建文件夹")
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var created = await ViewModel.CreateDirectoryAsync(dialog.Value);
            if (created is not null)
            {
                await Dispatcher.InvokeAsync(
                    () => FindActiveFilePaneControl()?.FocusFileItemByPath(created),
                    System.Windows.Threading.DispatcherPriority.ContextIdle);
            }
        }
        catch (Exception exception)
        {
            ShowOperationError("新建文件夹失败", exception);
        }
    }

    private async Task CreateTextDocumentAsync()
    {
        if (ViewModel is null || ViewModel.IsOperationRunning)
        {
            return;
        }

        var defaultName = ViewModel.GetNewTextDocumentDefaultName();
        var extension = Path.GetExtension(defaultName);
        var baseNameLength = Math.Max(0, defaultName.Length - extension.Length);
        var dialog = new TextInputDialog(
            "新建文本文档",
            "文件名",
            defaultName,
            0,
            baseNameLength)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var created = await ViewModel.CreateTextDocumentAsync(dialog.Value);
            if (created is not null)
            {
                await Dispatcher.InvokeAsync(
                    () => FindActiveFilePaneControl()?.FocusFileItemByPath(created),
                    System.Windows.Threading.DispatcherPriority.ContextIdle);
            }
        }
        catch (Exception exception)
        {
            ShowOperationError("新建文本文档失败", exception);
        }
    }

    private void ShowProperties(FilePaneViewModel pane)
    {
        if (pane.SelectedItems is not [var selected])
        {
            ViewModel?.SetStatusMessage("查看属性时请只选择一个文件或文件夹。");
            return;
        }

        try
        {
            _propertiesService.Show(
                selected.FullPath,
                new WindowInteropHelper(this).Handle);
        }
        catch (Exception exception)
        {
            ShowOperationError("打开属性失败", exception);
        }
    }

    private async Task RenameAsync()
    {
        if (ViewModel is null || ViewModel.IsOperationRunning)
        {
            return;
        }

        var selected = ViewModel.ActivePane?.SelectedItems;
        if (selected is not { Count: 1 })
        {
            MessageBox.Show(
                this,
                "重命名时请只选择一个项目。",
                "MYTC",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var selectedEntry = selected[0];
        var extension = selectedEntry.Kind == EntryKind.File
            ? Path.GetExtension(selectedEntry.Name)
            : string.Empty;
        var dialog = new TextInputDialog(
            "重命名",
            "新名称",
            selectedEntry.Name,
            0,
            selectedEntry.Name.Length - extension.Length)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true ||
            StringComparer.Ordinal.Equals(dialog.Value, selectedEntry.Name))
        {
            return;
        }

        try
        {
            await ViewModel.RenameSelectionAsync(dialog.Value);
        }
        catch (Exception exception)
        {
            ShowOperationError("重命名失败", exception);
        }
    }

    private async Task DeleteAsync(bool permanent)
    {
        if (ViewModel is null || ViewModel.IsOperationRunning)
        {
            return;
        }

        var focusTarget = FindActiveFileGrid();
        var selectedPaths = ViewModel.ActivePane?.SelectedItems
            .Select(item => item.FullPath)
            .ToArray() ?? [];
        var count = selectedPaths.Length;
        if (count == 0)
        {
            MessageBox.Show(
                this,
                "请先选择要删除的项目。",
                "MYTC",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            RestoreFileGridFocus(focusTarget);
            return;
        }

        var requiresConfirmation =
            permanent || _uiPreferences.ConfirmRecycleDelete;
        var confirmed = !requiresConfirmation ||
            (_deleteConfirmationOverride?.Invoke(
                    permanent,
                    count) ??
                ShowDeleteConfirmation(permanent, count));
        if (!confirmed)
        {
            _shortcutManager.ResetSequence();
            RestoreFileGridFocus(focusTarget);
            return;
        }

        try
        {
            var deletedAtUtc = DateTime.UtcNow;
            FileOperationResult? result;
            if (permanent)
            {
                result = await ViewModel.DeletePathsAsync(
                    selectedPaths,
                    permanent: true);
            }
            else
            {
                var managedPaths = selectedPaths
                    .Where(_managedRecycleService.RequiresManagedRecycle)
                    .ToArray();
                var shellPaths = selectedPaths
                    .Where(path => !managedPaths.Contains(
                        path,
                        StringComparer.OrdinalIgnoreCase))
                    .ToArray();

                var managedResult = managedPaths.Length == 0
                    ? new ManagedRecycleDeleteResult([], [])
                    : await _managedRecycleService.RecycleAsync(managedPaths);
                var shellResult = shellPaths.Length == 0
                    ? null
                    : await ViewModel.DeletePathsAsync(
                        shellPaths,
                        permanent: false);
                if (managedPaths.Length > 0 &&
                    shellPaths.Length == 0)
                {
                    await ViewModel.ActivePane!.RefreshCurrentAsync();
                }

                var shellFailures = shellResult?.Failures ?? [];
                var failures = managedResult.Failures
                    .Concat(shellFailures)
                    .ToArray();
                result = new FileOperationResult(
                    managedResult.Entries.Count +
                        (shellResult?.CompletedCount ?? 0),
                    shellResult?.SkippedCount ?? 0,
                    failures,
                    shellResult?.WasCancelled ?? false);

                var shellFailedPaths = shellFailures
                    .Select(failure => failure.Path)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var shellDeletedPaths = shellPaths
                    .Where(path => !shellFailedPaths.Contains(path))
                    .Take(shellResult?.CompletedCount ?? 0)
                    .ToArray();
                if (shellDeletedPaths.Length > 0 ||
                    managedResult.Entries.Count > 0)
                {
                    _recycleUndoStack.Push(new RecycleDeletionBatch(
                        shellDeletedPaths,
                        deletedAtUtc,
                        managedResult.Entries));
                }
            }

            ShowFailuresIfNeeded(result);
        }
        catch (Exception exception)
        {
            ShowOperationError("删除失败", exception);
        }
        finally
        {
            _shortcutManager.ResetSequence();
            RestoreFileGridFocus(focusTarget);
        }
    }

    private async Task UndoDeleteAsync()
    {
        if (ViewModel is null || ViewModel.IsOperationRunning)
        {
            return;
        }

        if (!_recycleUndoStack.TryPeek(out var deletion))
        {
            ViewModel.SetStatusMessage("当前会话没有可撤销的删除操作。");
            return;
        }

        try
        {
            var shellResult = deletion.OriginalPaths.Count == 0
                ? new RecycleBinRestoreResult([], [])
                : await _recycleBinRestoreService.RestoreAsync(
                    deletion with { ManagedEntries = null });
            var managedEntries = deletion.ManagedEntries ?? [];
            var managedResult = managedEntries.Count == 0
                ? new RecycleBinRestoreResult([], [])
                : await _managedRecycleService.RestoreAsync(managedEntries);
            var result = new RecycleBinRestoreResult(
                shellResult.RestoredPaths
                    .Concat(managedResult.RestoredPaths)
                    .ToArray(),
                shellResult.Failures
                    .Concat(managedResult.Failures)
                    .ToArray());
            if (result.RestoredPaths.Count > 0)
            {
                _recycleUndoStack.Pop();
                var failedPathSet = result.Failures
                    .Select(failure => failure.Path)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var remaining = deletion.OriginalPaths
                    .Where(path => failedPathSet.Contains(path))
                    .ToArray();
                var remainingManaged = managedEntries
                    .Where(entry => failedPathSet.Contains(
                        entry.OriginalPath))
                    .ToArray();
                if (remaining.Length > 0 ||
                    remainingManaged.Length > 0)
                {
                    _recycleUndoStack.Push(deletion with
                    {
                        OriginalPaths = remaining,
                        ManagedEntries = remainingManaged,
                    });
                }

                await Task.WhenAll(ViewModel.Panes.Select(
                    pane => pane.RefreshCurrentAsync()));
            }

            ViewModel.SetStatusMessage(
                $"撤销删除：已还原 {result.RestoredPaths.Count} 项，失败 {result.Failures.Count} 项。");
            if (result.Failures.Count > 0)
            {
                var details = string.Join(
                    Environment.NewLine,
                    result.Failures.Take(10).Select(failure =>
                        $"{failure.Path}：{failure.Message}"));
                MessageBox.Show(
                    this,
                    details,
                    "撤销删除未全部完成",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception exception)
        {
            ShowOperationError("撤销删除失败", exception);
        }
    }

    private bool ShowDeleteConfirmation(bool permanent, int count)
    {
        var message = permanent
            ? $"将永久删除选中的 {count} 个项目，且不能从回收站恢复。确定继续吗？"
            : $"将选中的 {count} 个项目移到回收站。确定继续吗？";
        return MessageBox.Show(
                this,
                message,
                permanent ? "确认永久删除" : "确认删除",
                MessageBoxButton.YesNo,
                permanent ? MessageBoxImage.Warning : MessageBoxImage.Question,
                MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    private void CopySelectionToClipboard(bool move)
    {
        if (ViewModel?.ActivePane?.SelectedItems is not { Count: > 0 } selected)
        {
            return;
        }

        var paths = new StringCollection();
        paths.AddRange(selected.Select(entry => entry.FullPath).ToArray());
        var data = new DataObject();
        data.SetFileDropList(paths);
        data.SetData(
            "Preferred DropEffect",
            new MemoryStream(BitConverter.GetBytes(move ? 2 : 1)));
        Clipboard.SetDataObject(data, copy: true);
    }

    private void CopyFullPathToClipboard(FilePaneViewModel pane)
    {
        var text = PathClipboardTextBuilder.Build(
            pane.SelectedItems,
            pane.CurrentPath);
        Clipboard.SetText(text);
        var count = pane.SelectedItems.Count;
        ViewModel?.SetStatusMessage(count switch
        {
            0 => $"已复制当前目录完整路径：{text}",
            1 => $"已复制完整路径：{text}",
            _ => $"已复制 {count} 个项目的完整路径。",
        });
    }

    private async Task PasteFromClipboardAsync()
    {
        if (ViewModel?.ActivePane is not { } destinationPane ||
            !Clipboard.ContainsFileDropList())
        {
            return;
        }

        var paths = Clipboard.GetFileDropList().Cast<string>().ToArray();
        var move = GetClipboardDropEffect() == 2;
        if (FileDropGuards.IsSameDirectoryDrop(
                destinationPane.CurrentPath,
                paths))
        {
            if (move)
            {
                ViewModel.SetStatusMessage(
                    "剪切来源与当前目录相同，无需移动。");
                return;
            }

            try
            {
                var result = await ViewModel.ExecutePathsToPaneAsync(
                    destinationPane,
                    paths,
                    FileOperationKind.Copy,
                    CollisionBehavior.KeepBoth,
                    destinationPane);
                ShowFailuresIfNeeded(result);
                ViewModel.SetStatusMessage(
                    $"已在当前目录创建 {result?.CompletedCount ?? 0} 个副本。");
            }
            catch (Exception exception)
            {
                ShowOperationError("创建副本失败", exception);
            }

            return;
        }

        await HandleFileDropAsync(destinationPane, paths, move);
        if (move)
        {
            Clipboard.Clear();
        }
    }

    private CollisionBehavior? ChooseCollisionBehavior(
        FilePaneViewModel destinationPane,
        IReadOnlyList<string> sourcePaths)
    {
        if (ViewModel is null ||
            !ViewModel.HasTransferCollisions(destinationPane, sourcePaths))
        {
            return CollisionBehavior.Skip;
        }

        var dialog = new CollisionDialog { Owner = this };
        return dialog.ShowDialog() == true ? dialog.SelectedBehavior : null;
    }

    private static int GetClipboardDropEffect()
    {
        var data = Clipboard.GetData("Preferred DropEffect");
        return data switch
        {
            MemoryStream stream when stream.Length >= sizeof(int) =>
                ReadDropEffect(stream),
            byte[] bytes when bytes.Length >= sizeof(int) =>
                BitConverter.ToInt32(bytes, 0),
            _ => 1,
        };
    }

    private static int ReadDropEffect(MemoryStream stream)
    {
        var position = stream.Position;
        stream.Position = 0;
        var bytes = new byte[sizeof(int)];
        _ = stream.Read(bytes, 0, bytes.Length);
        stream.Position = position;
        return BitConverter.ToInt32(bytes, 0);
    }

    private void ShowFailuresIfNeeded(FileOperationResult? result)
    {
        if (result is not { Failures.Count: > 0 })
        {
            return;
        }

        MessageBox.Show(
            this,
            $"操作完成，但有 {result.Failures.Count} 个项目失败。可点击窗口右下角“失败”查看。",
            "文件操作结果",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private async void OnSaveWorkspaceClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var dialog = new WorkspaceNameDialog(ViewModel.SelectedWorkspaceName)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await ViewModel.SaveNamedWorkspaceAsync(dialog.WorkspaceName);
        }
        catch (Exception exception)
        {
            ShowOperationError("保存工作区失败", exception);
        }
    }

    private async void OnLoadWorkspaceClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        try
        {
            _ = await ViewModel.LoadSelectedWorkspaceAsync();
        }
        catch (Exception exception)
        {
            ShowOperationError("载入工作区失败", exception);
        }
    }

    private async void OnManageWorkspacesClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        IReadOnlyDictionary<string, string?> iconAssignments;
        try
        {
            iconAssignments =
                await ViewModel.GetWorkspaceIconAssignmentsAsync();
        }
        catch (Exception exception)
        {
            ShowOperationError("读取工作区图标设置失败", exception);
            return;
        }

        var dialog = new WorkspaceManagerDialog(
            ViewModel.WorkspaceNames,
            ViewModel.SelectedWorkspaceName,
            iconAssignments)
        {
            Owner = this,
        };
        dialog.ImportRequested += OnWorkspaceImportRequested;
        dialog.ExportRequested += OnWorkspaceExportRequested;
        dialog.RenameRequested += OnWorkspaceRenameRequested;
        dialog.DeleteRequested += OnWorkspaceDeleteRequested;
        dialog.IconChangedRequested += OnWorkspaceIconChangedRequested;
        _ = dialog.ShowDialog();
    }

    private void OnSplitterDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var totalWidth = LeftPaneColumn.ActualWidth + RightPaneColumn.ActualWidth;
        var totalHeight = TopPaneRow.ActualHeight + BottomPaneRow.ActualHeight;
        if (totalWidth > 0)
        {
            ViewModel.HorizontalRatio = LeftPaneColumn.ActualWidth / totalWidth;
        }

        if (totalHeight > 0)
        {
            ViewModel.VerticalRatio = TopPaneRow.ActualHeight / totalHeight;
        }
    }

    private async void OnWorkspaceImportRequested(object? sender, EventArgs e)
    {
        if (sender is not WorkspaceManagerDialog dialog || ViewModel is null)
        {
            return;
        }

        var picker = new OpenFileDialog
        {
            Title = "导入工作区方案",
            Filter = "MYTC 工作区 (*.json)|*.json|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (picker.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var importedName = await ViewModel.ImportWorkspaceAsync(
                picker.FileName);
            await RefreshWorkspaceManagerAsync(dialog);
            MessageBox.Show(
                this,
                $"已导入工作区“{importedName}”。",
                "导入工作区",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            ShowOperationError("导入工作区失败", exception);
        }
    }

    private async void OnWorkspaceExportRequested(
        object? sender,
        WorkspaceSelectionEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var picker = new SaveFileDialog
        {
            Title = "导出工作区方案",
            Filter = "MYTC 工作区 (*.json)|*.json",
            DefaultExt = ".json",
            AddExtension = true,
            FileName = $"{e.WorkspaceName}.json",
            OverwritePrompt = true,
        };
        if (picker.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await ViewModel.ExportWorkspaceAsync(
                e.WorkspaceName,
                picker.FileName);
            MessageBox.Show(
                this,
                $"已导出工作区“{e.WorkspaceName}”。",
                "导出工作区",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            ShowOperationError("导出工作区失败", exception);
        }
    }

    private async void OnWorkspaceDeleteRequested(
        object? sender,
        WorkspaceSelectionEventArgs e)
    {
        if (sender is not WorkspaceManagerDialog dialog || ViewModel is null)
        {
            return;
        }

        if (MessageBox.Show(
                this,
                $"确定删除工作区“{e.WorkspaceName}”吗？\n\n此操作不会删除其中的实际文件。",
                "删除工作区",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await ViewModel.DeleteWorkspaceAsync(e.WorkspaceName);
            await RefreshWorkspaceManagerAsync(dialog);
        }
        catch (Exception exception)
        {
            ShowOperationError("删除工作区失败", exception);
        }
    }

    private async void OnWorkspaceRenameRequested(
        object? sender,
        WorkspaceSelectionEventArgs e)
    {
        if (sender is not WorkspaceManagerDialog dialog || ViewModel is null)
        {
            return;
        }

        var nameDialog = new WorkspaceNameDialog(
            e.WorkspaceName,
            "重命名工作区方案",
            "重命名")
        {
            Owner = this,
        };
        if (nameDialog.ShowDialog() != true ||
            StringComparer.Ordinal.Equals(
                e.WorkspaceName.Trim(),
                nameDialog.WorkspaceName))
        {
            return;
        }

        try
        {
            await ViewModel.RenameWorkspaceAsync(
                e.WorkspaceName,
                nameDialog.WorkspaceName);
            await RefreshWorkspaceManagerAsync(dialog);
        }
        catch (Exception exception)
        {
            ShowOperationError("重命名工作区失败", exception);
        }
    }

    private async void OnWorkspaceIconChangedRequested(
        object? sender,
        WorkspaceIconSelectionEventArgs e)
    {
        if (sender is not WorkspaceManagerDialog dialog || ViewModel is null)
        {
            return;
        }

        try
        {
            await ViewModel.SetWorkspaceIconKeyAsync(
                e.WorkspaceName,
                e.IconKey);
            await RefreshWorkspaceManagerAsync(dialog);
        }
        catch (Exception exception)
        {
            ShowOperationError("保存工作区图标失败", exception);
        }
    }

    private static async Task RefreshWorkspaceManagerAsync(
        WorkspaceManagerDialog dialog)
    {
        if (dialog.Owner is MainWindow window && window.ViewModel is not null)
        {
            var iconAssignments = await window.ViewModel
                .GetWorkspaceIconAssignmentsAsync();
            dialog.ReplaceWorkspaceNames(
                window.ViewModel.WorkspaceNames,
                window.ViewModel.SelectedWorkspaceName,
                iconAssignments);
        }
    }

    private async void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || ViewModel is null)
        {
            return;
        }

        e.Cancel = true;
        try
        {
            await ViewModel.SaveSessionAsync();
        }
        catch (Exception exception)
        {
            ShowOperationError("保存本次会话失败", exception);
        }
        finally
        {
            ViewModel.Dispose();
            _allowClose = true;
            Close();
        }
    }

    private void OnDataContextChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedViewModel.WorkspaceActivated -= OnWorkspaceActivated;
        }

        _subscribedViewModel = e.NewValue as MainWindowViewModel;
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
            _subscribedViewModel.WorkspaceActivated += OnWorkspaceActivated;
            Dispatcher.BeginInvoke(ApplyLayoutRatios);
        }
    }

    private async void OnWorkspaceActivated(string? workspaceName)
    {
        ApplyWorkspaceAppearance(
            workspaceName,
            ViewModel?.ActiveWorkspaceIconKey);
        var previous = _uiPreferences;
        try
        {
            _uiPreferences = _uiPreferences with
            {
                LastWorkspaceName = workspaceName,
            };
            await SaveUiPreferencesAsync(_uiPreferences);
        }
        catch (Exception exception)
        {
            _uiPreferences = previous;
            ShowOperationError("保存默认工作区失败", exception);
        }
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.HorizontalRatio) or
            nameof(MainWindowViewModel.VerticalRatio))
        {
            Dispatcher.BeginInvoke(ApplyLayoutRatios);
        }
    }

    private void ApplyLayoutRatios()
    {
        if (ViewModel is null)
        {
            return;
        }

        LeftPaneColumn.Width = new GridLength(ViewModel.HorizontalRatio, GridUnitType.Star);
        RightPaneColumn.Width = new GridLength(
            1 - ViewModel.HorizontalRatio,
            GridUnitType.Star);
        TopPaneRow.Height = new GridLength(ViewModel.VerticalRatio, GridUnitType.Star);
        BottomPaneRow.Height = new GridLength(
            1 - ViewModel.VerticalRatio,
            GridUnitType.Star);
    }

    private void ApplyOperationToolbarPreference(bool isVisible)
    {
        OperationToolbarMenuItem.IsChecked = isVisible;
        OperationToolbarBorder.Visibility = isVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async Task SetWorkspaceToolbarVisibleAsync(bool isVisible)
    {
        var previous = _uiPreferences;
        ApplyWorkspaceToolbarPreference(isVisible);
        try
        {
            _uiPreferences = _uiPreferences with
            {
                IsWorkspaceToolbarVisible = isVisible,
            };
            await SaveUiPreferencesAsync(_uiPreferences);
            ViewModel?.SetStatusMessage(
                isVisible ? "工作区工具栏已显示。" : "工作区工具栏已隐藏。");
        }
        catch (Exception exception)
        {
            _uiPreferences = previous;
            ApplyWorkspaceToolbarPreference(previous.IsWorkspaceToolbarVisible);
            ShowOperationError("保存界面设置失败", exception);
        }
    }

    private async Task SetSettingsToolbarVisibleAsync(bool isVisible)
    {
        var previous = _uiPreferences;
        ApplySettingsToolbarPreference(isVisible);
        try
        {
            _uiPreferences = _uiPreferences with
            {
                IsSettingsToolbarVisible = isVisible,
            };
            await SaveUiPreferencesAsync(_uiPreferences);
            ViewModel?.SetStatusMessage(
                isVisible ? "设置工具栏已显示。" : "设置工具栏已隐藏。");
        }
        catch (Exception exception)
        {
            _uiPreferences = previous;
            ApplySettingsToolbarPreference(previous.IsSettingsToolbarVisible);
            ShowOperationError("保存界面设置失败", exception);
        }
    }

    private void ApplyWorkspaceToolbarPreference(bool isVisible)
    {
        WorkspaceToolbarMenuItem.IsChecked = isVisible;
        WorkspaceToolbar.Visibility = isVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateHeaderToolbarVisibility();
    }

    private void ApplySettingsToolbarPreference(bool isVisible)
    {
        SettingsToolbarMenuItem.IsChecked = isVisible;
        SettingsToolbar.Visibility = isVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateHeaderToolbarVisibility();
    }

    private void UpdateHeaderToolbarVisibility()
    {
        HeaderToolbarBorder.Visibility =
            WorkspaceToolbar.Visibility == Visibility.Visible ||
            SettingsToolbar.Visibility == Visibility.Visible
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private async Task SaveUiPreferencesAsync(UiPreferences preferences)
    {
        await _uiPreferenceSaveGate.WaitAsync();
        try
        {
            await _uiPreferencesStore.SaveAsync(preferences);
        }
        finally
        {
            _uiPreferenceSaveGate.Release();
        }
    }

    private void SynchronizeFocusedFilePaneSelection()
    {
        if (TryGetFocusedFileGrid(out var pane, out var grid))
        {
            pane.RequestActivation();
            _fileListKeyboardPane = pane;
            pane.SetSelectedItems(
                grid.SelectedItems.Cast<FileSystemEntry>());
        }
    }

    private async Task<bool> TryHandleFileListKeyboardCommandAsync(
        KeyEventArgs e,
        Key eventKey,
        ModifierKeys eventModifiers)
    {
        var isPlainNavigation = eventModifiers == ModifierKeys.None;
        var isExtendSelection =
            eventModifiers == ModifierKeys.Shift &&
            eventKey is Key.Up or Key.Down;
        if (_fileListKeyboardPane is null ||
            (!isPlainNavigation && !isExtendSelection) ||
            IsFileListKeyboardInputFocused())
        {
            return false;
        }

        var pane = ViewModel?.ActivePane ?? _fileListKeyboardPane;
        _fileListKeyboardPane = pane;

        var control = FindVisualChildren<FilePaneControl>(this)
            .FirstOrDefault(candidate =>
                candidate.IsVisible &&
                ReferenceEquals(candidate.DataContext, pane));
        if (control is null)
        {
            _fileListKeyboardPane = null;
            return false;
        }

        switch (eventKey)
        {
            case Key.Up:
            case Key.Down:
                e.Handled = true;
                pane.RequestActivation();
                if (isExtendSelection)
                {
                    control.ExtendFileSelectionFromKeyboard(
                        eventKey == Key.Down ? 1 : -1);
                }
                else
                {
                    if (!control.TryCycleQuickLocate(
                            eventKey == Key.Down ? 1 : -1))
                    {
                        control.MoveFileSelectionFromKeyboard(
                            eventKey == Key.Down ? 1 : -1);
                    }
                }

                return true;
            case Key.Enter when !e.IsRepeat &&
                pane.SelectedItem is { } entry:
            {
                e.Handled = true;
                pane.RequestActivation();
                var previousPath = pane.CurrentPath;
                await pane.OpenEntryAsync(entry);
                if (!StringComparer.OrdinalIgnoreCase.Equals(
                        previousPath,
                        pane.CurrentPath))
                {
                    await Dispatcher.InvokeAsync(
                        () => RestoreFileListKeyboardNavigation(pane),
                        System.Windows.Threading.DispatcherPriority.ContextIdle);
                }

                return true;
            }
            case Key.Back when !e.IsRepeat &&
                _shortcutManager.IsExactBinding(
                    e,
                    ShortcutAction.NavigateUp):
            {
                e.Handled = true;
                pane.RequestActivation();
                var previousPath = pane.CurrentPath;
                await pane.NavigateUpAsync();
                var restorePath = pane.ConsumeParentNavigationChildPath();
                if (!StringComparer.OrdinalIgnoreCase.Equals(
                        previousPath,
                        pane.CurrentPath))
                {
                    await Dispatcher.InvokeAsync(
                        () => RestoreFileListKeyboardNavigation(
                            pane,
                            restorePath),
                        System.Windows.Threading.DispatcherPriority.ContextIdle);
                }

                return true;
            }
            default:
                return false;
        }
    }

    private static bool IsFileListKeyboardInputFocused()
    {
        for (var current = Keyboard.FocusedElement as DependencyObject;
             current is not null;
             current = GetParent(current))
        {
            if (current is TextBox or ComboBox or MenuItem or System.Windows.Controls.ContextMenu)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasCommandModifier()
    {
        return Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ||
            Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ||
            Keyboard.Modifiers.HasFlag(ModifierKeys.Windows);
    }

    private static bool TryGetFocusedFileGrid(
        out FilePaneViewModel pane,
        out DataGrid grid)
    {
        for (var current = Keyboard.FocusedElement as DependencyObject;
             current is not null;
             current = GetParent(current))
        {
            if (current is DataGrid
                {
                    DataContext: FilePaneViewModel foundPane,
                } foundGrid)
            {
                pane = foundPane;
                grid = foundGrid;
                return true;
            }
        }

        pane = null!;
        grid = null!;
        return false;
    }

    private void FocusActivePaneAddressBar()
    {
        if (ViewModel?.ActivePane is not { } activePane)
        {
            return;
        }

        var control = FindVisualChildren<FilePaneControl>(this)
            .FirstOrDefault(candidate =>
                candidate.IsVisible &&
                ReferenceEquals(candidate.DataContext, activePane));
        if (control is null)
        {
            return;
        }

        control.FocusAddressBar();
        ViewModel.SetStatusMessage("地址栏已激活，路径文字已全选。");
    }

    private void FocusActivePaneFirstItem()
    {
        if (ViewModel?.ActivePane is not { } activePane)
        {
            return;
        }

        var control = FindVisualChildren<FilePaneControl>(this)
            .FirstOrDefault(candidate =>
                candidate.IsVisible &&
                ReferenceEquals(candidate.DataContext, activePane));
        control?.FocusFirstFileItem();
    }

    private void RestoreFileListKeyboardNavigation(
        FilePaneViewModel pane,
        string? preferredPath = null)
    {
        _fileListKeyboardPane = pane;
        if (ViewModel?.ActivePane is not null &&
            !ReferenceEquals(ViewModel.ActivePane, pane))
        {
            pane.RequestActivation();
        }

        var control = FindVisualChildren<FilePaneControl>(this)
            .FirstOrDefault(candidate =>
                candidate.IsVisible &&
                ReferenceEquals(candidate.DataContext, pane));
        if (control is null ||
            (!string.IsNullOrWhiteSpace(preferredPath) &&
             control.FocusFileItemByPath(preferredPath)))
        {
            return;
        }

        control.FocusFirstFileItem();
    }

    private DataGrid? FindActiveFileGrid()
    {
        if (ViewModel?.ActivePane is not { } activePane)
        {
            return null;
        }

        return FindVisualChildren<DataGrid>(this)
            .FirstOrDefault(candidate =>
                candidate.IsVisible &&
                ReferenceEquals(candidate.DataContext, activePane));
    }

    private FilePaneControl? FindActiveFilePaneControl()
    {
        return ViewModel?.ActivePane is { } activePane
            ? FindVisualChildren<FilePaneControl>(this).FirstOrDefault(
                control => ReferenceEquals(control.DataContext, activePane))
            : null;
    }

    private void ClearQuickLocatePrefix()
    {
        _quickLocateTimer.Stop();
        _quickLocatePrefix = string.Empty;
    }

    private void RestoreFileGridFocus(DataGrid? grid)
    {
        if (grid is null)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            () =>
            {
                grid.Focus();
                Keyboard.Focus(grid);
            });
    }

    private static Key GetEventKey(KeyEventArgs e)
    {
        return e.Key == Key.System ? e.SystemKey : e.Key;
    }

    private static ModifierKeys GetEventModifiers(KeyEventArgs e)
    {
        return Keyboard.Modifiers |
            (e.Key == Key.System ? ModifierKeys.Alt : ModifierKeys.None);
    }

    private static void TryDeleteStagedUpdate(
        string packageRoot,
        string stagingRoot)
    {
        try
        {
            var fullStagingRoot = Path.GetFullPath(stagingRoot)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            var allowedPrefix =
                fullStagingRoot + Path.DirectorySeparatorChar;
            var candidate = new DirectoryInfo(
                Path.GetFullPath(packageRoot));
            if (!candidate.FullName.StartsWith(
                    allowedPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            while (candidate.Parent is not null &&
                   !StringComparer.OrdinalIgnoreCase.Equals(
                       candidate.Parent.FullName.TrimEnd(
                           Path.DirectorySeparatorChar,
                           Path.AltDirectorySeparatorChar),
                       fullStagingRoot))
            {
                candidate = candidate.Parent;
            }

            if (candidate.Parent is not null &&
                StringComparer.OrdinalIgnoreCase.Equals(
                    candidate.Parent.FullName.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar),
                    fullStagingRoot) &&
                Directory.Exists(candidate.FullName))
            {
                Directory.Delete(candidate.FullName, recursive: true);
            }
        }
        catch
        {
            // A stale staging directory can be removed on the next update.
        }
    }

    private static async Task WaitForDeleteKeyReleaseAsync()
    {
        const int virtualKeyDelete = 0x2E;
        while ((GetAsyncKeyState(virtualKeyDelete) & 0x8000) != 0)
        {
            await Task.Delay(20);
        }
    }

    private void BringWindowToForeground()
    {
        const int showRestore = 9;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            Activate();
            return;
        }

        _ = ShowWindow(handle, showRestore);
        var foregroundWindow = GetForegroundWindow();
        var currentThread = GetCurrentThreadId();
        var foregroundThread = foregroundWindow == IntPtr.Zero
            ? 0
            : GetWindowThreadProcessId(
                foregroundWindow,
                out _);
        var attached = foregroundThread != 0 &&
            foregroundThread != currentThread &&
            AttachThreadInput(
                currentThread,
                foregroundThread,
                attach: true);
        try
        {
            _ = BringWindowToTop(handle);
            _ = SetForegroundWindow(handle);
            Activate();
        }
        finally
        {
            if (attached)
            {
                _ = AttachThreadInput(
                    currentThread,
                    foregroundThread,
                    attach: false);
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr window,
        out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(
        uint threadId,
        uint threadToAttach,
        [MarshalAs(UnmanagedType.Bool)] bool attach);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(
        IntPtr window,
        int command);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private static IEnumerable<T> FindVisualChildren<T>(
        DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0;
             index < VisualTreeHelper.GetChildrenCount(root);
             index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static DependencyObject? GetParent(DependencyObject child)
    {
        if (child is Visual)
        {
            return VisualTreeHelper.GetParent(child);
        }

        return LogicalTreeHelper.GetParent(child);
    }

    private void ShowOperationError(string title, Exception exception)
    {
        MessageBox.Show(
            this,
            exception.Message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private enum RightDragChoice
    {
        Copy,
        Move,
        CreateShortcut,
    }
}

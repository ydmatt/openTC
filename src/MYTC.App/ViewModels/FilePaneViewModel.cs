using System.Collections.ObjectModel;
using System.IO;
using MYTC.App.Mvvm;
using MYTC.Application.Abstractions;
using MYTC.Application.Files;
using MYTC.Application.Navigation;
using MYTC.Domain.Drives;
using MYTC.Domain.Files;
using MYTC.Domain.Workspaces;

namespace MYTC.App.ViewModels;

public sealed class FilePaneViewModel : ObservableObject, IDisposable
{
    private readonly IDirectoryListingService _listingService;
    private readonly IDriveService _driveService;
    private readonly IFileLauncher _fileLauncher;
    private readonly Action<FilePaneViewModel> _activate;
    private readonly Action<FilePaneViewModel> _toggleMaximize;
    private readonly Stack<TabSnapshot> _closedTabs = new();
    private CancellationTokenSource? _navigationCancellation;
    private string _currentPath = string.Empty;
    private string _addressText = string.Empty;
    private string? _errorMessage;
    private string _statusText = "准备就绪";
    private bool _isBusy;
    private PaneVisualRole _role;
    private FileSystemEntry? _selectedItem;
    private IReadOnlyList<FileSystemEntry> _selectedItems = [];
    private DriveEntry? _selectedDrive;
    private bool _suppressDriveNavigation;
    private SortDescriptor _sort = SortDescriptor.Default;
    private FileTabViewModel _activeTab;

    public FilePaneViewModel(
        PaneSnapshot snapshot,
        IDirectoryListingService listingService,
        IDriveService driveService,
        IFileLauncher fileLauncher,
        Action<FilePaneViewModel> activate,
        Action<FilePaneViewModel> toggleMaximize)
    {
        Id = snapshot.Id;
        _listingService = listingService;
        _driveService = driveService;
        _fileLauncher = fileLauncher;
        _activate = activate;
        _toggleMaximize = toggleMaximize;

        foreach (var tabSnapshot in snapshot.Tabs)
        {
            Tabs.Add(new FileTabViewModel(tabSnapshot));
        }

        if (Tabs.Count == 0)
        {
            Tabs.Add(CreateTab(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        }

        _activeTab = Tabs.FirstOrDefault(tab =>
            StringComparer.Ordinal.Equals(tab.Id, snapshot.ActiveTabId)) ?? Tabs[0];
        SetActiveTabFlags();
        _sort = _activeTab.Sort;

        BackCommand = new AsyncRelayCommand(
            NavigateBackAsync,
            () => ActiveTab.BackHistory.Count > 0 && !IsBusy);
        ForwardCommand = new AsyncRelayCommand(
            NavigateForwardAsync,
            () => ActiveTab.ForwardHistory.Count > 0 && !IsBusy);
        UpCommand = new AsyncRelayCommand(
            NavigateUpAsync,
            () => !string.IsNullOrWhiteSpace(CurrentPath) && !IsBusy);
        RefreshCommand = new AsyncRelayCommand(
            RefreshAsync,
            () => !string.IsNullOrWhiteSpace(CurrentPath) && !IsBusy);
        NewTabCommand = new AsyncRelayCommand(NewTabAsync, () => !IsBusy);
        RestoreClosedTabCommand = new AsyncRelayCommand(
            RestoreClosedTabAsync,
            () => _closedTabs.Count > 0 && !IsBusy);
        ToggleMaximizeCommand = new RelayCommand(() => _toggleMaximize(this));
    }

    public string Id { get; }

    public ObservableCollection<FileSystemEntry> Items { get; } = [];

    public ObservableCollection<DriveEntry> Drives { get; } = [];

    public ObservableCollection<FileTabViewModel> Tabs { get; } = [];

    public AsyncRelayCommand BackCommand { get; }

    public AsyncRelayCommand ForwardCommand { get; }

    public AsyncRelayCommand UpCommand { get; }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand NewTabCommand { get; }

    public AsyncRelayCommand RestoreClosedTabCommand { get; }

    public RelayCommand ToggleMaximizeCommand { get; }

    public FileTabViewModel ActiveTab
    {
        get => _activeTab;
        private set
        {
            if (SetProperty(ref _activeTab, value))
            {
                SetActiveTabFlags();
                OnPropertyChanged(nameof(CanCloseTab));
            }
        }
    }

    public bool CanCloseTab => Tabs.Count > 1;

    public string CurrentPath
    {
        get => _currentPath;
        private set => SetProperty(ref _currentPath, value);
    }

    public string AddressText
    {
        get => _addressText;
        set => SetProperty(ref _addressText, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public PaneVisualRole Role
    {
        get => _role;
        set
        {
            if (SetProperty(ref _role, value))
            {
                OnPropertyChanged(nameof(RoleLabel));
            }
        }
    }

    public string RoleLabel => Role switch
    {
        PaneVisualRole.Active => "活动",
        PaneVisualRole.Target => "目标",
        _ => string.Empty,
    };

    public FileSystemEntry? SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }

    public IReadOnlyList<FileSystemEntry> SelectedItems
    {
        get => _selectedItems;
        private set => SetProperty(ref _selectedItems, value);
    }

    public DriveEntry? SelectedDrive
    {
        get => _selectedDrive;
        set
        {
            if (!SetProperty(ref _selectedDrive, value) ||
                _suppressDriveNavigation ||
                value is null)
            {
                return;
            }

            if (!value.IsReady)
            {
                ErrorMessage = $"驱动器当前不可用：{value.RootPath}";
                return;
            }

            _ = NavigateAsync(value.RootPath);
        }
    }

    public string SortSummary =>
        $"{GetSortColumnLabel(_sort.Column)} {(_sort.Direction == SortDirection.Ascending ? "↑" : "↓")}";

    public async Task InitializeAsync()
    {
        RefreshDrives();
        var initialPath = ActiveTab.IsFixed &&
            !string.IsNullOrWhiteSpace(ActiveTab.FixedPath)
            ? ActiveTab.FixedPath
            : ActiveTab.CurrentPath;
        await NavigateAsync(initialPath ?? ActiveTab.CurrentPath, recordHistory: false);
    }

    public void RequestActivation() => _activate(this);

    public Task NavigateFromAddressAsync() => NavigateAsync(AddressText);

    public Task RefreshCurrentAsync() => RefreshAsync();

    public void SetSelectedItems(IEnumerable<FileSystemEntry> entries)
    {
        SelectedItems = entries.ToArray();
        SelectedItem = SelectedItems.FirstOrDefault();
    }

    public void UpdateSelectedItems(IEnumerable<FileSystemEntry> entries)
    {
        SelectedItems = entries.ToArray();
    }

    public async Task OpenEntryAsync(FileSystemEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        RequestActivation();

        if (entry.Kind == EntryKind.Directory)
        {
            await NavigateAsync(entry.FullPath);
            return;
        }

        if (Path.GetExtension(entry.FullPath).Equals(
                ".lnk",
                StringComparison.OrdinalIgnoreCase))
        {
            var target = _fileLauncher.TryResolveShortcutTarget(
                entry.FullPath);
            if (!string.IsNullOrWhiteSpace(target) &&
                Directory.Exists(target))
            {
                await NavigateAsync(target);
                StatusText = $"已在当前窗格打开快捷方式：{entry.Name}";
                return;
            }
        }

        try
        {
            _fileLauncher.Open(entry.FullPath);
            StatusText = $"已打开：{entry.Name}";
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ErrorMessage = $"无法打开文件：{exception.Message}";
        }
    }

    public async Task SelectTabAsync(FileTabViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        RequestActivation();

        var wasActive = ReferenceEquals(ActiveTab, tab);
        ActiveTab = tab;
        _sort = tab.Sort;
        OnPropertyChanged(nameof(SortSummary));

        if (wasActive && !tab.IsFixed)
        {
            return;
        }

        var destination = tab.IsFixed &&
            !string.IsNullOrWhiteSpace(tab.FixedPath)
            ? tab.FixedPath
            : tab.CurrentPath;
        await NavigateAsync(destination ?? tab.CurrentPath, recordHistory: false);
    }

    public async Task CloseTabAsync(FileTabViewModel tab)
    {
        if (Tabs.Count <= 1 || !Tabs.Contains(tab))
        {
            return;
        }

        var index = Tabs.IndexOf(tab);
        _closedTabs.Push(tab.Capture());
        Tabs.Remove(tab);

        if (ReferenceEquals(tab, ActiveTab))
        {
            ActiveTab = Tabs[Math.Min(index, Tabs.Count - 1)];
            _sort = ActiveTab.Sort;
            await NavigateAsync(
                ActiveTab.IsFixed && !string.IsNullOrWhiteSpace(ActiveTab.FixedPath)
                    ? ActiveTab.FixedPath
                    : ActiveTab.CurrentPath,
                recordHistory: false);
        }

        OnPropertyChanged(nameof(CanCloseTab));
        RestoreClosedTabCommand.RaiseCanExecuteChanged();
    }

    public void MoveTab(FileTabViewModel tab, int offset)
    {
        var currentIndex = Tabs.IndexOf(tab);
        var targetIndex = currentIndex + offset;
        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= Tabs.Count)
        {
            return;
        }

        Tabs.Move(currentIndex, targetIndex);
    }

    public async Task<FileTabViewModel> CopyTabFromAsync(
        FileTabViewModel source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var snapshot = source.Capture() with
        {
            Id = Guid.NewGuid().ToString("N"),
        };
        var copied = new FileTabViewModel(snapshot);
        Tabs.Add(copied);
        ActiveTab = copied;
        _sort = copied.Sort;
        OnPropertyChanged(nameof(CanCloseTab));
        OnPropertyChanged(nameof(SortSummary));
        await NavigateAsync(copied.CurrentPath, recordHistory: false);
        return copied;
    }

    public async Task ApplyTabSettingsAsync(
        FileTabViewModel tab,
        string customTitle,
        bool isFixed,
        string? fixedPath)
    {
        tab.CustomTitle = customTitle.Trim();
        tab.Mode = isFixed ? TabMode.Fixed : TabMode.Normal;
        tab.FixedPath = isFixed
            ? string.IsNullOrWhiteSpace(fixedPath)
                ? tab.CurrentPath
                : DirectoryNavigator.Normalize(fixedPath)
            : null;

        if (ReferenceEquals(tab, ActiveTab) && tab.IsFixed)
        {
            await NavigateAsync(tab.FixedPath!, recordHistory: false);
        }
    }

    public Task PinTabToCurrentDirectoryAsync(FileTabViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        var path = ReferenceEquals(tab, ActiveTab)
            ? CurrentPath
            : tab.CurrentPath;
        return ApplyTabSettingsAsync(
            tab,
            GetDirectoryDisplayName(path),
            isFixed: true,
            path);
    }

    public void SortBy(FileSortColumn column)
    {
        _sort = _sort.Column == column
            ? _sort with
            {
                Direction = _sort.Direction == SortDirection.Ascending
                    ? SortDirection.Descending
                    : SortDirection.Ascending,
            }
            : new SortDescriptor(column, SortDirection.Ascending);

        ActiveTab.Sort = _sort;
        ApplySorted(Items.ToArray());
        OnPropertyChanged(nameof(SortSummary));
        StatusText = $"按{GetSortColumnLabel(column)}排序";
    }

    public PaneSnapshot Capture()
    {
        return new PaneSnapshot(
            Id,
            Tabs.Select(tab => tab.Capture()).ToArray(),
            ActiveTab.Id);
    }

    public async Task NavigateAsync(string path, bool recordHistory = true)
    {
        string normalizedPath;
        try
        {
            normalizedPath = DirectoryNavigator.Normalize(path);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            ErrorMessage = $"路径无效：{exception.Message}";
            AddressText = CurrentPath;
            return;
        }

        var previousPath = CurrentPath;
        var cancellation = new CancellationTokenSource();
        var previousCancellation = Interlocked.Exchange(ref _navigationCancellation, cancellation);
        previousCancellation?.Cancel();
        previousCancellation?.Dispose();

        IsBusy = true;
        ErrorMessage = null;
        StatusText = $"正在读取 {normalizedPath}";

        try
        {
            var entries = await _listingService.ListAsync(normalizedPath, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();

            if (recordHistory &&
                !string.IsNullOrWhiteSpace(previousPath) &&
                !StringComparer.OrdinalIgnoreCase.Equals(previousPath, normalizedPath))
            {
                ActiveTab.BackHistory.Add(previousPath);
                ActiveTab.ForwardHistory.Clear();
            }

            CurrentPath = normalizedPath;
            AddressText = normalizedPath;
            ActiveTab.CurrentPath = normalizedPath;
            ApplySorted(entries);
            UpdateSelectedDrive(normalizedPath);
            StatusText = $"{Items.Count} 个对象";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A newer navigation request superseded this one.
        }
        catch (Exception exception) when (
            exception is DirectoryNotFoundException or
            UnauthorizedAccessException or
            IOException)
        {
            ErrorMessage = $"无法访问目录：{exception.Message}";
            AddressText = CurrentPath;
            StatusText = "目录读取失败";
        }
        finally
        {
            if (ReferenceEquals(_navigationCancellation, cancellation))
            {
                IsBusy = false;
            }

            RaiseCommandStates();
        }
    }

    public void Dispose()
    {
        _navigationCancellation?.Cancel();
        _navigationCancellation?.Dispose();
    }

    public static PaneSnapshot CreateDefaultSnapshot(string id, string path)
    {
        var tabId = Guid.NewGuid().ToString("N");
        return new PaneSnapshot(
            id,
            [
                new TabSnapshot(
                    tabId,
                    string.Empty,
                    TabMode.Normal,
                    path,
                    null,
                    [],
                    [],
                    SortDescriptor.Default),
            ],
            tabId);
    }

    private async Task NewTabAsync()
    {
        var tab = CreateTab(
            string.IsNullOrWhiteSpace(CurrentPath)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : CurrentPath);
        Tabs.Add(tab);
        OnPropertyChanged(nameof(CanCloseTab));
        await SelectTabAsync(tab);
    }

    private async Task RestoreClosedTabAsync()
    {
        if (_closedTabs.Count == 0)
        {
            return;
        }

        var restored = new FileTabViewModel(_closedTabs.Pop());
        Tabs.Add(restored);
        OnPropertyChanged(nameof(CanCloseTab));
        RestoreClosedTabCommand.RaiseCanExecuteChanged();
        await SelectTabAsync(restored);
    }

    private async Task NavigateBackAsync()
    {
        if (ActiveTab.BackHistory.Count == 0)
        {
            return;
        }

        var destination = PopLast(ActiveTab.BackHistory);
        if (!string.IsNullOrWhiteSpace(CurrentPath))
        {
            ActiveTab.ForwardHistory.Add(CurrentPath);
        }

        await NavigateAsync(destination, recordHistory: false);
    }

    private async Task NavigateForwardAsync()
    {
        if (ActiveTab.ForwardHistory.Count == 0)
        {
            return;
        }

        var destination = PopLast(ActiveTab.ForwardHistory);
        if (!string.IsNullOrWhiteSpace(CurrentPath))
        {
            ActiveTab.BackHistory.Add(CurrentPath);
        }

        await NavigateAsync(destination, recordHistory: false);
    }

    public async Task NavigateUpAsync()
    {
        var parent = DirectoryNavigator.GetParent(CurrentPath);
        if (parent is not null)
        {
            await NavigateAsync(parent);
        }
    }

    private Task RefreshAsync() => NavigateAsync(CurrentPath, recordHistory: false);

    private void RefreshDrives()
    {
        _suppressDriveNavigation = true;
        try
        {
            Drives.Clear();
            foreach (var drive in _driveService.GetDrives())
            {
                Drives.Add(drive);
            }
        }
        finally
        {
            _suppressDriveNavigation = false;
        }
    }

    private void UpdateSelectedDrive(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        _suppressDriveNavigation = true;
        try
        {
            SelectedDrive = Drives.FirstOrDefault(
                drive => StringComparer.OrdinalIgnoreCase.Equals(drive.RootPath, root));
        }
        finally
        {
            _suppressDriveNavigation = false;
        }
    }

    private void ApplySorted(IEnumerable<FileSystemEntry> entries)
    {
        var sorted = entries.ToList();
        sorted.Sort(new FileEntryComparer(_sort));

        Items.Clear();
        foreach (var entry in sorted)
        {
            Items.Add(entry);
        }

        SetSelectedItems([]);
    }

    private void RaiseCommandStates()
    {
        BackCommand.RaiseCanExecuteChanged();
        ForwardCommand.RaiseCanExecuteChanged();
        UpCommand.RaiseCanExecuteChanged();
        RefreshCommand.RaiseCanExecuteChanged();
        NewTabCommand.RaiseCanExecuteChanged();
        RestoreClosedTabCommand.RaiseCanExecuteChanged();
    }

    private void SetActiveTabFlags()
    {
        foreach (var tab in Tabs)
        {
            tab.IsActive = ReferenceEquals(tab, ActiveTab);
        }
    }

    private static FileTabViewModel CreateTab(string path)
    {
        return new FileTabViewModel(
            new TabSnapshot(
                Guid.NewGuid().ToString("N"),
                string.Empty,
                TabMode.Normal,
                path,
                null,
                [],
                [],
                SortDescriptor.Default));
    }

    private static string PopLast(IList<string> values)
    {
        var index = values.Count - 1;
        var value = values[index];
        values.RemoveAt(index);
        return value;
    }

    private static string GetSortColumnLabel(FileSortColumn column)
    {
        return column switch
        {
            FileSortColumn.Name => "名称",
            FileSortColumn.ModifiedAt => "修改日期",
            FileSortColumn.Type => "类型",
            FileSortColumn.Size => "大小",
            _ => throw new ArgumentOutOfRangeException(nameof(column)),
        };
    }

    private static string GetDirectoryDisplayName(string path)
    {
        var normalized = DirectoryNavigator.Normalize(path);
        var name = Path.GetFileName(
            Path.TrimEndingDirectorySeparator(normalized));
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return (Path.GetPathRoot(normalized) ?? normalized)
            .TrimEnd(Path.DirectorySeparatorChar);
    }
}

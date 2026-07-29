using System.Collections.ObjectModel;
using System.IO;
using MYTC.App.Mvvm;
using MYTC.Application.Abstractions;
using MYTC.Application.Panes;
using MYTC.Domain.Drives;
using MYTC.Domain.Operations;
using MYTC.Domain.Workspaces;

namespace MYTC.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private static readonly string[] PaneIds =
        ["top-left", "top-right", "bottom-left", "bottom-right"];

    private readonly IDirectoryListingService _listingService;
    private readonly IDriveService _driveService;
    private readonly IFileLauncher _fileLauncher;
    private readonly IFileOperationService _fileOperationService;
    private readonly IWorkspaceStore _workspaceStore;
    private PaneFocusState _focusState = new("top-left", "top-right");
    private string _statusMessage = "正在恢复上次会话…";
    private FilePaneViewModel? _maximizedPane;
    private string? _selectedWorkspaceName;
    private double _horizontalRatio = 0.5;
    private double _verticalRatio = 0.5;
    private bool _isInitialized;
    private bool _isOperationRunning;
    private string _operationProgressText = string.Empty;
    private CancellationTokenSource? _operationCancellation;

    public MainWindowViewModel(
        IDirectoryListingService listingService,
        IDriveService driveService,
        IFileLauncher fileLauncher,
        IFileOperationService fileOperationService,
        IWorkspaceStore workspaceStore)
    {
        _listingService = listingService;
        _driveService = driveService;
        _fileLauncher = fileLauncher;
        _fileOperationService = fileOperationService;
        _workspaceStore = workspaceStore;

        ActivatePaneByIdCommand = new RelayCommand<string>(ActivatePaneById);
        RestoreLayoutCommand = new RelayCommand(RestoreLayout, () => IsLayoutMaximized);
        CancelOperationCommand = new RelayCommand(
            CancelOperation,
            () => IsOperationRunning);
    }

    public ObservableCollection<FilePaneViewModel> Panes { get; } = [];

    public ObservableCollection<string> WorkspaceNames { get; } = [];

    public ObservableCollection<FileOperationFailure> OperationFailures { get; } = [];

    public RelayCommand<string> ActivatePaneByIdCommand { get; }

    public RelayCommand RestoreLayoutCommand { get; }

    public RelayCommand CancelOperationCommand { get; }

    public event Action<string?>? WorkspaceActivated;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string? SelectedWorkspaceName
    {
        get => _selectedWorkspaceName;
        set => SetProperty(ref _selectedWorkspaceName, value);
    }

    public double HorizontalRatio
    {
        get => _horizontalRatio;
        set => SetProperty(ref _horizontalRatio, ClampRatio(value));
    }

    public double VerticalRatio
    {
        get => _verticalRatio;
        set => SetProperty(ref _verticalRatio, ClampRatio(value));
    }

    public bool IsOperationRunning
    {
        get => _isOperationRunning;
        private set
        {
            if (SetProperty(ref _isOperationRunning, value))
            {
                CancelOperationCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string OperationProgressText
    {
        get => _operationProgressText;
        private set => SetProperty(ref _operationProgressText, value);
    }

    public FilePaneViewModel? MaximizedPane
    {
        get => _maximizedPane;
        private set
        {
            if (SetProperty(ref _maximizedPane, value))
            {
                OnPropertyChanged(nameof(IsLayoutMaximized));
                RestoreLayoutCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsLayoutMaximized => MaximizedPane is not null;

    public FilePaneViewModel? ActivePane =>
        Panes.FirstOrDefault(pane =>
            StringComparer.Ordinal.Equals(pane.Id, _focusState.ActivePaneId));

    public FilePaneViewModel? TargetPane =>
        Panes.FirstOrDefault(pane =>
            StringComparer.Ordinal.Equals(pane.Id, _focusState.TargetPaneId));

    public async Task InitializeAsync(string? startupWorkspaceName = null)
    {
        WorkspaceSnapshot? snapshot = null;
        var loadedStartupWorkspace = false;
        if (!string.IsNullOrWhiteSpace(startupWorkspaceName))
        {
            snapshot = await _workspaceStore.LoadWorkspaceAsync(
                startupWorkspaceName);
            if (snapshot is not null)
            {
                SelectedWorkspaceName = startupWorkspaceName;
                loadedStartupWorkspace = true;
            }
        }

        snapshot ??= await _workspaceStore.LoadSessionAsync();
        snapshot ??= CreateDefaultWorkspace();

        await ApplyWorkspaceAsync(snapshot);
        await RefreshWorkspaceNamesAsync();
        _isInitialized = true;
        if (loadedStartupWorkspace)
        {
            WorkspaceActivated?.Invoke(SelectedWorkspaceName);
        }

        StatusMessage = !loadedStartupWorkspace
            ? "已恢复会话；快捷键可在右上角设置中修改。"
            : $"已载入工作区“{SelectedWorkspaceName}”。";
    }

    public async Task SaveSessionAsync()
    {
        if (!_isInitialized || Panes.Count == 0)
        {
            return;
        }

        await _workspaceStore.SaveSessionAsync(CaptureSnapshot("上次会话"));
    }

    public void SetStatusMessage(string message)
    {
        StatusMessage = message;
    }

    public async Task<bool> OpenExternalPathAsync(string path)
    {
        if (ActivePane is null || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(
                    path.Trim().Trim('"')));
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            StatusMessage = $"外部目录路径无效：{exception.Message}";
            return false;
        }

        if (File.Exists(fullPath))
        {
            fullPath = Path.GetDirectoryName(fullPath) ?? fullPath;
        }

        if (!Directory.Exists(fullPath))
        {
            StatusMessage = $"目录不存在或当前不可用：{fullPath}";
            return false;
        }

        await ActivePane.NavigateAsync(fullPath);
        StatusMessage = $"已打开外部目录：{fullPath}";
        return true;
    }

    public async Task SaveNamedWorkspaceAsync(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await _workspaceStore.SaveWorkspaceAsync(name.Trim(), CaptureSnapshot(name.Trim()));
        await RefreshWorkspaceNamesAsync();
        SelectedWorkspaceName = name.Trim();
        WorkspaceActivated?.Invoke(SelectedWorkspaceName);
        StatusMessage = $"工作区“{name.Trim()}”已保存";
    }

    public async Task<bool> LoadSelectedWorkspaceAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedWorkspaceName))
        {
            return false;
        }

        return await LoadWorkspaceAsync(SelectedWorkspaceName);
    }

    public async Task<bool> LoadWorkspaceAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var snapshot = await _workspaceStore.LoadWorkspaceAsync(name);
        if (snapshot is null)
        {
            StatusMessage = $"找不到工作区“{name}”";
            return false;
        }

        await ApplyWorkspaceAsync(snapshot);
        SelectedWorkspaceName = name;
        WorkspaceActivated?.Invoke(SelectedWorkspaceName);
        StatusMessage = $"已载入工作区“{name}”";
        return true;
    }

    public async Task DeleteWorkspaceAsync(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await _workspaceStore.DeleteWorkspaceAsync(name.Trim());
        var wasSelected = StringComparer.OrdinalIgnoreCase.Equals(
            SelectedWorkspaceName,
            name.Trim());
        await RefreshWorkspaceNamesAsync();
        if (wasSelected)
        {
            SelectedWorkspaceName = null;
            WorkspaceActivated?.Invoke(null);
        }

        StatusMessage = $"工作区“{name.Trim()}”已删除";
    }

    public Task ExportWorkspaceAsync(string name, string destinationPath)
    {
        return _workspaceStore.ExportWorkspaceAsync(name, destinationPath);
    }

    public async Task<string> ImportWorkspaceAsync(string sourcePath)
    {
        var name = await _workspaceStore.ImportWorkspaceAsync(sourcePath);
        await RefreshWorkspaceNamesAsync();
        SelectedWorkspaceName = name;
        StatusMessage = $"已导入工作区“{name}”";
        return name;
    }

    public bool HasTransferCollisions(
        FilePaneViewModel destinationPane,
        IReadOnlyList<string> sourcePaths)
    {
        return sourcePaths.Any(source =>
        {
            var name = Path.GetFileName(source.TrimEnd('\\', '/'));
            if (string.IsNullOrWhiteSpace(name))
            {
                return true;
            }

            var destination = Path.Combine(destinationPane.CurrentPath, name);
            return File.Exists(destination) || Directory.Exists(destination);
        });
    }

    public Task<FileOperationResult?> TransferSelectionAsync(
        FileOperationKind kind,
        CollisionBehavior collisionBehavior)
    {
        if (kind is not (FileOperationKind.Copy or FileOperationKind.Move))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        var source = ActivePane;
        var target = TargetPane;
        if (source is null || target is null || source.SelectedItems.Count == 0)
        {
            StatusMessage = "请先在活动窗格选择要操作的文件或文件夹。";
            return Task.FromResult<FileOperationResult?>(null);
        }

        return ExecutePathsToPaneAsync(
            target,
            source.SelectedItems.Select(entry => entry.FullPath).ToArray(),
            kind,
            collisionBehavior,
            source);
    }

    public async Task<FileOperationResult?> ExecutePathsToPaneAsync(
        FilePaneViewModel destinationPane,
        IReadOnlyList<string> sourcePaths,
        FileOperationKind kind,
        CollisionBehavior collisionBehavior,
        FilePaneViewModel? sourcePane = null)
    {
        if (sourcePaths.Count == 0)
        {
            StatusMessage = "没有可操作的项目。";
            return null;
        }

        var result = await ExecuteFileOperationAsync(
            new FileOperationRequest(
                kind,
                sourcePaths,
                destinationPane.CurrentPath,
                collisionBehavior));
        await RefreshAfterOperationAsync(sourcePane, destinationPane);
        return result;
    }

    public async Task<FileOperationResult?> DeleteSelectionAsync(bool permanent)
    {
        var pane = ActivePane;
        if (pane is null || pane.SelectedItems.Count == 0)
        {
            StatusMessage = "请先选择要删除的项目。";
            return null;
        }

        return await DeletePathsAsync(
            pane.SelectedItems
                .Select(entry => entry.FullPath)
                .ToArray(),
            permanent);
    }

    public async Task<FileOperationResult?> DeletePathsAsync(
        IReadOnlyList<string> paths,
        bool permanent)
    {
        var pane = ActivePane;
        if (pane is null || paths.Count == 0)
        {
            StatusMessage = "请先选择要删除的项目。";
            return null;
        }

        var result = await ExecuteFileOperationAsync(
            new FileOperationRequest(
                permanent
                    ? FileOperationKind.PermanentDelete
                    : FileOperationKind.RecycleDelete,
                paths));
        await pane.RefreshCurrentAsync();
        return result;
    }

    public async Task<string?> CreateDirectoryAsync(string name)
    {
        if (ActivePane is null)
        {
            return null;
        }

        var created = await _fileOperationService.CreateDirectoryAsync(
            ActivePane.CurrentPath,
            name);
        await ActivePane.RefreshCurrentAsync();
        StatusMessage = $"已新建文件夹：{Path.GetFileName(created)}";
        return created;
    }

    public async Task<string?> RenameSelectionAsync(string name)
    {
        if (ActivePane?.SelectedItems is not { Count: 1 } selected)
        {
            StatusMessage = "重命名时只能选择一个项目。";
            return null;
        }

        var renamed = await _fileOperationService.RenameAsync(
            selected[0].FullPath,
            name);
        await ActivePane.RefreshCurrentAsync();
        StatusMessage = $"已重命名为：{Path.GetFileName(renamed)}";
        return renamed;
    }

    public void Dispose()
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        foreach (var pane in Panes)
        {
            pane.Dispose();
        }
    }

    private async Task<FileOperationResult> ExecuteFileOperationAsync(
        FileOperationRequest request)
    {
        if (IsOperationRunning)
        {
            throw new InvalidOperationException("已有文件操作正在执行。");
        }

        _operationCancellation = new CancellationTokenSource();
        IsOperationRunning = true;
        OperationFailures.Clear();
        OperationProgressText = "正在准备…";
        var progress = new Progress<FileOperationProgress>(value =>
        {
            OperationProgressText = value.TotalSources == 0
                ? string.Empty
                : $"{value.CompletedSources}/{value.TotalSources}  {value.CurrentPath}";
        });

        try
        {
            var result = await _fileOperationService.ExecuteAsync(
                request,
                progress,
                _operationCancellation.Token);
            foreach (var failure in result.Failures)
            {
                OperationFailures.Add(failure);
            }

            StatusMessage = result.WasCancelled
                ? $"操作已取消；已完成 {result.CompletedCount} 项"
                : $"操作完成：成功 {result.CompletedCount}，跳过 {result.SkippedCount}，失败 {result.Failures.Count}";
            return result;
        }
        finally
        {
            IsOperationRunning = false;
            OperationProgressText = string.Empty;
            _operationCancellation.Dispose();
            _operationCancellation = null;
        }
    }

    private static async Task RefreshAfterOperationAsync(
        FilePaneViewModel? sourcePane,
        FilePaneViewModel destinationPane)
    {
        if (sourcePane is not null && !ReferenceEquals(sourcePane, destinationPane))
        {
            await Task.WhenAll(
                sourcePane.RefreshCurrentAsync(),
                destinationPane.RefreshCurrentAsync());
        }
        else
        {
            await destinationPane.RefreshCurrentAsync();
        }
    }

    private void CancelOperation()
    {
        _operationCancellation?.Cancel();
    }

    private async Task ApplyWorkspaceAsync(WorkspaceSnapshot snapshot)
    {
        foreach (var pane in Panes)
        {
            pane.Dispose();
        }

        Panes.Clear();
        MaximizedPane = null;

        var validPanes = snapshot.Panes
            .Where(pane => PaneIds.Contains(pane.Id, StringComparer.Ordinal))
            .GroupBy(pane => pane.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var initialPaths = ChooseInitialPaths(_driveService.GetDrives());

        for (var index = 0; index < PaneIds.Length; index++)
        {
            var id = PaneIds[index];
            var paneSnapshot = validPanes.GetValueOrDefault(id)
                ?? FilePaneViewModel.CreateDefaultSnapshot(id, initialPaths[index]);
            Panes.Add(new FilePaneViewModel(
                paneSnapshot,
                _listingService,
                _driveService,
                _fileLauncher,
                ActivatePane,
                ToggleMaximize));
        }

        var activeId = PaneIds.Contains(snapshot.ActivePaneId, StringComparer.Ordinal)
            ? snapshot.ActivePaneId
            : PaneIds[0];
        var targetId = PaneIds.Contains(snapshot.TargetPaneId, StringComparer.Ordinal) &&
            !StringComparer.Ordinal.Equals(snapshot.TargetPaneId, activeId)
            ? snapshot.TargetPaneId
            : PaneIds.First(id => !StringComparer.Ordinal.Equals(id, activeId));
        _focusState = new PaneFocusState(activeId, targetId);
        HorizontalRatio = snapshot.HorizontalRatio;
        VerticalRatio = snapshot.VerticalRatio;
        UpdatePaneRoles();

        await Task.WhenAll(Panes.Select(pane => pane.InitializeAsync()));
    }

    private async Task RefreshWorkspaceNamesAsync()
    {
        var selected = SelectedWorkspaceName;
        WorkspaceNames.Clear();
        foreach (var name in await _workspaceStore.ListWorkspaceNamesAsync())
        {
            WorkspaceNames.Add(name);
        }

        SelectedWorkspaceName = selected is not null && WorkspaceNames.Contains(selected)
            ? selected
            : null;
    }

    private WorkspaceSnapshot CaptureSnapshot(string name)
    {
        return new WorkspaceSnapshot(
            WorkspaceSnapshot.CurrentSchemaVersion,
            name,
            HorizontalRatio,
            VerticalRatio,
            Panes.Select(pane => pane.Capture()).ToArray(),
            _focusState.ActivePaneId,
            _focusState.TargetPaneId,
            DateTime.UtcNow);
    }

    private WorkspaceSnapshot CreateDefaultWorkspace()
    {
        var initialPaths = ChooseInitialPaths(_driveService.GetDrives());
        return new WorkspaceSnapshot(
            WorkspaceSnapshot.CurrentSchemaVersion,
            "默认",
            0.5,
            0.5,
            PaneIds
                .Select((id, index) =>
                    FilePaneViewModel.CreateDefaultSnapshot(id, initialPaths[index]))
                .ToArray(),
            PaneIds[0],
            PaneIds[1],
            DateTime.UtcNow);
    }

    private void ActivatePane(FilePaneViewModel pane)
    {
        if (_focusState.Activate(pane.Id))
        {
            UpdatePaneRoles();
            OnPropertyChanged(nameof(ActivePane));
            OnPropertyChanged(nameof(TargetPane));
            StatusMessage = $"活动：{pane.Id}；目标：{_focusState.TargetPaneId}";
        }
    }

    private void ActivatePaneById(string? paneId)
    {
        var pane = Panes.FirstOrDefault(item =>
            StringComparer.Ordinal.Equals(item.Id, paneId));

        if (pane is not null)
        {
            ActivatePane(pane);
        }
    }

    private void ToggleMaximize(FilePaneViewModel pane)
    {
        MaximizedPane = ReferenceEquals(MaximizedPane, pane) ? null : pane;
        StatusMessage = MaximizedPane is null
            ? "已恢复 2×2 四窗格"
            : $"已临时最大化 {pane.Id}";
    }

    private void RestoreLayout()
    {
        if (MaximizedPane is null)
        {
            return;
        }

        MaximizedPane = null;
        StatusMessage = "已恢复 2×2 四窗格";
    }

    private void UpdatePaneRoles()
    {
        foreach (var pane in Panes)
        {
            pane.Role = pane.Id switch
            {
                var id when StringComparer.Ordinal.Equals(id, _focusState.ActivePaneId) =>
                    PaneVisualRole.Active,
                var id when StringComparer.Ordinal.Equals(id, _focusState.TargetPaneId) =>
                    PaneVisualRole.Target,
                _ => PaneVisualRole.None,
            };
        }
    }

    private static IReadOnlyList<string> ChooseInitialPaths(IReadOnlyList<DriveEntry> drives)
    {
        var readyRoots = drives
            .Where(drive => drive.IsReady && Directory.Exists(drive.RootPath))
            .Select(drive => drive.RootPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Default placement: D: top-left, O: top-right, C: bottom-left,
        // and T: bottom-right when those drives are available.
        var preferred = new[] { @"D:\", @"O:\", @"C:\", @"T:\" };
        var ordered = preferred
            .Where(path => readyRoots.Contains(path, StringComparer.OrdinalIgnoreCase))
            .Concat(readyRoots.Where(path =>
                !preferred.Contains(path, StringComparer.OrdinalIgnoreCase)))
            .ToList();

        if (ordered.Count == 0)
        {
            ordered.Add(AppContext.BaseDirectory);
        }

        while (ordered.Count < 4)
        {
            ordered.Add(ordered[ordered.Count % Math.Max(1, ordered.Count)]);
        }

        return ordered.Take(4).ToArray();
    }

    private static double ClampRatio(double value)
    {
        return double.IsFinite(value) ? Math.Clamp(value, 0.2, 0.8) : 0.5;
    }
}

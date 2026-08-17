using System.IO;
using System.Windows;
using MYTC.App.Startup;
using MYTC.App.ViewModels;
using MYTC.App.Windows;
using MYTC.Infrastructure.Configuration;
using MYTC.Infrastructure.Drives;
using MYTC.Infrastructure.Files;
using MYTC.Infrastructure.Shell;

namespace MYTC.App;

public partial class App
{
    private readonly Queue<LaunchRequest> _pendingRequests = [];
    private SingleInstanceCoordinator? _singleInstance;
    private bool _mainWindowReady;
    private Action? _showUpdateCompletionNoticeWhenReady;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var dataRoot = ResolveDataRoot(e.Args);
        var launchRequest = LaunchRequest.Parse(e.Args);
        var uiPreferencesStore = new JsonUiPreferencesStore(dataRoot);
        var startupPreferences = await uiPreferencesStore.LoadAsync();
        var instanceWorkspaceName = launchRequest.WorkspaceName ??
            startupPreferences.LastWorkspaceName;
        _ = TaskbarIdentity.TryInitializeProcessIdentity(
            instanceWorkspaceName);
        var completedUpdateVersion = GetArgumentValue(e.Args, "--update-complete");
        _singleInstance = new SingleInstanceCoordinator(
            dataRoot,
            instanceWorkspaceName);
        if (!_singleInstance.IsPrimary)
        {
            var delivered = await _singleInstance.SendAsync(
                launchRequest,
                TimeSpan.FromSeconds(8));
            if (!delivered)
            {
                MessageBox.Show(
                    "openTC 已在运行，但本次目录请求未能转交给现有窗口。请切换到现有窗口后重试。",
                    "openTC",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            Shutdown();
            return;
        }

        _singleInstance.RequestReceived += OnLaunchRequestReceived;
        _singleInstance.StartListening();

        if (!e.Args.Contains("--data-dir", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                _ = new ShellIntegrationService()
                    .MigrateLegacyFolderAssociationToWinEOnly();
            }
            catch
            {
                // Failed migration must never prevent MYTC itself from starting.
            }
        }

        var driveService = new DriveService();
        var shortcutStore = new JsonShortcutStore(dataRoot);
        var contextMenuStore = new JsonContextMenuStore(dataRoot);
        var tabContextMenuStore = new JsonTabContextMenuStore(dataRoot);
        var autoStartService = new WindowsAutoStartService();
        var shortcutCreationService = new ShellShortcutCreationService();
        var openWithService = new ShellOpenWithService();
        var propertiesService = new ShellPropertiesService();
        var archiveExtractionService = new WinRarArchiveExtractionService();
        var managedRecycleService = new ManagedNetworkRecycleService();
        var recycleBinRestoreService = new ShellRecycleBinRestoreService();
        var viewModel = new MainWindowViewModel(
            new DirectoryListingService(),
            driveService,
            new ShellFileLauncher(),
            new ManagedFileOperationService(),
            new JsonWorkspaceStore(dataRoot, instanceWorkspaceName));

        var window = new MainWindow(
            shortcutStore,
            contextMenuStore,
            tabContextMenuStore,
            uiPreferencesStore,
            shortcutCreationService,
            openWithService,
            propertiesService,
            archiveExtractionService,
            autoStartService,
            managedRecycleService,
            recycleBinRestoreService)
        {
            DataContext = viewModel,
        };
        window.SourceInitialized += (_, _) =>
            window.ApplyWorkspaceAppearance(
                instanceWorkspaceName,
                configuredIconKey: null);
        window.Closing += (_, args) =>
        {
            if (!args.Cancel)
            {
                TaskbarIdentity.TryClearWindowProperties(window);
            }
        };

        MainWindow = window;
        ScheduleUpdateCompletionNotice(window, completedUpdateVersion);
        await window.InitializeSettingsAsync();
        window.Show();
        if (!e.Args.Contains("--skip-initial-setup", StringComparer.OrdinalIgnoreCase))
        {
            await window.ConfirmWinRarExecutableAsync();
        }

        await viewModel.InitializeAsync(
            launchRequest.WorkspaceName ?? window.PreferredWorkspaceName);
        window.ApplyWorkspaceAppearance(
            viewModel.SelectedWorkspaceName,
            viewModel.ActiveWorkspaceIconKey);
        _mainWindowReady = true;
        _showUpdateCompletionNoticeWhenReady?.Invoke();
        _showUpdateCompletionNoticeWhenReady = null;
        await HandleLaunchRequestAsync(launchRequest);
        while (_pendingRequests.TryDequeue(out var pending))
        {
            await HandleLaunchRequestAsync(pending);
        }

    }

    private void ScheduleUpdateCompletionNotice(
        MainWindow window,
        string? completedUpdateVersion)
    {
        if (string.IsNullOrWhiteSpace(completedUpdateVersion))
        {
            return;
        }

        var contentRendered = false;
        var applicationReady = false;
        var queued = false;
        void TryQueueNotice()
        {
            if (queued || !contentRendered || !applicationReady)
            {
                return;
            }

            queued = true;
            _ = Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(() =>
                {
                    window.Activate();
                    window.Focus();
                    MessageBox.Show(
                        window,
                        $"openTC 已升级到 {completedUpdateVersion}。\n\n" +
                        "用户配置目录 data 未被覆盖。",
                        "openTC 升级完成",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }));
        }

        window.ContentRendered += (_, _) =>
        {
            contentRendered = true;
            TryQueueNotice();
        };
        _showUpdateCompletionNoticeWhenReady = () =>
        {
            applicationReady = true;
            TryQueueNotice();
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        _singleInstance = null;
        base.OnExit(e);
    }

    private static string ResolveDataRoot(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(arguments[index], "--data-dir"))
            {
                return Path.GetFullPath(arguments[index + 1]);
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "data");
    }

    private static string? GetArgumentValue(
        IReadOnlyList<string> arguments,
        string name)
    {
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(arguments[index], name))
            {
                return arguments[index + 1].Trim().Trim('\"');
            }
        }

        return null;
    }

    private void OnLaunchRequestReceived(LaunchRequest request)
    {
        _ = Dispatcher.InvokeAsync(async () =>
        {
            if (!_mainWindowReady)
            {
                _pendingRequests.Enqueue(request);
                return;
            }

            await HandleLaunchRequestAsync(request);
        });
    }

    private async Task HandleLaunchRequestAsync(LaunchRequest request)
    {
        if (MainWindow is MainWindow window)
        {
            await window.HandleExternalLaunchAsync(
                request.OpenPath,
                request.WorkspaceName);
        }
    }
}

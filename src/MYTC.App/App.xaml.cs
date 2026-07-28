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

    protected override async void OnStartup(StartupEventArgs e)
    {
        _ = TaskbarIdentity.TryInitializeProcessIdentity();
        base.OnStartup(e);

        var dataRoot = ResolveDataRoot(e.Args);
        var launchRequest = LaunchRequest.Parse(e.Args);
        _singleInstance = new SingleInstanceCoordinator(dataRoot);
        if (!_singleInstance.IsPrimary)
        {
            var delivered = await _singleInstance.SendAsync(
                launchRequest,
                TimeSpan.FromSeconds(8));
            if (!delivered)
            {
                MessageBox.Show(
                    "MYTC 已在运行，但本次目录请求未能转交给现有窗口。请切换到现有窗口后重试。",
                    "MYTC",
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
        var uiPreferencesStore = new JsonUiPreferencesStore(dataRoot);
        var autoStartService = new WindowsAutoStartService();
        var shortcutCreationService = new ShellShortcutCreationService();
        var openWithService = new ShellOpenWithService();
        var managedRecycleService = new ManagedNetworkRecycleService();
        var recycleBinRestoreService = new ShellRecycleBinRestoreService();
        var viewModel = new MainWindowViewModel(
            new DirectoryListingService(),
            driveService,
            new ShellFileLauncher(),
            new ManagedFileOperationService(),
            new JsonWorkspaceStore(dataRoot));

        var window = new MainWindow(
            shortcutStore,
            contextMenuStore,
            tabContextMenuStore,
            uiPreferencesStore,
            shortcutCreationService,
            openWithService,
            autoStartService,
            managedRecycleService,
            recycleBinRestoreService)
        {
            DataContext = viewModel,
        };
        window.SourceInitialized += (_, _) =>
            _ = TaskbarIdentity.TryApplyWindowProperties(window);
        window.Closing += (_, args) =>
        {
            if (!args.Cancel)
            {
                TaskbarIdentity.TryClearWindowProperties(window);
            }
        };

        MainWindow = window;
        await window.InitializeSettingsAsync();
        window.Show();

        await viewModel.InitializeAsync();
        _mainWindowReady = true;
        await HandleLaunchRequestAsync(launchRequest);
        while (_pendingRequests.TryDequeue(out var pending))
        {
            await HandleLaunchRequestAsync(pending);
        }
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
            await window.HandleExternalLaunchAsync(request.OpenPath);
        }
    }
}

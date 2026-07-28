using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MYTC.App.Dialogs;
using MYTC.App.Shortcuts;
using MYTC.App.ViewModels;
using MYTC.App.Views;
using MYTC.App.Windows;
using MYTC.Application.Abstractions;
using MYTC.Application.Configuration;
using MYTC.Domain.Configuration;
using MYTC.Domain.Drives;
using MYTC.Domain.Files;
using MYTC.Infrastructure.Configuration;
using MYTC.Infrastructure.Files;

namespace MYTC.Tests.Ui;

public sealed class MainWindowSmokeTests
{
    [Fact]
    public async Task MainWindow_RendersFourPanes_AndFixedTabReturnsHome()
    {
        var sandbox = Path.Combine(
            Path.GetTempPath(),
            "MYTC.Tests",
            Guid.NewGuid().ToString("N"));
        var dataRoot = Path.Combine(sandbox, "data");
        var fixedHome = Directory.CreateDirectory(Path.Combine(sandbox, "fixed-home")).FullName;
        var nested = Directory.CreateDirectory(
            Path.Combine(fixedHome, "nested")).FullName;
        Directory.CreateDirectory(Path.Combine(nested, "child"));
        await File.WriteAllTextAsync(
            Path.Combine(nested, "inside.txt"),
            "inside");
        var away = Directory.CreateDirectory(Path.Combine(sandbox, "away")).FullName;
        await File.WriteAllTextAsync(Path.Combine(fixedHome, "sample.txt"), "sample");
        var directoryShortcut = Assert.Single(
            await new ShellShortcutCreationService().CreateAsync(
                [away],
                fixedHome));

        var completion = new TaskCompletionSource<Exception?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var application = new MYTC.App.App();
                application.InitializeComponent();
                var shortcutStore = new JsonShortcutStore(dataRoot);
                var contextMenuStore = new JsonContextMenuStore(dataRoot);
                var tabContextMenuStore =
                    new JsonTabContextMenuStore(dataRoot);
                var uiPreferencesStore = new JsonUiPreferencesStore(dataRoot);
                shortcutStore.SaveAsync(ShortcutDefaults.Create())
                    .GetAwaiter()
                    .GetResult();
                var deleteConfirmationCount = 0;
                var openWithService = new RecordingOpenWithService();
                var viewModel = new MainWindowViewModel(
                    new FakeListingService(),
                    new FakeDriveService(sandbox),
                    new ShellFileLauncher(),
                    new ManagedFileOperationService(),
                    new JsonWorkspaceStore(dataRoot));
                var window = new MYTC.App.MainWindow(
                    shortcutStore,
                    contextMenuStore,
                    tabContextMenuStore,
                    uiPreferencesStore,
                    new ShellShortcutCreationService(),
                    openWithService,
                    new WindowsAutoStartService(),
                    new ManagedNetworkRecycleService(),
                    new ShellRecycleBinRestoreService(),
                    (permanent, count) =>
                    {
                        Assert.False(permanent);
                        Assert.Equal(1, count);
                        deleteConfirmationCount++;
                        return false;
                    })
                {
                    DataContext = viewModel,
                };
                window.Show();
                var workingArea = SystemParameters.WorkArea;
                Assert.True(window.Left >= workingArea.Left);
                Assert.True(window.Top >= workingArea.Top);
                Assert.True(
                    window.Left + window.ActualWidth <=
                    workingArea.Right + 1);
                Assert.True(
                    window.Top + window.ActualHeight <=
                    workingArea.Bottom + 1);
                Assert.NotNull(window.Icon);
                Assert.True(
                    TaskbarIdentity.TryApplyWindowProperties(window));

                _ = window.Dispatcher.BeginInvoke(async () =>
                {
                    try
                    {
                        await window.InitializeSettingsAsync();
                        await viewModel.InitializeAsync();
                        window.UpdateLayout();

                        Assert.Equal(4, viewModel.Panes.Count);
                        Assert.Equal(
                            4,
                            FindVisualChildren<FilePaneControl>(window)
                                .Count(control => control.IsVisible));
                        Assert.True(window.ActualWidth >= 1000);
                        Assert.True(window.ActualHeight >= 620);
                        Assert.NotEmpty(FindVisualChildren<Menu>(window));
                        var operationToolbar = Assert.IsType<Border>(
                            window.FindName("OperationToolbarBorder"));
                        Assert.Equal(
                            Visibility.Collapsed,
                            operationToolbar.Visibility);
                        var focusedFileGrid =
                            FindVisualChildren<DataGrid>(window).First();
                        focusedFileGrid.SelectedIndex = 0;
                        focusedFileGrid.Focus();
                        Keyboard.Focus(focusedFileGrid);
                        var deleteKeyEvent = new KeyEventArgs(
                            Keyboard.PrimaryDevice,
                            PresentationSource.FromVisual(window),
                            Environment.TickCount,
                            Key.Delete)
                        {
                            RoutedEvent = Keyboard.PreviewKeyDownEvent,
                        };
                        focusedFileGrid.RaiseEvent(deleteKeyEvent);
                        Assert.True(deleteKeyEvent.Handled);
                        Assert.Equal(1, deleteConfirmationCount);
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        Assert.Same(
                            focusedFileGrid,
                            Keyboard.FocusedElement);
                        var secondDeleteKeyEvent = new KeyEventArgs(
                            Keyboard.PrimaryDevice,
                            PresentationSource.FromVisual(window),
                            Environment.TickCount,
                            Key.Delete)
                        {
                            RoutedEvent = Keyboard.PreviewKeyDownEvent,
                        };
                        typeof(KeyEventArgs)
                            .GetField(
                                "_isRepeat",
                                System.Reflection.BindingFlags.Instance |
                                System.Reflection.BindingFlags.NonPublic)!
                            .SetValue(secondDeleteKeyEvent, true);
                        Assert.True(secondDeleteKeyEvent.IsRepeat);
                        focusedFileGrid.RaiseEvent(secondDeleteKeyEvent);
                        Assert.True(secondDeleteKeyEvent.Handled);
                        Assert.Equal(2, deleteConfirmationCount);

                        var focusAddressBindings = ShortcutDefaults.Create()
                            .Bindings
                            .Where(binding =>
                                binding.Action !=
                                ShortcutAction.FocusAddressBar)
                            .Append(new ShortcutBinding(
                                ShortcutAction.FocusAddressBar,
                                "F12"))
                            .ToArray();
                        await shortcutStore.SaveAsync(
                            new ShortcutConfiguration(
                                ShortcutConfiguration.CurrentSchemaVersion,
                                focusAddressBindings));
                        await window.InitializeSettingsAsync();
                        focusedFileGrid.Focus();
                        Keyboard.Focus(focusedFileGrid);
                        var focusAddressEvent = new KeyEventArgs(
                            Keyboard.PrimaryDevice,
                            PresentationSource.FromVisual(window),
                            Environment.TickCount,
                            Key.F12)
                        {
                            RoutedEvent = Keyboard.PreviewKeyDownEvent,
                        };
                        focusedFileGrid.RaiseEvent(focusAddressEvent);
                        var focusedAddress = Assert.IsType<TextBox>(
                            Keyboard.FocusedElement);
                        Assert.Equal(
                            "AddressTextBox",
                            focusedAddress.Name);
                        Assert.Equal(
                            focusedAddress.Text.Length,
                            focusedAddress.SelectionLength);
                        var fileDropPaths =
                            new System.Collections.Specialized.StringCollection
                            {
                                away,
                            };
                        Clipboard.SetFileDropList(fileDropPaths);
                        focusedAddress.SelectAll();
                        ApplicationCommands.Paste.Execute(
                            null,
                            focusedAddress);
                        Assert.Equal(away, focusedAddress.Text);
                        Clipboard.SetText(fixedHome);
                        focusedAddress.SelectAll();
                        ApplicationCommands.Paste.Execute(
                            null,
                            focusedAddress);
                        Assert.Equal(fixedHome, focusedAddress.Text);
                        Clipboard.Clear();

                        await shortcutStore.SaveAsync(
                            ShortcutDefaults.Create());
                        await window.InitializeSettingsAsync();
                        var operationToolbarMenuItem = Assert.IsType<MenuItem>(
                            window.FindName("OperationToolbarMenuItem"));
                        operationToolbarMenuItem.IsChecked = true;
                        operationToolbarMenuItem.RaiseEvent(
                            new RoutedEventArgs(MenuItem.ClickEvent));
                        await Task.Delay(100);
                        Assert.Equal(
                            Visibility.Visible,
                            operationToolbar.Visibility);
                        Assert.True(
                            (await uiPreferencesStore.LoadAsync())
                                .IsOperationToolbarVisible);
                        operationToolbarMenuItem.IsChecked = false;
                        operationToolbarMenuItem.RaiseEvent(
                            new RoutedEventArgs(MenuItem.ClickEvent));
                        await Task.Delay(100);
                        Assert.Equal(
                            Visibility.Collapsed,
                            operationToolbar.Visibility);

                        var pane = viewModel.Panes[0];
                        var paneControl =
                            FindVisualChildren<FilePaneControl>(window)
                                .First(control =>
                                    ReferenceEquals(
                                        control.DataContext,
                                        pane));
                        var tabScrollViewer = Assert.IsType<ScrollViewer>(
                            paneControl.FindName("TabScrollViewer"));
                        var tabCountBeforeDoubleClick = pane.Tabs.Count;
                        var tabDoubleClick = new MouseButtonEventArgs(
                            Mouse.PrimaryDevice,
                            Environment.TickCount,
                            MouseButton.Left)
                        {
                            RoutedEvent = Mouse.PreviewMouseDownEvent,
                        };
                        typeof(MouseButtonEventArgs)
                            .GetProperty(
                                nameof(MouseButtonEventArgs.ClickCount))!
                            .GetSetMethod(nonPublic: true)!
                            .Invoke(tabDoubleClick, [2]);
                        tabScrollViewer.RaiseEvent(tabDoubleClick);
                        await Task.Delay(100);
                        Assert.True(tabDoubleClick.Handled);
                        Assert.Equal(
                            tabCountBeforeDoubleClick + 1,
                            pane.Tabs.Count);
                        await pane.NavigateAsync(fixedHome);
                        window.UpdateLayout();
                        var paneFileGrid = Assert.IsType<DataGrid>(
                            paneControl.FindName("FileGrid"));
                        await pane.NavigateAsync(nested);
                        paneFileGrid.Focus();
                        Keyboard.Focus(paneFileGrid);
                        var backspaceEvent = new KeyEventArgs(
                            Keyboard.PrimaryDevice,
                            PresentationSource.FromVisual(window),
                            Environment.TickCount,
                            Key.Back)
                        {
                            RoutedEvent = Keyboard.PreviewKeyDownEvent,
                        };
                        paneFileGrid.RaiseEvent(backspaceEvent);
                        Assert.True(backspaceEvent.Handled);
                        await Task.Delay(100);
                        Assert.Equal(fixedHome, pane.CurrentPath);
                        Assert.Equal(0, paneFileGrid.SelectedIndex);
                        Assert.Same(
                            pane.Items[0],
                            paneFileGrid.SelectedItem);
                        Assert.IsType<DataGridCell>(
                            Keyboard.FocusedElement);
                        var focusSink = Assert.IsAssignableFrom<Button>(
                            FindVisualChildren<Button>(window).First());
                        focusSink.Focus();
                        Keyboard.Focus(focusSink);
                        var downEvent = new KeyEventArgs(
                            Keyboard.PrimaryDevice,
                            PresentationSource.FromVisual(window),
                            Environment.TickCount,
                            Key.Down)
                        {
                            RoutedEvent = Keyboard.PreviewKeyDownEvent,
                        };
                        window.RaiseEvent(downEvent);
                        Assert.True(downEvent.Handled);
                        Assert.Equal(1, paneFileGrid.SelectedIndex);
                        Assert.Same(
                            pane.Items[1],
                            paneFileGrid.SelectedItem);
                        var repeatedDownEvent = new KeyEventArgs(
                            Keyboard.PrimaryDevice,
                            PresentationSource.FromVisual(window),
                            Environment.TickCount,
                            Key.Down)
                        {
                            RoutedEvent = Keyboard.PreviewKeyDownEvent,
                        };
                        window.RaiseEvent(repeatedDownEvent);
                        Assert.True(repeatedDownEvent.Handled);
                        Assert.Equal(2, paneFileGrid.SelectedIndex);
                        Assert.Same(
                            pane.Items[2],
                            paneFileGrid.SelectedItem);
                        var nestedEntry = Assert.Single(
                            pane.Items,
                            item => item.Kind == EntryKind.Directory &&
                                item.Name == "nested");
                        paneFileGrid.SelectedItem = nestedEntry;
                        focusSink.Focus();
                        Keyboard.Focus(focusSink);
                        var enterEvent = new KeyEventArgs(
                            Keyboard.PrimaryDevice,
                            PresentationSource.FromVisual(window),
                            Environment.TickCount,
                            Key.Enter)
                        {
                            RoutedEvent = Keyboard.PreviewKeyDownEvent,
                        };
                        window.RaiseEvent(enterEvent);
                        Assert.True(enterEvent.Handled);
                        await Task.Delay(100);
                        Assert.Equal(nested, pane.CurrentPath);
                        Assert.Equal(0, paneFileGrid.SelectedIndex);
                        focusSink.Focus();
                        Keyboard.Focus(focusSink);
                        var enterNavigationDownEvent = new KeyEventArgs(
                            Keyboard.PrimaryDevice,
                            PresentationSource.FromVisual(window),
                            Environment.TickCount,
                            Key.Down)
                        {
                            RoutedEvent = Keyboard.PreviewKeyDownEvent,
                        };
                        window.RaiseEvent(enterNavigationDownEvent);
                        Assert.True(enterNavigationDownEvent.Handled);
                        Assert.Equal(1, paneFileGrid.SelectedIndex);
                        Assert.Same(
                            pane.Items[1],
                            paneFileGrid.SelectedItem);
                        var enterNavigationBackspaceEvent = new KeyEventArgs(
                            Keyboard.PrimaryDevice,
                            PresentationSource.FromVisual(window),
                            Environment.TickCount,
                            Key.Back)
                        {
                            RoutedEvent = Keyboard.PreviewKeyDownEvent,
                        };
                        window.RaiseEvent(enterNavigationBackspaceEvent);
                        Assert.True(enterNavigationBackspaceEvent.Handled);
                        await Task.Delay(100);
                        Assert.Equal(fixedHome, pane.CurrentPath);
                        var switchedPane = viewModel.Panes[1];
                        await switchedPane.NavigateAsync(fixedHome);
                        var switchedPaneControl =
                            FindVisualChildren<FilePaneControl>(window)
                                .First(control =>
                                    ReferenceEquals(
                                        control.DataContext,
                                        switchedPane));
                        var switchedPaneGrid = Assert.IsType<DataGrid>(
                            switchedPaneControl.FindName("FileGrid"));
                        switchedPaneGrid.SelectedIndex = 0;
                        var switchPaneClick = new MouseButtonEventArgs(
                            Mouse.PrimaryDevice,
                            Environment.TickCount,
                            MouseButton.Left)
                        {
                            RoutedEvent = Mouse.PreviewMouseDownEvent,
                        };
                        switchedPaneControl.RaiseEvent(switchPaneClick);
                        Assert.Same(switchedPane, viewModel.ActivePane);
                        focusSink.Focus();
                        Keyboard.Focus(focusSink);
                        var switchedPaneDownEvent = new KeyEventArgs(
                            Keyboard.PrimaryDevice,
                            PresentationSource.FromVisual(window),
                            Environment.TickCount,
                            Key.Down)
                        {
                            RoutedEvent = Keyboard.PreviewKeyDownEvent,
                        };
                        window.RaiseEvent(switchedPaneDownEvent);
                        Assert.True(switchedPaneDownEvent.Handled);
                        Assert.Equal(1, switchedPaneGrid.SelectedIndex);
                        Assert.Equal(0, paneFileGrid.SelectedIndex);
                        await pane.NavigateAsync(fixedHome);
                        Assert.Contains(
                            FindVisualChildren<Image>(window),
                            image => image.Source is not null);
                        var sampleEntry = Assert.Single(
                            pane.Items,
                            item => item.Name == "sample.txt");
                        var shortcutEntry = Assert.Single(
                            pane.Items,
                            item => StringComparer.OrdinalIgnoreCase.Equals(
                                item.FullPath,
                                directoryShortcut));
                        await pane.OpenEntryAsync(shortcutEntry);
                        Assert.Equal(away, pane.CurrentPath);
                        await pane.NavigateAsync(fixedHome);
                        pane.SetSelectedItems([sampleEntry]);
                        var contextMenu = new ContextMenu();
                        window.PopulateContextMenu(contextMenu, pane);
                        var copyPathItem = Assert.Single(
                            contextMenu.Items.OfType<MenuItem>(),
                            item => Equals(item.Header, "复制完整路径"));
                        var newSubmenu = Assert.Single(
                            contextMenu.Items.OfType<MenuItem>(),
                            item => Equals(
                                item.Header,
                                "新建（_W）"));
                        Assert.Contains(
                            newSubmenu.Items.OfType<MenuItem>(),
                            item => Equals(
                                item.Header,
                                "文件夹（_F）"));
                        Assert.False(
                            InputMethod.GetIsInputMethodEnabled(contextMenu));
                        var openWithItem = Assert.Single(
                            contextMenu.Items.OfType<MenuItem>(),
                            item => Equals(
                                item.Header,
                                "打开方式（_H）"));
                        Assert.True(openWithItem.IsEnabled);
                        openWithItem.RaiseEvent(
                            new RoutedEventArgs(MenuItem.ClickEvent));
                        await Dispatcher.Yield(
                            DispatcherPriority.ContextIdle);
                        Assert.Equal(
                            sampleEntry.FullPath,
                            openWithService.LastFilePath);
                        copyPathItem.RaiseEvent(
                            new RoutedEventArgs(MenuItem.ClickEvent));
                        await Dispatcher.Yield(
                            DispatcherPriority.ContextIdle);
                        await Task.Delay(100);
                        Assert.Equal(
                            sampleEntry.FullPath,
                            Clipboard.GetText());
                        Clipboard.Clear();
                        var tab = pane.ActiveTab;
                        await pane.PinTabToCurrentDirectoryAsync(tab);
                        Assert.True(tab.IsFixed);
                        Assert.Equal(fixedHome, tab.FixedPath);
                        Assert.Equal("fixed-home", tab.CustomTitle);
                        await pane.NavigateAsync(away);
                        Assert.Equal(away, pane.CurrentPath);
                        await pane.SelectTabAsync(tab);
                        Assert.Equal(fixedHome, pane.CurrentPath);
                        var targetPane = Assert.IsType<FilePaneViewModel>(
                            viewModel.TargetPane);
                        var dragTargetCount = targetPane.Tabs.Count;
                        await window.HandleTabDropAsync(
                            pane,
                            targetPane,
                            tab);
                        Assert.Equal(
                            dragTargetCount + 1,
                            targetPane.Tabs.Count);
                        Assert.Equal(
                            tab.CurrentPath,
                            targetPane.ActiveTab.CurrentPath);
                        pane.RequestActivation();
                        var targetTabCount = targetPane.Tabs.Count;
                        var tabContextMenu = new ContextMenu();
                        window.PopulateTabContextMenu(
                            tabContextMenu,
                            pane,
                            tab);
                        var copyTabItem = Assert.Single(
                            tabContextMenu.Items.OfType<MenuItem>(),
                            item => Equals(
                                item.Header,
                                "复制标签到目标窗格（_T）"));
                        Assert.True(copyTabItem.IsEnabled);
                        copyTabItem.RaiseEvent(
                            new RoutedEventArgs(MenuItem.ClickEvent));
                        await Task.Delay(100);
                        Assert.Equal(
                            targetTabCount + 1,
                            targetPane.Tabs.Count);
                        Assert.Equal(
                            tab.CustomTitle,
                            targetPane.ActiveTab.CustomTitle);
                        Assert.Equal(
                            tab.FixedPath,
                            targetPane.ActiveTab.FixedPath);
                        Assert.Equal(
                            tab.CurrentPath,
                            targetPane.ActiveTab.CurrentPath);
                        Assert.Equal(
                            tab.BackHistory,
                            targetPane.ActiveTab.BackHistory);
                        Assert.Equal(
                            tab.ForwardHistory,
                            targetPane.ActiveTab.ForwardHistory);
                        Assert.Equal(
                            tab.Sort,
                            targetPane.ActiveTab.Sort);

                        await viewModel.SaveNamedWorkspaceAsync("UI 冒烟测试");
                        Assert.Contains("UI 冒烟测试", viewModel.WorkspaceNames);

                        var repositoryRoot = FindRepositoryRoot();
                        SaveWindowPreview(
                            window,
                            repositoryRoot,
                            "main-window-smoke.png");

                        var shortcutManager = new ShortcutManager(shortcutStore);
                        await shortcutManager.InitializeAsync();
                        var shortcutDialog =
                            new ShortcutSettingsDialog(shortcutManager)
                            {
                                Owner = window,
                            };
                        shortcutDialog.Show();
                        shortcutDialog.UpdateLayout();
                        Assert.Equal(
                            3,
                            shortcutDialog.Rows.Count(row =>
                                row.Action == ShortcutAction.RecycleDelete));
                        Assert.True(shortcutDialog.ActualWidth >= 650);
                        SaveWindowPreview(
                            shortcutDialog,
                            repositoryRoot,
                            "shortcut-settings-smoke.png");
                        shortcutDialog.Hide();

                        var captureDialog = new ShortcutCaptureDialog("移到回收站")
                        {
                            Owner = window,
                        };
                        captureDialog.Loaded += (_, _) =>
                        {
                            captureDialog.UpdateLayout();
                            SaveWindowPreview(
                                captureDialog,
                                repositoryRoot,
                                "shortcut-capture-smoke.png");
                            _ = captureDialog.Dispatcher.BeginInvoke(() =>
                            {
                                var keyEvent = new KeyEventArgs(
                                    Keyboard.PrimaryDevice,
                                    PresentationSource.FromVisual(captureDialog),
                                    Environment.TickCount,
                                    Key.Delete)
                                {
                                    RoutedEvent = Keyboard.PreviewKeyDownEvent,
                                };
                                captureDialog.RaiseEvent(keyEvent);
                            });
                        };
                        Assert.True(captureDialog.ShowDialog());
                        Assert.Equal("Del", captureDialog.Gesture);

                        var contextMenuDialog =
                            new ContextMenuSettingsDialog(
                                ContextMenuDefaults.Create(),
                                contextMenuStore)
                            {
                                Owner = window,
                            };
                        contextMenuDialog.Show();
                        contextMenuDialog.UpdateLayout();
                        Assert.Contains(
                            contextMenuDialog.Rows,
                            row => row.Action ==
                                ContextMenuAction.CopyFullPath);
                        Assert.Contains(
                            contextMenuDialog.Rows,
                            row => row.Kind ==
                                ContextMenuItemKind.Submenu &&
                                row.Id == "new-submenu");
                        Assert.Contains(
                            contextMenuDialog.Rows,
                            row => row.Action ==
                                ContextMenuAction.CreateDirectory &&
                                row.ParentId == "new-submenu");
                        Assert.Contains(
                            contextMenuDialog.Rows,
                            row => row.Action ==
                                ContextMenuAction.UndoDelete);
                        SaveWindowPreview(
                            contextMenuDialog,
                            repositoryRoot,
                            "context-menu-settings-smoke.png");
                        contextMenuDialog.Hide();

                        var tabContextMenuDialog =
                            new TabContextMenuSettingsDialog(
                                TabContextMenuDefaults.Create(),
                                tabContextMenuStore)
                            {
                                Owner = window,
                            };
                        tabContextMenuDialog.Show();
                        tabContextMenuDialog.UpdateLayout();
                        Assert.Contains(
                            tabContextMenuDialog.Rows,
                            row => row.Action ==
                                TabContextMenuAction.CopyToTargetPane);
                        SaveWindowPreview(
                            tabContextMenuDialog,
                            repositoryRoot,
                            "tab-context-menu-settings-smoke.png");
                        tabContextMenuDialog.Hide();

                        var globalSettingsDialog =
                            new GlobalSettingsDialog(
                                startWithWindows: false,
                                confirmRecycleDelete: true)
                            {
                                Owner = window,
                            };
                        globalSettingsDialog.Show();
                        globalSettingsDialog.UpdateLayout();
                        Assert.False(globalSettingsDialog.StartWithWindows);
                        Assert.True(globalSettingsDialog.ConfirmRecycleDelete);
                        SaveWindowPreview(
                            globalSettingsDialog,
                            repositoryRoot,
                            "global-settings-smoke.png");
                        globalSettingsDialog.Hide();
                        TaskbarIdentity.TryClearWindowProperties(window);
                        viewModel.Dispose();
                        window.Hide();
                        completion.TrySetResult(null);
                    }
                    catch (Exception exception)
                    {
                        completion.TrySetResult(exception);
                    }
                    finally
                    {
                        Dispatcher.CurrentDispatcher.BeginInvokeShutdown(
                            DispatcherPriority.Send);
                    }
                });

                Dispatcher.Run();
            }
            catch (Exception exception)
            {
                completion.TrySetResult(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var error = await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
        thread.Join(TimeSpan.FromSeconds(5));

        try
        {
            Assert.Null(error);
        }
        finally
        {
            DeleteVerifiedSandbox(sandbox);
        }
    }

    private static void SaveWindowPreview(
        Window window,
        string repositoryRoot,
        string fileName)
    {
        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var bitmap = new RenderTargetBitmap(
            width,
            height,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(window);

        var outputDirectory = Path.Combine(
            repositoryRoot,
            "artifacts",
            "test-results");
        Directory.CreateDirectory(outputDirectory);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(
            Path.Combine(outputDirectory, fileName));
        encoder.Save(stream);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MYTC.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("无法确定测试仓库根目录。");
    }

    private static void DeleteVerifiedSandbox(string sandbox)
    {
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        var resolved = Path.GetFullPath(sandbox);
        Assert.StartsWith(tempRoot, resolved, StringComparison.OrdinalIgnoreCase);
        if (Directory.Exists(resolved))
        {
            Directory.Delete(resolved, recursive: true);
        }
    }

    private sealed class FakeDriveService(string root) : IDriveService
    {
        public IReadOnlyList<DriveEntry> GetDrives()
        {
            return
            [
                new DriveEntry(
                    Path.GetPathRoot(root) ?? root,
                    "测试盘",
                    DriveTypeKind.Fixed,
                    true),
            ];
        }
    }

    private sealed class FakeFileLauncher : IFileLauncher
    {
        public void Open(string path)
        {
        }

        public string? TryResolveShortcutTarget(string path)
        {
            return null;
        }
    }

    private sealed class RecordingOpenWithService : IOpenWithService
    {
        public string? LastFilePath { get; private set; }

        public void Show(string filePath, nint ownerHandle)
        {
            LastFilePath = filePath;
            Assert.NotEqual(nint.Zero, ownerHandle);
        }
    }

    private sealed class FakeListingService : IDirectoryListingService
    {
        public Task<IReadOnlyList<FileSystemEntry>> ListAsync(
            string path,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(path))
            {
                throw new DirectoryNotFoundException(path);
            }

            IReadOnlyList<FileSystemEntry> entries = new DirectoryInfo(path)
                .EnumerateFileSystemInfos()
                .Select(item => item is DirectoryInfo
                    ? new FileSystemEntry(
                        item.FullName,
                        item.Name,
                        EntryKind.Directory,
                        item.LastWriteTime,
                        "文件夹",
                        null)
                    : new FileSystemEntry(
                        item.FullName,
                        item.Name,
                        EntryKind.File,
                        item.LastWriteTime,
                        "文件",
                        ((FileInfo)item).Length))
                .ToArray();
            return Task.FromResult(entries);
        }
    }
}

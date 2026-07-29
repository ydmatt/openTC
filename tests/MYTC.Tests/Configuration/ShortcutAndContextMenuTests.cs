using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;
using MYTC.App.Menus;
using MYTC.App.Shortcuts;
using MYTC.Application.Configuration;
using MYTC.Application.Files;
using MYTC.Domain.Configuration;
using MYTC.Domain.Files;
using MYTC.Infrastructure.Configuration;

namespace MYTC.Tests.Configuration;

public sealed class ShortcutAndContextMenuTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(
        Path.GetTempPath(),
        "MYTC.Tests",
        Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("ctrl+d,p", "Ctrl+D, P")]
    [InlineData("shift+f8", "Shift+F8")]
    [InlineData("control+1", "Ctrl+1")]
    [InlineData("delete", "Del")]
    public void NormalizeGesture_SupportsCombinationAndSequence(
        string input,
        string expected)
    {
        Assert.Equal(expected, ShortcutManager.NormalizeGesture(input));
    }

    [Fact]
    public void DefaultShortcuts_AreValidAndUnique()
    {
        var defaults = ShortcutDefaults.Create();

        ShortcutManager.Validate(defaults);
        Assert.Contains(
            defaults.Bindings,
            binding => binding is
            {
                Action: ShortcutAction.RecycleDelete,
                Gesture: "Del",
            });
        Assert.Contains(
            defaults.Bindings,
            binding => binding is
            {
                Action: ShortcutAction.PermanentDelete,
                Gesture: "Shift+Del",
            });
        Assert.Contains(
            defaults.Bindings,
            binding => binding is
            {
                Action: ShortcutAction.FocusAddressBar,
                Gesture: "Alt+D",
            });
        Assert.Contains(
            defaults.Bindings,
            binding => binding is
            {
                Action: ShortcutAction.RecycleDelete,
                Gesture: "Ctrl+D",
            });
        Assert.Contains(
            defaults.Bindings,
            binding => binding is
            {
                Action: ShortcutAction.NavigateUp,
                Gesture: "Backspace",
            });
        Assert.Equal(
            "Ctrl+Shift+T",
            ShortcutManager.FormatChord(
                ModifierKeys.Control | ModifierKeys.Shift,
                Key.T));
    }

    [Fact]
    public void Validate_RejectsDuplicateAndAmbiguousPrefix()
    {
        var duplicate = new ShortcutConfiguration(
            1,
            [
                new(ShortcutAction.CopyToTarget, "F5"),
                new(ShortcutAction.MoveToTarget, "f5"),
            ]);
        var prefix = new ShortcutConfiguration(
            1,
            [
                new(ShortcutAction.CopyToTarget, "Ctrl+D"),
                new(ShortcutAction.MoveToTarget, "Ctrl+D, P"),
            ]);

        Assert.Throws<ArgumentException>(() => ShortcutManager.Validate(duplicate));
        Assert.Throws<ArgumentException>(() => ShortcutManager.Validate(prefix));

        var sameActionPrefix = new ShortcutConfiguration(
            ShortcutConfiguration.CurrentSchemaVersion,
            [
                new(ShortcutAction.CopyToTarget, "Ctrl+D"),
                new(ShortcutAction.CopyToTarget, "Ctrl+D, P"),
            ]);
        Assert.Throws<ArgumentException>(() =>
            ShortcutManager.Validate(sameActionPrefix));
    }

    [Fact]
    public async Task JsonStores_RoundTripUserConfiguration()
    {
        var shortcutStore = new JsonShortcutStore(_sandbox);
        var contextMenuStore = new JsonContextMenuStore(_sandbox);
        var shortcuts = ShortcutDefaults.Create() with
        {
            Bindings =
            [
                new ShortcutBinding(ShortcutAction.CopyToTarget, "Ctrl+D, P"),
            ],
        };
        var menu = new ContextMenuConfiguration(
            ContextMenuConfiguration.CurrentSchemaVersion,
            [
                new ContextMenuItemDefinition(
                    "external-test",
                    ContextMenuItemKind.ExternalProgram,
                    "测试工具",
                    null,
                    @"D:\Tools\Test.exe",
                    "{path}",
                    true),
            ]);

        await shortcutStore.SaveAsync(shortcuts);
        await contextMenuStore.SaveAsync(menu);
        var loadedShortcuts = await shortcutStore.LoadAsync();
        var loadedMenu = await contextMenuStore.LoadAsync();

        Assert.Contains(
            loadedShortcuts.Bindings,
            binding => binding.Action == ShortcutAction.CopyToTarget &&
                binding.Gesture == "Ctrl+D, P");
        Assert.Single(loadedMenu.Items);
        Assert.Equal("测试工具", loadedMenu.Items[0].Label);
    }

    [Fact]
    public async Task ShortcutStore_PreservesMultipleBindings_AndNamedSchemes()
    {
        var store = new JsonShortcutStore(_sandbox);
        var configuration = new ShortcutConfiguration(
            ShortcutConfiguration.CurrentSchemaVersion,
            [
                new(ShortcutAction.RecycleDelete, "F8"),
                new(ShortcutAction.RecycleDelete, "Del"),
                new(ShortcutAction.PermanentDelete, "Shift+F8"),
                new(ShortcutAction.PermanentDelete, "Shift+Del"),
            ]);

        await store.SaveAsync(configuration);
        await store.SaveSchemeAsync("标书方案", configuration);
        var loaded = await store.LoadAsync();
        var schemeNames = await store.ListSchemeNamesAsync();
        var scheme = await store.LoadSchemeAsync("标书方案");

        Assert.Equal(4, loaded.Bindings.Count);
        Assert.Equal(2, loaded.Bindings.Count(binding =>
            binding.Action == ShortcutAction.RecycleDelete));
        Assert.Contains("标书方案", schemeNames);
        Assert.NotNull(scheme);
        Assert.Equal(configuration.Bindings, scheme.Bindings);
    }

    [Fact]
    public async Task Version1Configuration_MigratesDeleteShortcuts()
    {
        Directory.CreateDirectory(_sandbox);
        var oldConfiguration = new ShortcutConfiguration(
            1,
            [
                new(ShortcutAction.RecycleDelete, "F8"),
                new(ShortcutAction.PermanentDelete, "Shift+F8"),
            ]);
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() },
        };
        await File.WriteAllTextAsync(
            Path.Combine(_sandbox, "shortcuts.json"),
            JsonSerializer.Serialize(oldConfiguration, options));

        var loaded = await new JsonShortcutStore(_sandbox).LoadAsync();

        Assert.Equal(ShortcutConfiguration.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Contains(loaded.Bindings, binding =>
            binding.Action == ShortcutAction.RecycleDelete &&
            binding.Gesture == "Del");
        Assert.Contains(loaded.Bindings, binding =>
            binding.Action == ShortcutAction.PermanentDelete &&
            binding.Gesture == "Shift+Del");
        Assert.Contains(loaded.Bindings, binding =>
            binding.Action == ShortcutAction.FocusAddressBar &&
            binding.Gesture == "Alt+D");
        Assert.Contains(loaded.Bindings, binding =>
            binding.Action == ShortcutAction.NavigateUp &&
            binding.Gesture == "Backspace");
        Assert.Contains(loaded.Bindings, binding =>
            binding.Action == ShortcutAction.RecycleDelete &&
            binding.Gesture == "Ctrl+D");
    }

    [Fact]
    public async Task ShortcutMigration_DoesNotBreakExistingCtrlDSequence()
    {
        Directory.CreateDirectory(_sandbox);
        var oldConfiguration = new ShortcutConfiguration(
            3,
            [
                new(ShortcutAction.CopyToTarget, "Ctrl+D, P"),
            ]);
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() },
        };
        await File.WriteAllTextAsync(
            Path.Combine(_sandbox, "shortcuts.json"),
            JsonSerializer.Serialize(oldConfiguration, options));

        var loaded = await new JsonShortcutStore(_sandbox).LoadAsync();

        Assert.Contains(loaded.Bindings, binding =>
            binding.Action == ShortcutAction.CopyToTarget &&
            binding.Gesture == "Ctrl+D, P");
        Assert.DoesNotContain(loaded.Bindings, binding =>
            StringComparer.OrdinalIgnoreCase.Equals(
                binding.Gesture,
                "Ctrl+D"));
        ShortcutManager.Validate(loaded);
    }

    [Fact]
    public async Task Version1ContextMenu_MigratesCopyFullPath()
    {
        Directory.CreateDirectory(_sandbox);
        var oldConfiguration = new ContextMenuConfiguration(
            1,
            [
                new ContextMenuItemDefinition(
                    "open",
                    ContextMenuItemKind.BuiltIn,
                    "打开",
                    ContextMenuAction.Open,
                    null,
                    null,
                    true),
            ]);
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() },
        };
        await File.WriteAllTextAsync(
            Path.Combine(_sandbox, "context-menu.json"),
            JsonSerializer.Serialize(oldConfiguration, options));

        var loaded = await new JsonContextMenuStore(_sandbox).LoadAsync();

        Assert.Equal(
            ContextMenuConfiguration.CurrentSchemaVersion,
            loaded.SchemaVersion);
        Assert.Contains(
            loaded.Items,
            item => item.Action == ContextMenuAction.CopyFullPath &&
                item.IsVisible);
        Assert.Contains(
            loaded.Items,
            item => item.Action == ContextMenuAction.CreateDirectory &&
                item.Label.Contains("&F", StringComparison.Ordinal) &&
                StringComparer.Ordinal.Equals(
                    item.ParentId,
                    "new-submenu"));
        Assert.Contains(
            loaded.Items,
            item => item.Kind == ContextMenuItemKind.Submenu &&
                item.Label.Contains("&W", StringComparison.Ordinal));
        Assert.Contains(
            loaded.Items,
            item => item.Action == ContextMenuAction.OpenWith &&
                item.Label.Contains("&H", StringComparison.Ordinal));
        Assert.Contains(
            loaded.Items,
            item => item.Action == ContextMenuAction.UndoDelete);
    }

    [Fact]
    public async Task Version4ContextMenu_MigratesNestedNewMenuWithoutLosingCustomItems()
    {
        Directory.CreateDirectory(_sandbox);
        var oldConfiguration = new ContextMenuConfiguration(
            4,
            [
                new(
                    "open",
                    ContextMenuItemKind.BuiltIn,
                    "打开",
                    ContextMenuAction.Open,
                    null,
                    null,
                    true),
                new(
                    "create-directory",
                    ContextMenuItemKind.BuiltIn,
                    "新建文件夹（&W）",
                    ContextMenuAction.CreateDirectory,
                    null,
                    null,
                    true),
                new(
                    "external-user",
                    ContextMenuItemKind.ExternalProgram,
                    "我的工具",
                    null,
                    @"D:\Tools\User.exe",
                    "{path}",
                    false),
            ]);
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() },
        };
        await File.WriteAllTextAsync(
            Path.Combine(_sandbox, "context-menu.json"),
            JsonSerializer.Serialize(oldConfiguration, options));

        var loaded = await new JsonContextMenuStore(_sandbox).LoadAsync();

        Assert.Contains(
            loaded.Items,
            item => item.Kind == ContextMenuItemKind.Submenu &&
                item.Id == "new-submenu" &&
                item.Label.Contains("&W", StringComparison.Ordinal));
        Assert.Contains(
            loaded.Items,
            item => item.Action == ContextMenuAction.CreateDirectory &&
                item.ParentId == "new-submenu" &&
                item.Label.Contains("&F", StringComparison.Ordinal));
        var custom = Assert.Single(
            loaded.Items,
            item => item.Id == "external-user");
        Assert.Equal("我的工具", custom.Label);
        Assert.False(custom.IsVisible);
        Assert.Contains(
            loaded.Items,
            item => item.Action == ContextMenuAction.UndoDelete);
    }

    [Fact]
    public async Task UiPreferencesStore_RoundTripsToolbarVisibility()
    {
        var store = new JsonUiPreferencesStore(_sandbox);

        var defaults = await store.LoadAsync();
        Assert.False(defaults.IsOperationToolbarVisible);
        Assert.True(defaults.ConfirmRecycleDelete);
        Assert.False(defaults.StartWithWindows);

        await store.SaveAsync(new UiPreferences(
            UiPreferences.CurrentSchemaVersion,
            IsOperationToolbarVisible: true,
            ConfirmRecycleDelete: false,
            StartWithWindows: true,
            IsWorkspaceToolbarVisible: false,
            IsSettingsToolbarVisible: false,
            LastWorkspaceName: "work"));
        var loaded = await store.LoadAsync();

        Assert.True(loaded.IsOperationToolbarVisible);
        Assert.False(loaded.ConfirmRecycleDelete);
        Assert.True(loaded.StartWithWindows);
        Assert.False(loaded.IsWorkspaceToolbarVisible);
        Assert.False(loaded.IsSettingsToolbarVisible);
        Assert.Equal("work", loaded.LastWorkspaceName);
    }

    [Fact]
    public async Task UiPreferencesStore_AllowsConcurrentSaves()
    {
        var store = new JsonUiPreferencesStore(_sandbox);
        var saves = Enumerable.Range(0, 12)
            .Select(index => store.SaveAsync(
                UiPreferences.CreateDefault() with
                {
                    LastWorkspaceName = $"workspace-{index}",
                }));

        await Task.WhenAll(saves);

        var loaded = await store.LoadAsync();
        Assert.StartsWith("workspace-", loaded.LastWorkspaceName);
    }

    [Fact]
    public async Task Version1UiPreferences_PreservesToolbarAndAddsSafeDefaults()
    {
        Directory.CreateDirectory(_sandbox);
        await File.WriteAllTextAsync(
            Path.Combine(_sandbox, "ui-preferences.json"),
            """
            {
              "SchemaVersion": 1,
              "IsOperationToolbarVisible": true
            }
            """);

        var loaded = await new JsonUiPreferencesStore(_sandbox).LoadAsync();

        Assert.Equal(UiPreferences.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.True(loaded.IsOperationToolbarVisible);
        Assert.True(loaded.ConfirmRecycleDelete);
        Assert.False(loaded.StartWithWindows);
    }

    [Fact]
    public void PathClipboardTextBuilder_UsesSelectionOrCurrentDirectory()
    {
        var selected = new[]
        {
            new FileSystemEntry(
                @"T:\V2-包1.docx",
                "V2-包1.docx",
                EntryKind.File,
                DateTime.UnixEpoch,
                "Microsoft Word 文档",
                100),
            new FileSystemEntry(
                @"T:\素材",
                "素材",
                EntryKind.Directory,
                DateTime.UnixEpoch,
                "文件夹",
                null),
        };

        Assert.Equal(
            @"T:\V2-包1.docx" + Environment.NewLine + @"T:\素材",
            PathClipboardTextBuilder.Build(selected, @"T:\"));
        Assert.Equal(
            @"T:\",
            PathClipboardTextBuilder.Build([], @"T:\"));
    }

    [Theory]
    [InlineData("复制完整路径（&A）", "复制完整路径（_A）")]
    [InlineData("研发_资料 && 工具(&W)", "研发__资料 & 工具(_W)")]
    public void AccessKeyFormatter_ConvertsWindowsMnemonicToWpf(
        string label,
        string expected)
    {
        Assert.Equal(expected, AccessKeyFormatter.ToWpfHeader(label));
    }

    [Fact]
    public async Task ContextMenuStore_PreservesNamedSchemes()
    {
        var store = new JsonContextMenuStore(_sandbox);
        var configuration = ContextMenuDefaults.Create() with
        {
            Items =
            [
                new ContextMenuItemDefinition(
                    "copy-full-path",
                    ContextMenuItemKind.BuiltIn,
                    "复制完整路径（&A）",
                    ContextMenuAction.CopyFullPath,
                    null,
                    null,
                    true),
            ],
        };

        await store.SaveSchemeAsync("标书菜单", configuration);
        var names = await store.ListSchemeNamesAsync();
        var loaded = await store.LoadSchemeAsync("标书菜单");

        Assert.Contains("标书菜单", names);
        Assert.NotNull(loaded);
        Assert.Equal(
            "复制完整路径（&A）",
            Assert.Single(loaded.Items).Label);
    }

    [Fact]
    public async Task TabContextMenuStore_PreservesOrderVisibilityAndSchemes()
    {
        var store = new JsonTabContextMenuStore(_sandbox);
        var defaults = TabContextMenuDefaults.Create();
        var reordered = defaults with
        {
            Items = defaults.Items
                .Reverse()
                .Select((item, index) => index == 0
                    ? item with { IsVisible = false }
                    : item)
                .ToArray(),
        };

        await store.SaveAsync(reordered);
        await store.SaveSchemeAsync("标签方案", reordered);
        var loaded = await store.LoadAsync();
        var names = await store.ListSchemeNamesAsync();
        var scheme = await store.LoadSchemeAsync("标签方案");

        Assert.Equal(reordered.Items, loaded.Items);
        Assert.Contains("标签方案", names);
        Assert.NotNull(scheme);
        Assert.Equal(reordered.Items, scheme.Items);
        Assert.False(loaded.Items[0].IsVisible);
    }

    public void Dispose()
    {
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        var resolved = Path.GetFullPath(_sandbox);
        Assert.StartsWith(tempRoot, resolved, StringComparison.OrdinalIgnoreCase);
        if (Directory.Exists(resolved))
        {
            Directory.Delete(resolved, recursive: true);
        }
    }
}

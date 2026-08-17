using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using MYTC.Application.Shell;
using MYTC.Application.Updates;

namespace MYTC.Infrastructure.Shell;

public sealed class ShellIntegrationService
{
    private const int BackupSchemaVersion = 1;
    private const string DirectoryShellKey =
        @"Software\Classes\Directory\shell";
    private const string DriveShellKey =
        @"Software\Classes\Drive\shell";
    private const string FolderShellKey =
        @"Software\Classes\Folder\shell";
    private const string DirectoryBackgroundShellKey =
        @"Software\Classes\Directory\Background\shell";
    private const string RunKey =
        @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ManagedValueName = "MYTC.Managed";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _backupPath;

    public ShellIntegrationService(string? backupPath = null)
    {
        _backupPath = backupPath ?? Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "MYTC",
            "shell",
            "registration-backup.json");
    }

    public ShellIntegrationStatus GetStatus(
        string executablePath,
        string maintenancePath)
    {
        var fullExecutable = Path.GetFullPath(executablePath);
        var fullMaintenance = Path.GetFullPath(maintenancePath);
        var command = GetStringValue(
            $@"{DirectoryShellKey}\{ShellIntegrationConstants.VerbName}\command",
            string.Empty);
        var folderDefault = StringComparer.Ordinal.Equals(
                GetStringValue(DirectoryShellKey, string.Empty),
                ShellIntegrationConstants.VerbName) &&
            StringComparer.OrdinalIgnoreCase.Equals(
                command,
                BuildOpenCommand(fullExecutable, "%1"));
        var bridgeCommand = GetStringValue(
            RunKey,
            ShellIntegrationConstants.BridgeRunValueName);
        var bridgeEnabled = StringComparer.OrdinalIgnoreCase.Equals(
            bridgeCommand,
            BuildBridgeCommand(fullMaintenance));

        var registeredPath = ParseExecutableFromCommand(command);
        var description = folderDefault && bridgeEnabled
            ? "检测到旧版文件夹接管；请启动 openTC 或点击“启用 Win+E 启动 openTC”完成迁移。"
            : folderDefault
                ? "检测到旧版文件夹接管；Win+E 桥接未启用。"
                : bridgeEnabled
                    ? "Win+E 会启动 openTC；资源管理器中的文件夹仍由 Windows 资源管理器打开。"
                    : "Windows 资源管理器保持默认；Win+E 未配置为启动 openTC。";
        return new ShellIntegrationStatus(
            folderDefault,
            bridgeEnabled,
            registeredPath,
            description);
    }

    public void Register(
        string executablePath,
        string maintenancePath)
    {
        var fullExecutable = Path.GetFullPath(executablePath);
        var fullMaintenance = Path.GetFullPath(maintenancePath);
        var installRoot = Path.GetDirectoryName(fullExecutable)
            ?? throw new InvalidOperationException("无法识别 openTC 程序目录。");

        if (!InstallationPathPolicy.IsSupportedFixedLocalPath(
                installRoot,
                out var reason))
        {
            throw new InvalidOperationException(reason);
        }

        if (!File.Exists(fullExecutable))
        {
            throw new FileNotFoundException(
                "找不到 openTC 主程序（兼容文件名 MYTC.exe）。",
                fullExecutable);
        }

        if (!File.Exists(fullMaintenance))
        {
            throw new FileNotFoundException(
                "找不到 openTC 维护工具（兼容文件名 MYTC.Maintenance.exe）。",
                fullMaintenance);
        }

        EnsureVerbKeyIsOursOrAbsent(DirectoryShellKey);
        EnsureVerbKeyIsOursOrAbsent(DriveShellKey);
        EnsureVerbKeyIsOursOrAbsent(FolderShellKey);
        EnsureVerbKeyIsOursOrAbsent(DirectoryBackgroundShellKey);

        ShellRegistrationBackup backup;
        var createdBackup = false;
        if (File.Exists(_backupPath))
        {
            backup = ReadBackup();
        }
        else
        {
            backup = CaptureBackup(fullExecutable, fullMaintenance);
            WriteBackupAtomically(backup);
            createdBackup = true;
        }

        try
        {
            RestoreLegacyFolderDefaults(backup.Values);
            WriteVerb(
                DirectoryShellKey,
                fullExecutable,
                "%1");
            WriteVerb(
                DriveShellKey,
                fullExecutable,
                "%1");
            WriteVerb(
                FolderShellKey,
                fullExecutable,
                "%1");
            WriteVerb(
                DirectoryBackgroundShellKey,
                fullExecutable,
                "%V");

            SetStringValue(
                RunKey,
                ShellIntegrationConstants.BridgeRunValueName,
                BuildBridgeCommand(fullMaintenance));
            NotifyShellAssociationChanged();
        }
        catch
        {
            RestoreSnapshots(backup.Values);
            DeleteManagedVerbKeys();
            if (createdBackup)
            {
                TryDeleteBackup();
            }

            NotifyShellAssociationChanged();
            throw;
        }

        StartBridge(fullMaintenance);
    }

    public bool MigrateLegacyFolderAssociationToWinEOnly()
    {
        if (!IsFolderDefaultOwnedByMyTc())
        {
            return false;
        }

        var backup = File.Exists(_backupPath)
            ? ReadBackup()
            : null;
        RestoreLegacyFolderDefaults(backup?.Values ?? []);
        NotifyShellAssociationChanged();
        return true;
    }

    public void Restore()
    {
        ShellRegistrationBackup? backup = null;
        if (File.Exists(_backupPath))
        {
            backup = ReadBackup();
        }

        DeleteManagedVerbKeys();
        if (backup is not null)
        {
            RestoreSnapshots(backup.Values);
            RemoveValueIfEqual(
                DirectoryShellKey,
                string.Empty,
                ShellIntegrationConstants.VerbName);
            RemoveValueIfEqual(
                DriveShellKey,
                string.Empty,
                ShellIntegrationConstants.VerbName);
            TryDeleteBackup();
        }
        else
        {
            RemoveValueIfEqual(
                DirectoryShellKey,
                string.Empty,
                ShellIntegrationConstants.VerbName);
            RemoveValueIfEqual(
                DriveShellKey,
                string.Empty,
                ShellIntegrationConstants.VerbName);
            RemoveBridgeRunValueIfOwned();
        }

        NotifyShellAssociationChanged();
    }

    private ShellRegistrationBackup CaptureBackup(
        string executablePath,
        string maintenancePath)
    {
        return new ShellRegistrationBackup(
            BackupSchemaVersion,
            DateTimeOffset.UtcNow,
            executablePath,
            maintenancePath,
            [
                CaptureValue(DirectoryShellKey, string.Empty),
                CaptureValue(DriveShellKey, string.Empty),
                CaptureValue(
                    RunKey,
                    ShellIntegrationConstants.BridgeRunValueName),
            ]);
    }

    private void WriteBackupAtomically(ShellRegistrationBackup backup)
    {
        var parent = Path.GetDirectoryName(_backupPath)
            ?? throw new IOException("注册表备份目录无效。");
        Directory.CreateDirectory(parent);
        var temporary = Path.Combine(
            parent,
            $".{Path.GetFileName(_backupPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(backup, JsonOptions));
            File.Move(temporary, _backupPath);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private ShellRegistrationBackup ReadBackup()
    {
        var backup = JsonSerializer.Deserialize<ShellRegistrationBackup>(
            File.ReadAllText(_backupPath),
            JsonOptions);
        if (backup is null ||
            backup.SchemaVersion != BackupSchemaVersion)
        {
            throw new InvalidDataException(
                "Windows 接管备份文件损坏或版本不受支持；为避免破坏原设置，已停止操作。");
        }

        return backup;
    }

    private static RegistryValueSnapshot CaptureValue(
        string keyPath,
        string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        if (key is null ||
            !key.GetValueNames().Contains(
                valueName,
                StringComparer.OrdinalIgnoreCase))
        {
            return new RegistryValueSnapshot(
                keyPath,
                valueName,
                false,
                null,
                null,
                null,
                null,
                null);
        }

        var kind = key.GetValueKind(valueName);
        var value = key.GetValue(
            valueName,
            null,
            RegistryValueOptions.DoNotExpandEnvironmentNames);
        return kind switch
        {
            RegistryValueKind.String or RegistryValueKind.ExpandString =>
                new RegistryValueSnapshot(
                    keyPath,
                    valueName,
                    true,
                    kind,
                    value as string,
                    null,
                    null,
                    null),
            RegistryValueKind.MultiString =>
                new RegistryValueSnapshot(
                    keyPath,
                    valueName,
                    true,
                    kind,
                    null,
                    value as string[],
                    null,
                    null),
            RegistryValueKind.DWord =>
                new RegistryValueSnapshot(
                    keyPath,
                    valueName,
                    true,
                    kind,
                    null,
                    null,
                    Convert.ToInt64(value),
                    null),
            RegistryValueKind.QWord =>
                new RegistryValueSnapshot(
                    keyPath,
                    valueName,
                    true,
                    kind,
                    null,
                    null,
                    Convert.ToInt64(value),
                    null),
            RegistryValueKind.Binary =>
                new RegistryValueSnapshot(
                    keyPath,
                    valueName,
                    true,
                    kind,
                    null,
                    null,
                    null,
                    Convert.ToBase64String((byte[]?)value ?? [])),
            _ => throw new NotSupportedException(
                $"暂不支持备份注册表值类型：{kind}。"),
        };
    }

    private static void RestoreSnapshots(
        IEnumerable<RegistryValueSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            using var key = Registry.CurrentUser.CreateSubKey(snapshot.KeyPath);
            if (!snapshot.Existed)
            {
                key.DeleteValue(
                    snapshot.ValueName,
                    throwOnMissingValue: false);
                continue;
            }

            var kind = snapshot.Kind
                ?? throw new InvalidDataException("注册表备份缺少值类型。");
            object value = kind switch
            {
                RegistryValueKind.String or RegistryValueKind.ExpandString =>
                    snapshot.StringValue ?? string.Empty,
                RegistryValueKind.MultiString =>
                    snapshot.StringArrayValue ?? [],
                RegistryValueKind.DWord =>
                    checked((int)(snapshot.NumericValue ?? 0)),
                RegistryValueKind.QWord =>
                    snapshot.NumericValue ?? 0,
                RegistryValueKind.Binary =>
                    Convert.FromBase64String(
                        snapshot.BinaryBase64Value ?? string.Empty),
                _ => throw new NotSupportedException(
                    $"暂不支持恢复注册表值类型：{kind}。"),
            };
            key.SetValue(snapshot.ValueName, value, kind);
        }
    }

    private static void RestoreLegacyFolderDefaults(
        IEnumerable<RegistryValueSnapshot> snapshots)
    {
        var defaults = snapshots.Where(snapshot =>
            StringComparer.Ordinal.Equals(snapshot.ValueName, string.Empty) &&
            (StringComparer.OrdinalIgnoreCase.Equals(
                snapshot.KeyPath,
                DirectoryShellKey) ||
             StringComparer.OrdinalIgnoreCase.Equals(
                snapshot.KeyPath,
                DriveShellKey)));
        RestoreSnapshots(defaults);
        RemoveValueIfEqual(
            DirectoryShellKey,
            string.Empty,
            ShellIntegrationConstants.VerbName);
        RemoveValueIfEqual(
            DriveShellKey,
            string.Empty,
            ShellIntegrationConstants.VerbName);
    }

    private static bool IsFolderDefaultOwnedByMyTc()
    {
        return StringComparer.Ordinal.Equals(
                   GetStringValue(DirectoryShellKey, string.Empty),
                   ShellIntegrationConstants.VerbName) ||
               StringComparer.Ordinal.Equals(
                   GetStringValue(DriveShellKey, string.Empty),
                   ShellIntegrationConstants.VerbName);
    }

    private static void WriteVerb(
        string shellKey,
        string executablePath,
        string placeholder)
    {
        var verbPath =
            $@"{shellKey}\{ShellIntegrationConstants.VerbName}";
        using (var verb = Registry.CurrentUser.CreateSubKey(verbPath))
        {
            verb.SetValue(
                string.Empty,
                "在 openTC 中打开",
                RegistryValueKind.String);
            verb.SetValue(
                "Icon",
                executablePath,
                RegistryValueKind.String);
            verb.SetValue(
                ManagedValueName,
                1,
                RegistryValueKind.DWord);
        }

        using var command = Registry.CurrentUser.CreateSubKey(
            $@"{verbPath}\command");
        command.SetValue(
            string.Empty,
            BuildOpenCommand(executablePath, placeholder),
            RegistryValueKind.String);
    }

    private static void EnsureVerbKeyIsOursOrAbsent(string shellKey)
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            $@"{shellKey}\{ShellIntegrationConstants.VerbName}");
        if (key is not null &&
            !Equals(key.GetValue(ManagedValueName), 1))
        {
            throw new InvalidOperationException(
                $"注册表中已存在非 openTC 管理的 {ShellIntegrationConstants.VerbName} 项；为避免覆盖，已停止注册。");
        }
    }

    private static void DeleteManagedVerbKeys()
    {
        foreach (var shellKey in new[]
                 {
                     DirectoryShellKey,
                     DriveShellKey,
                     FolderShellKey,
                     DirectoryBackgroundShellKey,
                 })
        {
            using var parent = Registry.CurrentUser.OpenSubKey(
                shellKey,
                writable: true);
            using var existing = parent?.OpenSubKey(
                ShellIntegrationConstants.VerbName);
            if (existing is null ||
                !Equals(existing.GetValue(ManagedValueName), 1))
            {
                continue;
            }

            existing.Close();
            parent?.DeleteSubKeyTree(
                ShellIntegrationConstants.VerbName,
                throwOnMissingSubKey: false);
        }
    }

    private static void SetStringValue(
        string keyPath,
        string valueName,
        string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        key.SetValue(valueName, value, RegistryValueKind.String);
    }

    private static string GetStringValue(
        string keyPath,
        string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(
            valueName,
            string.Empty,
            RegistryValueOptions.DoNotExpandEnvironmentNames) as string
            ?? string.Empty;
    }

    private static void RemoveValueIfEqual(
        string keyPath,
        string valueName,
        string expected)
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            keyPath,
            writable: true);
        if (StringComparer.Ordinal.Equals(
                key?.GetValue(valueName) as string,
                expected))
        {
            key?.DeleteValue(valueName, throwOnMissingValue: false);
        }
    }

    private static void RemoveBridgeRunValueIfOwned()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            RunKey,
            writable: true);
        var value = key?.GetValue(
            ShellIntegrationConstants.BridgeRunValueName) as string;
        if (value?.Contains(
                "--bridge",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            key?.DeleteValue(
                ShellIntegrationConstants.BridgeRunValueName,
                throwOnMissingValue: false);
        }
    }

    private static string BuildOpenCommand(
        string executablePath,
        string placeholder)
    {
        return $"\"{executablePath}\" --open \"{placeholder}\"";
    }

    private static string BuildBridgeCommand(string maintenancePath)
    {
        return $"\"{maintenancePath}\" --bridge";
    }

    private static string ParseExecutableFromCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return string.Empty;
        }

        if (command[0] == '"')
        {
            var closingQuote = command.IndexOf('"', 1);
            return closingQuote > 1
                ? command[1..closingQuote]
                : string.Empty;
        }

        var separator = command.IndexOf(' ');
        return separator > 0 ? command[..separator] : command;
    }

    private static void StartBridge(string maintenancePath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = maintenancePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "--bridge" },
        });
    }

    private void TryDeleteBackup()
    {
        try
        {
            File.Delete(_backupPath);
        }
        catch
        {
            // Registration is already restored; a stale backup is harmless.
        }
    }

    private static void NotifyShellAssociationChanged()
    {
        SHChangeNotify(
            0x08000000,
            0,
            IntPtr.Zero,
            IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(
        uint wEventId,
        uint uFlags,
        IntPtr dwItem1,
        IntPtr dwItem2);

    private sealed record ShellRegistrationBackup(
        int SchemaVersion,
        DateTimeOffset RegisteredAtUtc,
        string ExecutablePath,
        string MaintenancePath,
        IReadOnlyList<RegistryValueSnapshot> Values);

    private sealed record RegistryValueSnapshot(
        string KeyPath,
        string ValueName,
        bool Existed,
        RegistryValueKind? Kind,
        string? StringValue,
        string[]? StringArrayValue,
        long? NumericValue,
        string? BinaryBase64Value);
}

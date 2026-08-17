using MYTC.Infrastructure.Shell;

namespace MYTC.Maintenance;

internal sealed class MaintenanceForm : Form
{
    private readonly string _installRoot = AppContext.BaseDirectory;
    private readonly string _mytcPath;
    private readonly string _maintenancePath;
    private readonly ShellIntegrationService _shellIntegration = new();
    private readonly Label _statusLabel;
    private readonly Button _registerButton;
    private readonly Button _restoreButton;

    public MaintenanceForm()
    {
        _mytcPath = Path.Combine(_installRoot, "MYTC.exe");
        _maintenancePath = Environment.ProcessPath
            ?? Path.Combine(_installRoot, "MYTC.Maintenance.exe");

        Text = "配置 Win+E 启动 openTC";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(650, 330);
        MinimumSize = new Size(650, 330);
        MaximizeBox = false;
        Font = new Font("Microsoft YaHei UI", 9F);

        var title = new Label
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Location = new Point(24, 22),
            Text = "配置 Win+E 启动 openTC",
        };
        var explanation = new Label
        {
            AutoSize = false,
            Location = new Point(24, 58),
            Size = new Size(600, 76),
            Text =
                "此工具只为当前 Windows 用户配置轻量 Win+E 桥接程序。" +
                "资源管理器中的文件夹双击和 Enter 始终保留给 Windows 资源管理器。" +
                "它不会替换 explorer.exe，也不会接管桌面、任务栏或登录外壳。\r\n\r\n" +
                "程序应先放在固定的本机磁盘目录，例如 E:\\port\\openTC；注册后不要移动该目录。",
        };
        var pathLabel = new Label
        {
            AutoSize = false,
            Location = new Point(24, 143),
            Size = new Size(600, 40),
            Text = $"当前程序：{_mytcPath}",
        };
        _statusLabel = new Label
        {
            AutoSize = false,
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(24, 188),
            Padding = new Padding(8),
            Size = new Size(600, 45),
        };
        _registerButton = new Button
        {
            Location = new Point(24, 252),
            Size = new Size(245, 42),
            Text = "启用 Win+E 启动 openTC",
        };
        _restoreButton = new Button
        {
            Location = new Point(285, 252),
            Size = new Size(210, 42),
            Text = "停用 Win+E 桥接",
        };
        var closeButton = new Button
        {
            Location = new Point(511, 252),
            Size = new Size(113, 42),
            Text = "关闭",
        };

        _registerButton.Click += (_, _) => Register();
        _restoreButton.Click += (_, _) => Restore();
        closeButton.Click += (_, _) => Close();
        Controls.AddRange(
        [
            title,
            explanation,
            pathLabel,
            _statusLabel,
            _registerButton,
            _restoreButton,
            closeButton,
        ]);
        Shown += (_, _) => RefreshStatus();
    }

    private void Register()
    {
        var confirmation = MessageBox.Show(
            this,
            "确认让 Win+E 启动 openTC 吗？\n\n" +
            "资源管理器中的文件夹双击和 Enter 不会受影响，仍由 Windows 资源管理器打开。",
            "确认启用 Win+E",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (confirmation != DialogResult.Yes)
        {
            return;
        }

        RunAction(
            () => _shellIntegration.Register(
                _mytcPath,
                _maintenancePath),
            "配置完成。Win+E 将启动 openTC；资源管理器内的文件夹仍由 Windows 资源管理器打开。");
    }

    private void Restore()
    {
        var confirmation = MessageBox.Show(
            this,
            "确认停用 Win+E 启动 openTC 吗？\n\n" +
            "资源管理器中的文件夹打开方式不会被修改。",
            "确认停用 Win+E",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (confirmation != DialogResult.Yes)
        {
            return;
        }

        RunAction(
            () =>
            {
                WinEBridge.SignalExit();
                _shellIntegration.Restore();
            },
            "已停用 Win+E 桥接；Win+E 将恢复由 Windows 资源管理器处理。");
    }

    private void RunAction(Action action, string successMessage)
    {
        try
        {
            Enabled = false;
            action();
            MessageBox.Show(
                this,
                successMessage,
                "openTC 维护工具",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "操作失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            Enabled = true;
            RefreshStatus();
        }
    }

    private void RefreshStatus()
    {
        try
        {
            var status = _shellIntegration.GetStatus(
                _mytcPath,
                _maintenancePath);
            _statusLabel.Text = $"当前状态：{status.Description}";
            _registerButton.Enabled =
                status.IsFolderDefault || !status.IsWinEBridgeEnabled;
            _restoreButton.Enabled =
                status.IsFolderDefault || status.IsWinEBridgeEnabled;
        }
        catch (Exception exception)
        {
            _statusLabel.Text = $"无法读取状态：{exception.Message}";
            _registerButton.Enabled = true;
            _restoreButton.Enabled = true;
        }
    }
}

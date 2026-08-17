using System.Windows.Forms;

namespace MYTC.Maintenance;

internal static class Program
{
    [STAThread]
    private static int Main(string[] arguments)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            if (arguments.Contains(
                    "--bridge",
                    StringComparer.OrdinalIgnoreCase))
            {
                return WinEBridge.Run();
            }

            if (arguments.Contains(
                    "--bridge-exit",
                    StringComparer.OrdinalIgnoreCase))
            {
                WinEBridge.SignalExit();
                return 0;
            }

            if (arguments.Contains(
                    "--apply-update",
                    StringComparer.OrdinalIgnoreCase))
            {
                return UpdaterMode.RunAsync(arguments)
                    .GetAwaiter()
                    .GetResult();
            }

            if (arguments.Contains(
                    "--cleanup-updater",
                    StringComparer.OrdinalIgnoreCase))
            {
                return CleanupMode.RunAsync(arguments)
                    .GetAwaiter()
                    .GetResult();
            }

            System.Windows.Forms.Application.Run(new MaintenanceForm());
            return 0;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"openTC 维护工具发生错误：\n\n{exception.Message}",
                "openTC 维护工具",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }
}

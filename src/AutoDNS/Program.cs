using System.Diagnostics;

namespace AutoDNS;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (HasFlag(args, "--install"))
        {
            try
            {
                TaskSchedulerService.EnsureTasks(AppConstants.ExePath);
                var installSettings = AppSettings.Load();
                TaskSchedulerService.SetLogonEnabled(installSettings.AutoStart);
            }
            catch
            {
                Environment.ExitCode = 1;
            }

            return;
        }

        if (HasFlag(args, "--uninstall"))
        {
            TrySignalShutdown();
            Thread.Sleep(800);
            WindowsDns.RestoreAutomatic();
            TaskSchedulerService.DeleteTasks();
            AppCleanup.RemoveAppData();
            AppCleanup.RemoveShortcuts();
            return;
        }

        if (!PrivilegeHelper.IsAdministrator())
        {
            if (TaskSchedulerService.TryRunElevatedInstance())
                return;

            TryRelaunchElevated();
            return;
        }

        using var mutex = new Mutex(true, AppConstants.MutexName, out var created);
        if (!created)
            return;

        var settings = AppSettings.Load();
        try
        {
            TaskSchedulerService.EnsureTasks(AppConstants.ExePath);
            TaskSchedulerService.SetLogonEnabled(settings.AutoStart);
        }
        catch
        {
            // Tasks are also created by the installer; continue if COM registration fails.
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayAppContext(settings));
    }

    private static bool HasFlag(string[] args, string flag) =>
        args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    private static void TryRelaunchElevated()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = AppConstants.ExePath,
                UseShellExecute = true,
                Verb = "runas"
            });
        }
        catch
        {
            // User cancelled UAC.
        }
    }

    private static void TrySignalShutdown()
    {
        try
        {
            using var shutdown = new EventWaitHandle(
                false,
                EventResetMode.AutoReset,
                AppConstants.ShutdownEventName);
            shutdown.Set();
        }
        catch
        {
            // No running instance.
        }
    }
}

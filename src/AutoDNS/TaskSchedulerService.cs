using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AutoDNS;

internal static class TaskSchedulerService
{
    private const int TaskLogonInteractiveToken = 3;
    private const int TaskRunlevelHighest = 1;
    private const int TaskCreateOrUpdate = 6;
    private const int TaskTriggerLogon = 9;
    private const int TaskActionExec = 0;
    private const int TaskInstancesIgnoreNew = 2;

    public static void EnsureTasks(string exePath)
    {
        var service = Connect();
        var folder = service.GetFolder("\\");
        TryDelete(folder, AppConstants.LegacyLogonTaskName);
        TryDelete(folder, AppConstants.LegacyRunTaskName);
        RegisterTask(service, folder, AppConstants.LogonTaskName, exePath, withLogonTrigger: true);
        RegisterTask(service, folder, AppConstants.RunTaskName, exePath, withLogonTrigger: false);
    }

    public static void SetLogonEnabled(bool enabled)
    {
        try
        {
            var service = Connect();
            var folder = service.GetFolder("\\");
            var task = folder.GetTask(AppConstants.LogonTaskName);
            task.Enabled = enabled;
        }
        catch (COMException)
        {
            // Task may not exist yet during first-run setup.
        }
    }

    public static bool IsLogonEnabled()
    {
        try
        {
            var service = Connect();
            var folder = service.GetFolder("\\");
            var task = folder.GetTask(AppConstants.LogonTaskName);
            return task.Enabled;
        }
        catch
        {
            return false;
        }
    }

    public static bool TaskExists(string name)
    {
        try
        {
            var service = Connect();
            var folder = service.GetFolder("\\");
            folder.GetTask(name);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryRunElevatedInstance()
    {
        if (!TaskExists(AppConstants.RunTaskName))
            return false;

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Run /TN \"{AppConstants.RunTaskName}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
            process?.WaitForExit(15_000);
            return process is { ExitCode: 0 };
        }
        catch
        {
            return false;
        }
    }

    public static void DeleteTasks()
    {
        try
        {
            var service = Connect();
            var folder = service.GetFolder("\\");
            TryDelete(folder, AppConstants.LogonTaskName);
            TryDelete(folder, AppConstants.RunTaskName);
            TryDelete(folder, AppConstants.LegacyLogonTaskName);
            TryDelete(folder, AppConstants.LegacyRunTaskName);
        }
        catch
        {
            // Best-effort cleanup on uninstall.
        }
    }

    private static void TryDelete(dynamic folder, string name)
    {
        try
        {
            folder.DeleteTask(name, 0);
        }
        catch
        {
            // Already removed.
        }
    }

    private static void RegisterTask(dynamic service, dynamic folder, string name, string exePath, bool withLogonTrigger)
    {
        dynamic definition = service.NewTask(0);
        definition.RegistrationInfo.Description = withLogonTrigger
            ? "Автозапуск AutoDNS и проверка системного DNS при входе в Windows"
            : "Запуск AutoDNS с правами администратора без UAC";
        definition.Principal.RunLevel = TaskRunlevelHighest;
        definition.Principal.LogonType = TaskLogonInteractiveToken;
        definition.Principal.UserId = Environment.UserDomainName + "\\" + Environment.UserName;

        definition.Settings.Enabled = true;
        definition.Settings.AllowDemandStart = true;
        definition.Settings.DisallowStartIfOnBatteries = false;
        definition.Settings.StopIfGoingOnBatteries = false;
        definition.Settings.StartWhenAvailable = true;
        definition.Settings.ExecutionTimeLimit = "PT0S";
        definition.Settings.MultipleInstances = TaskInstancesIgnoreNew;
        definition.Settings.Hidden = false;
        definition.Settings.AllowHardTerminate = true;
            try
            {
                definition.Settings.IdleSettings.StopOnIdleEnd = false;
            }
            catch
            {
                // Optional setting; some hosts reject IdleSettings updates.
            }

        if (withLogonTrigger)
            definition.Triggers.Create(TaskTriggerLogon);

        dynamic action = definition.Actions.Create(TaskActionExec);
        action.Path = exePath;
        action.WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty;

        folder.RegisterTaskDefinition(
            name,
            definition,
            TaskCreateOrUpdate,
            null,
            null,
            TaskLogonInteractiveToken);
    }

    private static dynamic Connect()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new InvalidOperationException("Планировщик заданий недоступен.");
        dynamic service = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("Не удалось подключиться к планировщику заданий.");
        service.Connect();
        return service;
    }
}

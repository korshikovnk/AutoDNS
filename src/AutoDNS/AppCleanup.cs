using System.Diagnostics;
using Microsoft.Win32;

namespace AutoDNS;

internal static class AppCleanup
{
    public static void RemoveAppData()
    {
        try
        {
            if (Directory.Exists(AppConstants.DataDirectory))
                Directory.Delete(AppConstants.DataDirectory, recursive: true);
            if (Directory.Exists(AppConstants.LegacyDataDirectory))
                Directory.Delete(AppConstants.LegacyDataDirectory, recursive: true);
        }
        catch
        {
            // Best-effort: leftover files are removed by the delayed delete if needed.
        }
    }

    public static void RemoveShortcuts()
    {
        foreach (var folder in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                     Environment.GetFolderPath(Environment.SpecialFolder.Programs)
                 })
        {
            TryDeleteFile(Path.Combine(folder, AppConstants.ProductName + ".lnk"));
            TryDeleteFile(Path.Combine(folder, AppConstants.LegacyProductName + ".lnk"));
        }
    }

    public static void RemoveUninstallRegistry()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                Path.GetDirectoryName(AppConstants.UninstallRegistryKey)!,
                writable: true);
            key?.DeleteSubKeyTree(
                Path.GetFileName(AppConstants.UninstallRegistryKey),
                throwOnMissingSubKey: false);
        }
        catch
        {
            // Registry key may already be gone.
        }
    }

    public static void StartSelfRemoval()
    {
        var exeDir = Path.GetDirectoryName(AppConstants.ExePath);
        if (string.IsNullOrWhiteSpace(exeDir))
            return;

        var uninstaller = Directory.EnumerateFiles(exeDir, "unins*.exe")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string arguments;
        if (uninstaller is not null)
        {
            arguments =
                $"/c timeout /t 2 /nobreak >nul & \"{uninstaller}\" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART";
        }
        else
        {
            arguments = $"/c timeout /t 2 /nobreak >nul & rmdir /s /q \"{exeDir}\"";
            RemoveUninstallRegistry();
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = windows
        });
    }

    public static void UninstallAll()
    {
        WindowsDns.RestoreAutomatic();
        TaskSchedulerService.DeleteTasks();
        RemoveAppData();
        RemoveShortcuts();
        StartSelfRemoval();
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Ignore missing or locked shortcuts.
        }
    }
}

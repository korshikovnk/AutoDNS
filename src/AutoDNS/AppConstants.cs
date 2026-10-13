namespace AutoDNS;

internal static class AppConstants
{
    public const string ProductName = "AutoDNS";
    public const string DefaultProviderId = "malw";
    public const string AutomaticProviderId = "auto";
    public const string AutomaticProviderName = "Авто";

    public const string LogonTaskName = "AutoDNS";
    public const string RunTaskName = "AutoDNS.Run";

    public const string MutexName = @"Global\AutoDNS.SingleInstance";
    public const string ShutdownEventName = @"Global\AutoDNS.Shutdown";

    public const string LegacyProductName = "AutoHosts";
    public const string LegacyLogonTaskName = "AutoHosts";
    public const string LegacyRunTaskName = "AutoHosts.Run";

    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), ProductName);

    public static string LegacyDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), LegacyProductName);

    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public static string LegacySettingsPath => Path.Combine(LegacyDataDirectory, "settings.json");

    public static string ExePath =>
        Environment.ProcessPath
        ?? Application.ExecutablePath;

    public const string UninstallRegistryKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{A7C4E91B-2D18-4F6A-9C33-8B0E5D1A7F24}_is1";

    public static readonly Color ActiveGreen = Color.FromArgb(0x21, 0xA3, 0x66);
}

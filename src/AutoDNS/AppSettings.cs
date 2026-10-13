using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoDNS;

internal enum AutoUpdateInterval
{
    Off,
    Hour,
    Day,
    Week,
    Month
}

internal sealed class AppSettings
{
    public string ProviderId { get; set; } = AppConstants.DefaultProviderId;

    public List<string> CachedIpv4 { get; set; } = [];

    public List<string> CachedIpv6 { get; set; } = [];

    public bool HasCompletedFirstRun { get; set; }

    public AutoUpdateInterval AutoUpdate { get; set; } = AutoUpdateInterval.Day;

    public bool AutoStart { get; set; } = true;

    public DateTimeOffset? LastUpdateUtc { get; set; }

    public bool DnsEnabled { get; set; } = true;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppSettings Load()
    {
        try
        {
            var path = AppConstants.SettingsPath;
            if (!File.Exists(path) && File.Exists(AppConstants.LegacySettingsPath))
            {
                Directory.CreateDirectory(AppConstants.DataDirectory);
                File.Copy(AppConstants.LegacySettingsPath, path, overwrite: false);
            }

            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                if (!settings.DnsEnabled)
                    settings.ProviderId = AppConstants.AutomaticProviderId;
                else if (string.IsNullOrWhiteSpace(settings.ProviderId)
                    || (!IsAutomatic(settings.ProviderId) && DnsCatalog.Find(settings.ProviderId) is null))
                    settings.ProviderId = AppConstants.DefaultProviderId;
                settings.DnsEnabled = !IsAutomatic(settings.ProviderId);
                return settings;
            }
        }
        catch
        {
            // Keep defaults if the file is damaged.
        }

        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(AppConstants.DataDirectory);
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(AppConstants.SettingsPath, json);
    }

    public TimeSpan? GetUpdateInterval() => AutoUpdate switch
    {
        AutoUpdateInterval.Hour => TimeSpan.FromHours(1),
        AutoUpdateInterval.Day => TimeSpan.FromDays(1),
        AutoUpdateInterval.Week => TimeSpan.FromDays(7),
        AutoUpdateInterval.Month => TimeSpan.FromDays(30),
        _ => null
    };

    public bool IsUpdateDue()
        => IsUpdateDue(LastUpdateUtc, DateTimeOffset.UtcNow);

    internal bool IsUpdateDue(DateTimeOffset? lastUpdateUtc, DateTimeOffset nowUtc)
    {
        var interval = GetUpdateInterval();
        if (interval is null)
            return false;

        if (lastUpdateUtc is null)
            return true;

        return nowUtc - lastUpdateUtc.Value >= interval.Value;
    }

    public static bool IsAutomatic(string? providerId) =>
        string.Equals(providerId, AppConstants.AutomaticProviderId, StringComparison.OrdinalIgnoreCase);
}

using Microsoft.Win32;

namespace AutoDNS;

internal static class BrowserDns
{
    private const string ChromePolicy = @"SOFTWARE\Policies\Google\Chrome";
    private const string EdgePolicy = @"SOFTWARE\Policies\Microsoft\Edge";
    private const string QuicValue = "QuicAllowed";
    private const string DohModeValue = "DnsOverHttpsMode";
    private const string DohTemplatesValue = "DnsOverHttpsTemplates";

    public static void Apply(string dohTemplate)
    {
        var template = NormalizeTemplate(dohTemplate);
        Write(ChromePolicy, template);
        Write(EdgePolicy, template);
    }

    public static void Restore()
    {
        Clear(ChromePolicy);
        Clear(EdgePolicy);
    }

    internal static string NormalizeTemplate(string dohTemplate)
    {
        var template = dohTemplate.Trim();
        if (!template.Contains("{?dns}", StringComparison.OrdinalIgnoreCase)
            && !template.Contains("{?dns", StringComparison.OrdinalIgnoreCase))
            template += "{?dns}";
        return template;
    }

    private static void Write(string keyPath, string template)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(keyPath, writable: true);
            if (key is null)
                return;

            key.DeleteValue(QuicValue, throwOnMissingValue: false);
            key.SetValue(DohModeValue, "secure", RegistryValueKind.String);
            key.SetValue(DohTemplatesValue, template, RegistryValueKind.String);
        }
        catch
        {
            // Policy write is best-effort.
        }
    }

    private static void Clear(string keyPath)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath, writable: true);
            if (key is null)
                return;

            key.DeleteValue(QuicValue, throwOnMissingValue: false);
            key.DeleteValue(DohModeValue, throwOnMissingValue: false);
            key.DeleteValue(DohTemplatesValue, throwOnMissingValue: false);
        }
        catch
        {
            // Already absent.
        }
    }
}

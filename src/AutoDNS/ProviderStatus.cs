using System.Text.RegularExpressions;

namespace AutoDNS;

internal sealed class ProviderUnavailableException(string message) : InvalidOperationException(message);

internal static class ProviderStatus
{
    public static void EnsureAvailable(DnsProvider provider, string html)
    {
        if (!string.Equals(provider.Id, "xbox", StringComparison.OrdinalIgnoreCase))
            return;

        // Read the provider's status banner, rather than matching quoted text in its FAQ.
        var status = Regex.Match(html,
            "<p\\b[^>]*class=[\"'][^\"']*\\bstatus-title\\b[^\"']*[\"'][^>]*>(.*?)</p\\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!status.Success)
            return;
        var text = Regex.Replace(DnsAddressParser.VisibleText(status.Groups[1].Value), @"\s+", " ").Trim();
        if (text.Contains("не работает", StringComparison.OrdinalIgnoreCase)
            || text.Contains("недоступен", StringComparison.OrdinalIgnoreCase))
            throw new ProviderUnavailableException(
                "Xbox DNS: поставщик сообщает, что сервис временно недоступен. "
                + "Ответ DNS не подтверждает доступ к сайтам. Выберите другого поставщика.");
    }
}

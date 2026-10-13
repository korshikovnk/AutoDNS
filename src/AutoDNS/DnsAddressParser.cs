using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace AutoDNS;

internal static class DnsAddressParser
{
    private static readonly Regex Ipv6Regex = new(
        @"(?<![\w:])(?:[0-9a-fA-F]{0,4}:){2,}[0-9a-fA-F]{0,4}(?![\w:])",
        RegexOptions.Compiled);

    internal static string VisibleText(string html)
    {
        var text = Regex.Replace(html, @"<(script|style)\b[^>]*>.*?</\1\s*>", " ",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]*>", " "));
    }

    public static IReadOnlyList<string> ParsePublicIpv6(string text, int max = 4)
    {
        var result = new List<string>();
        foreach (Match match in Ipv6Regex.Matches(text))
        {
            if (result.Count >= max)
                break;
            if (!IPAddress.TryParse(match.Value, out var address)
                || address.AddressFamily != AddressFamily.InterNetworkV6
                || (address.GetAddressBytes()[0] & 0xe0) != 0x20)
                continue;
            var value = address.ToString();
            if (!result.Contains(value, StringComparer.OrdinalIgnoreCase))
                result.Add(value);
        }
        return result;
    }

    private static readonly Regex Ipv4Regex = new(
        @"\b(?:(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\.){3}(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\b",
        RegexOptions.Compiled);

    private static readonly HashSet<string> IgnoredIpv4 = new(StringComparer.Ordinal)
    {
        "1.1.1.1", "1.0.0.1",
        "8.8.8.8", "8.8.4.4",
        "9.9.9.9", "149.112.112.112",
        "0.0.0.0", "255.255.255.255"
    };

    public static IReadOnlyList<string> ParsePublicIpv4(string text, int max = 4)
    {
        var result = new List<string>();
        foreach (Match match in Ipv4Regex.Matches(text))
        {
            if (result.Count >= max)
                break;

            var value = match.Value;
            if (IgnoredIpv4.Contains(value) || result.Contains(value, StringComparer.Ordinal))
                continue;
            if (!IPAddress.TryParse(value, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
                continue;
            if (!IsPublicIpv4(address))
                continue;

            result.Add(value);
        }

        return result;
    }

    private static bool IsPublicIpv4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (bytes[0] == 10)
            return false;
        if (bytes[0] == 127)
            return false;
        if (bytes[0] == 169 && bytes[1] == 254)
            return false;
        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            return false;
        if (bytes[0] == 192 && bytes[1] == 168)
            return false;
        if (bytes[0] >= 224)
            return false;
        return true;
    }
}

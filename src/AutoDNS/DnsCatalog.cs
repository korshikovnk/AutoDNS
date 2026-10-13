namespace AutoDNS;

internal sealed record DnsProvider(
    string Id,
    string DisplayName,
    string DohTemplate,
    IReadOnlyList<string> Ipv4,
    IReadOnlyList<string> Ipv6,
    string? InfoUrl);

internal sealed record DnsEndpoints(
    string ProviderId,
    string DisplayName,
    string DohTemplate,
    IReadOnlyList<string> Ipv4,
    IReadOnlyList<string> Ipv6);

internal static class DnsCatalog
{
    public static IReadOnlyList<DnsProvider> All { get; } =
    [
        new(
            "malw",
            "dns.malw.link",
            "https://dns.malw.link/dns-query",
            ["179.254.166.171", "164.5.224.81"],
            ["2a12:bec4:1bb0:27dd::2", "2a13:82c0:0:d::3"],
            "https://info.dns.malw.link/setup"),
        new(
            "xbox",
            "Xbox DNS",
            "https://xbox-dns.ru/dns-query",
            ["111.88.96.54", "111.88.96.55"],
            ["2a00:ab00:1233:26::50", "2a00:ab00:1233:26::51"],
            "https://xbox-dns.ru/"),
        new(
            "geohide",
            "GeoHide",
            "https://dns.geohide.ru:444/dns-query",
            ["95.182.120.241", "45.155.204.190"],
            [],
            "https://dns.geohide.ru:8443/"),
        new(
            "astracat",
            "Astracat",
            "https://dns.astracat.ru/dns-query",
            ["185.139.69.24", "87.121.221.179"],
            [],
            "https://dns.astracat.ru/")
    ];

    public static DnsProvider? Find(string? id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    public static DnsProvider Require(string? id) =>
        Find(id) ?? Find(AppConstants.DefaultProviderId)!;

    public static DnsEndpoints EndpointsFrom(DnsProvider provider, IReadOnlyList<string>? ipv4 = null, IReadOnlyList<string>? ipv6 = null)
    {
        var v4 = MergeAddresses(provider.Ipv4, ipv4, max: 2);
        var v6 = MergeAddresses(provider.Ipv6, ipv6, max: 2);
        return new DnsEndpoints(provider.Id, provider.DisplayName, provider.DohTemplate, v4, v6);
    }

    internal static IReadOnlyList<string> MergeAddresses(
        IReadOnlyList<string> fallback,
        IReadOnlyList<string>? discovered,
        int max)
    {
        // A provider can replace every address. Never append obsolete servers to a fresh list.
        var refreshed = Unique(discovered, max);
        return refreshed.Count > 0 ? refreshed : Unique(fallback, max);
    }

    private static IReadOnlyList<string> Unique(IReadOnlyList<string>? values, int max)
    {
        var result = new List<string>();
        foreach (var value in values ?? [])
        {
            if (result.Count >= max)
                break;
            if (string.IsNullOrWhiteSpace(value) || Contains(result, value))
                continue;
            result.Add(value);
        }

        return result;
    }

    private static bool Contains(IEnumerable<string> values, string ip) =>
        values.Any(value => string.Equals(value, ip, StringComparison.OrdinalIgnoreCase));
}

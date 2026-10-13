using Xunit;

namespace AutoDNS.Tests;

public class DnsAddressParserTests
{
    [Fact]
    public void ParsePublicIpv4_TakesProviderAddresses_AndSkipsPrivateAndPublicResolvers()
    {
        var html = """
            Основной (IPv4) 111.88.96.50
            Дополнительный (IPv4) 111.88.96.51
            192.168.0.1 10.0.0.1 127.0.0.1 8.8.8.8 1.1.1.1
            """;

        var ips = DnsAddressParser.ParsePublicIpv4(html, max: 2);

        Assert.Equal(["111.88.96.50", "111.88.96.51"], ips);
    }

    [Fact]
    public void ParsePublicIpv4_PreservesOrder_AndDeduplicates()
    {
        var ips = DnsAddressParser.ParsePublicIpv4("179.254.166.171 179.254.166.171 164.5.224.81");
        Assert.Equal(["179.254.166.171", "164.5.224.81"], ips);
    }
}

public class DnsCatalogTests
{
    [Fact]
    public void Catalog_HasUniqueIds_AndDefaultProvider()
    {
        Assert.Equal(DnsCatalog.All.Count, DnsCatalog.All.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.NotNull(DnsCatalog.Find(AppConstants.DefaultProviderId));
        Assert.All(DnsCatalog.All, provider =>
        {
            Assert.False(string.IsNullOrWhiteSpace(provider.DisplayName));
            Assert.StartsWith("https://", provider.DohTemplate, StringComparison.OrdinalIgnoreCase);
            Assert.NotEmpty(provider.Ipv4);
        });
    }

    [Fact]
    public void EndpointsFrom_FallsBack_WhenRefreshIsEmpty()
    {
        var provider = DnsCatalog.Require("malw");
        var endpoints = DnsCatalog.EndpointsFrom(provider, [], []);
        Assert.Equal(provider.Ipv4.Take(2), endpoints.Ipv4);
        Assert.Equal(provider.DohTemplate, endpoints.DohTemplate);
    }

    [Fact]
    public void MergeAddresses_UsesFreshAddresses_InPublishedOrder()
    {
        var merged = DnsCatalog.MergeAddresses(
            ["179.254.166.171", "164.5.224.81"],
            ["95.216.204.218", "179.254.166.171"],
            max: 2);

        Assert.Equal(["95.216.204.218", "179.254.166.171"], merged);
    }
}

public class HostsCleanupTests
{
    [Fact]
    public void RemoveManagedSection_RemovesOnlyManagedBlock()
    {
        var original = "127.0.0.1 localhost\r\n# custom\r\n1.2.3.4 example.test\r\n"
                       + AppConstants.ManagedBegin + "\r\n45.1.2.3 chatgpt.com\r\n"
                       + AppConstants.ManagedEnd + "\r\n";

        var updated = HostsCleanup.RemoveManagedSection(original);

        Assert.Contains("example.test", updated);
        Assert.Contains("localhost", updated);
        Assert.DoesNotContain(AppConstants.ManagedBegin, updated);
        Assert.DoesNotContain("chatgpt.com", updated);
    }
}

public class BrowserDnsTests
{
    [Fact]
    public void NormalizeTemplate_AddsRfcQueryPlaceholder()
    {
        Assert.Equal(
            "https://dns.malw.link/dns-query{?dns}",
            BrowserDns.NormalizeTemplate("https://dns.malw.link/dns-query"));
        Assert.Equal(
            "https://dns.geohide.ru:444/dns-query{?dns}",
            BrowserDns.NormalizeTemplate("https://dns.geohide.ru:444/dns-query{?dns}"));
    }
}

public class WindowsDnsScriptTests
{
    [Fact]
    public void NetshCommands_UseIpsAndTemplate_WithoutPowerShell()
    {
        var endpoints = DnsCatalog.EndpointsFrom(DnsCatalog.Require("malw"));
        var ipv4 = WindowsDns.BuildIpv4Commands(12, endpoints.Ipv4);
        var add = WindowsDns.BuildDohAddArguments(endpoints.Ipv4[0], endpoints.DohTemplate);

        Assert.Contains("name=12", ipv4[0]);
        Assert.Contains("179.254.166.171", ipv4[0]);
        Assert.Contains("164.5.224.81", ipv4[1]);
        Assert.DoesNotContain("AddressFamily", ipv4[0]);
        Assert.Contains("179.254.166.171", add);
        Assert.Contains("https://dns.malw.link/dns-query", add);
        Assert.Contains("autoupgrade=yes", add);
        Assert.Equal(
            @"SYSTEM\CurrentControlSet\Services\Dnscache\InterfaceSpecificParameters\{guid}\DohInterfaceSettings\Doh\179.254.166.171",
            WindowsDns.DohRegistryPath("{guid}", "179.254.166.171"));
        Assert.Contains(@"DohInterfaceSettings\Doh6\", WindowsDns.DohRegistryPath("{guid}", "2a12:bec4:1bb0:27dd::2"));
        Assert.Equal(["179.254.166.171", "2a12:bec4:1bb0:27dd::2"], WindowsDns.DohServers(endpoints));
        Assert.Equal(["164.5.224.81", "2a13:82c0:0:d::3"], WindowsDns.PlainServers(endpoints));
    }
}


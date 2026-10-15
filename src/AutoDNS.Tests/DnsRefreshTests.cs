using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace AutoDNS.Tests;

public class DnsRefreshTests
{
    [Fact]
    public async Task Refresh_ReplacesBothFamilies_WhenAllOldAddressesDisappeared()
    {
        using var http = new HttpClient(new ResponseHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                <script>const oldIp = '80.253.249.40';</script>
                <a href="https://95.216.204.218/">Help</a>
                <p>Primary: <code>179.254.166.171</code></p>
                <p>Secondary: <code>164.5.224.81</code></p>
                <code>2a12:bec4:1bb0:27dd::2</code><code>2a13:82c0:0:d::3</code>
                """)
        }));
        var provider = DnsCatalog.Require("malw") with
        {
            Ipv4 = ["80.253.249.40", "193.23.209.189"],
            Ipv6 = ["2a12:bec4:1460:5b7::2", "2a01:ecc0:680:120::2"]
        };

        var endpoints = await DnsRefreshService.ResolveAsync(provider, http);

        Assert.Equal(["179.254.166.171", "164.5.224.81"], endpoints.Ipv4);
        Assert.Equal(["2a12:bec4:1bb0:27dd::2", "2a13:82c0:0:d::3"], endpoints.Ipv6);
    }

    [Fact]
    public async Task Refresh_KeepsFallback_WhenWebsiteIsUnavailable()
    {
        using var http = new HttpClient(new ResponseHandler(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var provider = DnsCatalog.Require("malw");
        var endpoints = await DnsRefreshService.ResolveAsync(provider, http);
        Assert.Equal(provider.Ipv4, endpoints.Ipv4);
        Assert.Equal(provider.Ipv6, endpoints.Ipv6);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HealthCheck_RejectsHttpErrorsAndHtml(bool html)
    {
        using var http = new HttpClient(new ResponseHandler(() => new HttpResponseMessage(
            html ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("<html>Service unavailable</html>")
        }));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DnsHealthCheck.EnsureAvailableAsync(DnsCatalog.EndpointsFrom(DnsCatalog.Require("xbox")), http));
        Assert.Contains("не отвечает", exception.Message);
    }

    [Fact]
    public async Task HealthCheck_AcceptsSuccessfulDnsReply()
    {
        var message = DnsReply("example.com", IPAddress.Parse("1.2.3.4"));
        using var http = new HttpClient(new ResponseHandler(() =>
        {
            var content = new ByteArrayContent(message);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }));
        await DnsHealthCheck.EnsureAvailableAsync(DnsCatalog.EndpointsFrom(DnsCatalog.Require("malw")), http);
        Assert.Equal([IPAddress.Parse("1.2.3.4")], DnsHealthCheck.ReadIpv4Answers(message));
        message[3] = 0x82; // SERVFAIL
        Assert.False(DnsHealthCheck.IsSuccessfulResponse(message));
        message[3] = 0x80;
        message[7] = 0;
        Assert.False(DnsHealthCheck.IsSuccessfulResponse(message));
    }

    [Fact]
    public void SiteAccessDescription_NamesFailures_WithoutUncheckedDisclaimer()
    {
        Assert.Equal("ChatGPT, Gemini и Claude открываются.", DnsHealthCheck.DescribeSiteAccess([]));
        Assert.Equal("Не открывается: Gemini.", DnsHealthCheck.DescribeSiteAccess(["Gemini"]));
        Assert.Equal("Не открываются: ChatGPT, Gemini.", DnsHealthCheck.DescribeSiteAccess(["ChatGPT", "Gemini"]));
        Assert.Equal(ProviderAvailability.Available, DnsHealthCheck.StatusFromFailures(0, 3));
        Assert.Equal(ProviderAvailability.Partial, DnsHealthCheck.StatusFromFailures(1, 3));
        Assert.Equal(ProviderAvailability.Unavailable, DnsHealthCheck.StatusFromFailures(3, 3));
        Assert.DoesNotContain("не проверен", DnsHealthCheck.DescribeSiteAccess([]));
    }

    private static byte[] DnsReply(string domain, IPAddress address)
    {
        var query = DnsHealthCheck.BuildQuery(domain);
        var message = new byte[query.Length + 16];
        query.CopyTo(message, 0);
        message[2] = 0x81;
        message[3] = 0x80;
        message[7] = 1;
        var answer = query.Length;
        message[answer] = 0xC0;
        message[answer + 1] = 0x0C;
        message[answer + 3] = 1;
        message[answer + 5] = 1;
        message[answer + 11] = 4;
        address.GetAddressBytes().CopyTo(message, answer + 12);
        return message;
    }

    [Fact]
    public void ParseIpv6_SkipsLocalAddressesAndDeduplicates()
    {
        Assert.Equal(["2a12:bec4:1bb0:27dd::2"], DnsAddressParser.ParsePublicIpv6(
            "::1 fe80::1 fd00::1 2a12:bec4:1bb0:27dd::2 2a12:bec4:1bb0:27dd::2"));
    }

    private sealed class ResponseHandler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response());
    }
}

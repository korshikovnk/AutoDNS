namespace AutoDNS;

internal static class DnsRefreshService
{
    private static readonly HttpClient Http = CreateClient();

    public static async Task<DnsEndpoints> ResolveAsync(DnsProvider provider, CancellationToken cancellationToken = default)
        => await ResolveAsync(provider, Http, cancellationToken).ConfigureAwait(false);

    internal static async Task<DnsEndpoints> ResolveAsync(DnsProvider provider, HttpClient http,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> ipv4 = provider.Ipv4;
        IReadOnlyList<string> ipv6 = provider.Ipv6;
        if (!string.IsNullOrWhiteSpace(provider.InfoUrl))
        {
            try
            {
                using var response = await http.GetAsync(provider.InfoUrl, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    ProviderStatus.EnsureAvailable(provider, html);
                    var text = DnsAddressParser.VisibleText(html);
                    var parsed = DnsAddressParser.ParsePublicIpv4(text, max: 2);
                    if (parsed.Count > 0)
                        ipv4 = parsed;
                    var parsed6 = DnsAddressParser.ParsePublicIpv6(text, max: 2);
                    if (parsed6.Count > 0)
                        ipv6 = parsed6;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                // Keep fallback addresses when the provider page is unavailable.
            }
            catch (OperationCanceledException)
            {
                // Keep the last known fallback addresses if the provider page is unavailable.
            }
        }

        return DnsCatalog.EndpointsFrom(provider, ipv4, ipv6);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AutoDNS/2.1");
        return client;
    }
}

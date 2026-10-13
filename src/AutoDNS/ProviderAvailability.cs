namespace AutoDNS;

internal enum ProviderAvailability
{
    Unknown,
    Checking,
    Available,
    Partial,
    Unavailable
}

internal sealed record ProviderCheckResult(ProviderAvailability Status, string Description);

internal static class ProviderAvailabilityCheck
{
    public static Task<ProviderCheckResult> CheckAsync(DnsProvider provider, CancellationToken cancellationToken)
        => CheckAsync(provider,
            token => DnsRefreshService.ResolveAsync(provider, token),
            (endpoints, token) => DnsHealthCheck.CheckSitesAsync(endpoints, token),
            cancellationToken);

    internal static async Task<ProviderCheckResult> CheckAsync(DnsProvider provider,
        Func<CancellationToken, Task<DnsEndpoints>> resolve,
        Func<DnsEndpoints, CancellationToken, Task<SiteAccess>> checkSites,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(35));
        DnsEndpoints endpoints;
        try
        {
            using var resolveTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            resolveTimeout.CancelAfter(TimeSpan.FromSeconds(8));
            endpoints = await resolve(resolveTimeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            endpoints = DnsCatalog.EndpointsFrom(provider);
        }

        try
        {
            var access = await checkSites(endpoints, timeout.Token).ConfigureAwait(false);
            return new(access.Status, access.Description);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or OperationCanceledException)
        {
            return new(ProviderAvailability.Unavailable,
                ex is OperationCanceledException ? "Время проверки истекло." : ex.Message);
        }
    }
}

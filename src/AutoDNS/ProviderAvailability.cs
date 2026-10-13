namespace AutoDNS;

internal enum ProviderAvailability
{
    Unknown,
    Checking,
    Available,
    Unavailable
}

internal sealed record ProviderCheckResult(ProviderAvailability Status, string Description);

internal static class ProviderAvailabilityCheck
{
    public static Task<ProviderCheckResult> CheckAsync(DnsProvider provider, CancellationToken cancellationToken)
        => CheckAsync(provider,
            token => DnsRefreshService.ResolveAsync(provider, token),
            (endpoints, token) => DnsHealthCheck.EnsureAvailableAsync(endpoints, token),
            cancellationToken);

    internal static async Task<ProviderCheckResult> CheckAsync(DnsProvider provider,
        Func<CancellationToken, Task<DnsEndpoints>> resolve,
        Func<DnsEndpoints, CancellationToken, Task> checkDns,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var endpoints = await resolve(timeout.Token).ConfigureAwait(false);
            await checkDns(endpoints, timeout.Token).ConfigureAwait(false);
            return new(ProviderAvailability.Available,
                "DNS отвечает. Доступ к отдельным сайтам не проверен.");
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

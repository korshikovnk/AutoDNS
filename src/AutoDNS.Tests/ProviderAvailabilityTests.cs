using Xunit;

namespace AutoDNS.Tests;

public class ProviderAvailabilityTests
{
    [Fact]
    public async Task SuccessfulDnsCheck_ReportsAvailable_WithLimitedScope()
    {
        var provider = DnsCatalog.Require("malw");
        var result = await ProviderAvailabilityCheck.CheckAsync(provider,
            _ => Task.FromResult(DnsCatalog.EndpointsFrom(provider)),
            (_, _) => Task.CompletedTask, CancellationToken.None);
        Assert.Equal(ProviderAvailability.Available, result.Status);
        Assert.Contains("Доступ к отдельным сайтам не проверен", result.Description);
    }

    [Fact]
    public async Task OfficialOutage_ReportsUnavailable_EvenWhenDnsWouldReply()
    {
        var dnsChecked = false;
        var result = await ProviderAvailabilityCheck.CheckAsync(DnsCatalog.Require("xbox"),
            _ => Task.FromException<DnsEndpoints>(new ProviderUnavailableException("Сервис недоступен")),
            (_, _) => { dnsChecked = true; return Task.CompletedTask; }, CancellationToken.None);
        Assert.Equal(ProviderAvailability.Unavailable, result.Status);
        Assert.False(dnsChecked);
        Assert.Contains("недоступен", result.Description);
    }

    [Fact]
    public async Task FailedDnsCheck_ReportsUnavailable()
    {
        var provider = DnsCatalog.Require("malw");
        var result = await ProviderAvailabilityCheck.CheckAsync(provider,
            _ => Task.FromResult(DnsCatalog.EndpointsFrom(provider)),
            (_, _) => Task.FromException(new HttpRequestException("Connection failed")), CancellationToken.None);
        Assert.Equal(ProviderAvailability.Unavailable, result.Status);
    }

    [Fact]
    public async Task ShutdownCancellation_IsNotReportedAsServerFailure()
    {
        using var shutdown = new CancellationTokenSource();
        shutdown.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProviderAvailabilityCheck.CheckAsync(
            DnsCatalog.Require("malw"), token => Task.FromCanceled<DnsEndpoints>(token),
            (_, _) => Task.CompletedTask, shutdown.Token));
    }
}

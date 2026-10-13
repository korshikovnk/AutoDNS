using System.Net;
using Xunit;

namespace AutoDNS.Tests;

public class ProviderStatusTests
{
    [Theory]
    [InlineData("Сервис временно не&nbsp;работает")]
    [InlineData("Сервис недоступен")]
    public void OfficialOutageBanner_RejectsXbox(string status)
    {
        Assert.Throws<ProviderUnavailableException>(() => ProviderStatus.EnsureAvailable(
            DnsCatalog.Require("xbox"), $"<p class='status-title'>{status}</p>"));
    }

    [Theory]
    [InlineData("<p class='status-title'>Сервис работает</p>")]
    [InlineData("<p>Что делать, если сервис не работает?</p>")]
    public void AvailableBannerOrFaq_DoesNotDeclareOutage(string html)
    {
        ProviderStatus.EnsureAvailable(DnsCatalog.Require("xbox"), html);
    }

    [Fact]
    public async Task Refresh_PropagatesOutage_InsteadOfApplyingFallbackAddresses()
    {
        using var http = new HttpClient(new OutageHandler());
        await Assert.ThrowsAsync<ProviderUnavailableException>(() =>
            DnsRefreshService.ResolveAsync(DnsCatalog.Require("xbox"), http));
    }

    private sealed class OutageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<p class='status-title'>Сервис временно не&nbsp;работает</p>111.88.96.54 111.88.96.55")
            });
    }
}

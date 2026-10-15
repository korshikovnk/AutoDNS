using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;

namespace AutoDNS;

internal readonly record struct TargetSite(string Name, string Domain);

internal static class TargetSites
{
    public static IReadOnlyList<TargetSite> All { get; } =
    [
        new("ChatGPT", "chatgpt.com"),
        new("Gemini", "gemini.google.com"),
        new("Claude", "claude.ai")
    ];
}

internal readonly record struct SiteAccess(ProviderAvailability Status, string Description);

internal static class DnsHealthCheck
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static Task EnsureAvailableAsync(DnsEndpoints endpoints, CancellationToken cancellationToken = default)
        => EnsureAvailableAsync(endpoints, Http, cancellationToken);

    internal static async Task EnsureAvailableAsync(DnsEndpoints endpoints, HttpClient http,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var addresses = await ResolveIpv4Async(endpoints.DohTemplate, "example.com", http, cancellationToken)
                .ConfigureAwait(false);
            if (addresses.Count == 0)
                throw new HttpRequestException("Сервер не вернул успешный DNS-ответ.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            throw new InvalidOperationException(
                "DNS " + endpoints.DisplayName + " не отвечает.", ex);
        }
    }

    public static Task<SiteAccess> CheckSitesAsync(DnsEndpoints endpoints, CancellationToken cancellationToken = default)
        => CheckSitesAsync(endpoints, Http, cancellationToken);

    internal static async Task<SiteAccess> CheckSitesAsync(DnsEndpoints endpoints, HttpClient http,
        CancellationToken cancellationToken = default)
    {
        var failed = new string?[TargetSites.All.Count];
        await Task.WhenAll(TargetSites.All.Select(async (site, index) =>
        {
            using var siteTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            siteTimeout.CancelAfter(TimeSpan.FromSeconds(12));
            try
            {
                var addresses = await ResolveIpv4Async(endpoints.DohTemplate, site.Domain, http, siteTimeout.Token)
                    .ConfigureAwait(false);
                foreach (var address in addresses)
                {
                    if (await ProbeHttpsAsync(address, site.Domain, siteTimeout.Token).ConfigureAwait(false))
                        return;
                }

                failed[index] = site.Name;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                failed[index] = site.Name;
            }
        })).ConfigureAwait(false);

        var names = failed.Where(name => name is not null).Cast<string>().ToArray();
        return new SiteAccess(StatusFromFailures(names.Length, TargetSites.All.Count), DescribeSiteAccess(names));
    }

    internal static ProviderAvailability StatusFromFailures(int failedCount, int total) =>
        failedCount <= 0 ? ProviderAvailability.Available
        : failedCount >= total ? ProviderAvailability.Unavailable
        : ProviderAvailability.Partial;

    internal static string DescribeSiteAccess(IReadOnlyList<string> failed)
    {
        if (failed.Count == 0)
        {
            var names = TargetSites.All.Select(site => site.Name).ToArray();
            return names.Length == 1
                ? names[0] + " открывается."
                : string.Join(", ", names[..^1]) + " и " + names[^1] + " открываются.";
        }

        return (failed.Count == 1 ? "Не открывается: " : "Не открываются: ")
            + string.Join(", ", failed) + ".";
    }

    internal static byte[] BuildQuery(string domain, ushort id = 0x1234)
    {
        var buffer = new MemoryStream();
        buffer.WriteByte((byte)(id >> 8));
        buffer.WriteByte((byte)id);
        buffer.Write([0x01, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);
        foreach (var label in domain.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            buffer.WriteByte((byte)bytes.Length);
            buffer.Write(bytes);
        }

        buffer.WriteByte(0);
        buffer.Write([0x00, 0x01, 0x00, 0x01]);
        return buffer.ToArray();
    }

    internal static bool IsSuccessfulResponse(byte[] message, ushort id = 0x1234) =>
        message.Length >= 12
        && message[0] == (byte)(id >> 8) && message[1] == (byte)id
        && (message[2] & 0x80) != 0 && (message[2] & 0x02) == 0
        && (message[3] & 0x0f) == 0
        && (message[6] != 0 || message[7] != 0);

    internal static IReadOnlyList<IPAddress> ReadIpv4Answers(byte[] message)
    {
        var result = new List<IPAddress>();
        if (!IsSuccessfulResponse(message))
            return result;

        var offset = 12;
        var questions = (message[4] << 8) | message[5];
        for (var i = 0; i < questions; i++)
        {
            if (!TryAdvanceName(message, ref offset))
                return result;
            offset += 4;
            if (offset > message.Length)
                return result;
        }

        var answers = (message[6] << 8) | message[7];
        for (var i = 0; i < answers; i++)
        {
            if (!TryAdvanceName(message, ref offset) || offset + 10 > message.Length)
                return result;

            var type = (message[offset] << 8) | message[offset + 1];
            var length = (message[offset + 8] << 8) | message[offset + 9];
            offset += 10;
            if (length < 0 || offset + length > message.Length)
                return result;
            if (type == 1 && length == 4)
                result.Add(new IPAddress(message.AsSpan(offset, 4)));
            offset += length;
        }

        return result;
    }

    private static async Task<IReadOnlyList<IPAddress>> ResolveIpv4Async(string dohTemplate, string domain,
        HttpClient http, CancellationToken cancellationToken)
    {
        var query = BuildQuery(domain);
        using var request = new HttpRequestMessage(HttpMethod.Post, dohTemplate);
        request.Version = System.Net.HttpVersion.Version20;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
        request.Content = new ByteArrayContent(query);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-message"));
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode
            || response.Content.Headers.ContentType?.MediaType != "application/dns-message")
            return [];

        var message = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return ReadIpv4Answers(message);
    }

    private static async Task<bool> ProbeHttpsAsync(IPAddress address, string domain, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(address, 443, timeout.Token).ConfigureAwait(false);
            await using var ssl = new SslStream(tcp.GetStream());
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = domain
            }, timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryAdvanceName(byte[] message, ref int offset)
    {
        var guard = 0;
        while (offset < message.Length && guard++ < 64)
        {
            var length = message[offset];
            if (length == 0)
            {
                offset++;
                return true;
            }

            if ((length & 0xC0) == 0xC0)
            {
                offset += 2;
                return offset <= message.Length;
            }

            if ((length & 0xC0) != 0)
                return false;

            offset += length + 1;
        }

        return false;
    }
}

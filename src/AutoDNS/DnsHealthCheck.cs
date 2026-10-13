using System.Net.Http.Headers;

namespace AutoDNS;

internal static class DnsHealthCheck
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    // Standard DNS A query for example.com, with transaction ID 0x1234.
    private static readonly byte[] Query =
    [0x12, 0x34, 0x01, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
     7, (byte)'e', (byte)'x', (byte)'a', (byte)'m', (byte)'p', (byte)'l', (byte)'e',
     3, (byte)'c', (byte)'o', (byte)'m', 0, 0, 1, 0, 1];

    public static Task EnsureAvailableAsync(DnsEndpoints endpoints, CancellationToken cancellationToken = default)
        => EnsureAvailableAsync(endpoints, Http, cancellationToken);

    internal static async Task EnsureAvailableAsync(DnsEndpoints endpoints, HttpClient http,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoints.DohTemplate);
            request.Version = System.Net.HttpVersion.Version20;
            request.VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
            request.Content = new ByteArrayContent(Query);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-message"));
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var message = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            if (response.Content.Headers.ContentType?.MediaType != "application/dns-message"
                || !IsSuccessfulResponse(message))
                throw new HttpRequestException("Сервер не вернул успешный DNS-ответ.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            throw new InvalidOperationException(
                "DNS " + endpoints.DisplayName + " не отвечает. Выберите другого поставщика в меню «Поставщик DNS».", ex);
        }
    }

    internal static bool IsSuccessfulResponse(byte[] message) =>
        message.Length >= Query.Length
        && message[0] == Query[0] && message[1] == Query[1]
        && (message[2] & 0x80) != 0 && (message[2] & 0x02) == 0
        && (message[3] & 0x0f) == 0
        && (message[6] != 0 || message[7] != 0);
}

using System.Reflection;
using System.Text.Json;

namespace AutoDNS;

internal readonly record struct ReleaseNotice(bool UpdateAvailable, string Text);

internal static class ReleaseCheck
{
    public const string ReleasesPageUrl = "https://github.com/korshikovnk/AutoDNS/releases";
    private const string LatestReleaseUrl = "https://api.github.com/repos/korshikovnk/AutoDNS/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    public static Version Current { get; } =
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(2, 2);

    public static async Task<ReleaseNotice> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return CurrentNotice();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!TryReadTag(json, out var latest, out var label))
                return CurrentNotice();

            return NoticeFor(Current, latest, label);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return CurrentNotice();
        }
    }

    internal static ReleaseNotice NoticeFor(Version current, Version? latest, string? latestLabel) =>
        new(latest is not null && Compare(latest, current) > 0,
            Describe(current, latest, latestLabel));

    internal static ReleaseNotice CurrentNotice() => NoticeFor(Current, null, null);

    internal static string Describe(Version current, Version? latest, string? latestLabel)
    {
        var currentLabel = Format(current);
        if (latest is not null && Compare(latest, current) > 0)
        {
            var remote = string.IsNullOrWhiteSpace(latestLabel) ? Format(latest) : latestLabel;
            return "Доступно обновление: " + currentLabel + "->" + remote;
        }

        return "Версия: " + currentLabel;
    }

    internal static string Format(Version version)
    {
        var parts = new List<int> { version.Major, Math.Max(version.Minor, 0) };
        if (version.Build > 0)
            parts.Add(version.Build);
        if (version.Revision > 0)
            parts.Add(version.Revision);
        return string.Join('.', parts);
    }

    internal static int Compare(Version left, Version right)
    {
        for (var index = 0; index < 4; index++)
        {
            var difference = Part(left, index) - Part(right, index);
            if (difference != 0)
                return difference;
        }

        return 0;
    }

    internal static bool TryReadTag(string json, out Version version, out string label)
    {
        version = new Version(0, 0);
        label = "";
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("tag_name", out var tag))
                return false;
            return TryParseTag(tag.GetString(), out version, out label);
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryParseTag(string? tag, out Version version, out string label)
    {
        version = new Version(0, 0);
        label = "";
        if (string.IsNullOrWhiteSpace(tag))
            return false;

        var text = tag.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
            text = text[1..];
        var suffix = text.IndexOf('-');
        if (suffix >= 0)
            text = text[..suffix];
        if (!Version.TryParse(text, out var parsed))
            return false;

        version = parsed;
        label = text;
        return true;
    }

    private static int Part(Version version, int index) => index switch
    {
        0 => version.Major,
        1 => Math.Max(version.Minor, 0),
        2 => Math.Max(version.Build, 0),
        _ => Math.Max(version.Revision, 0)
    };

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AutoDNS");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}

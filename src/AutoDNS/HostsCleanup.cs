using System.Text;

namespace AutoDNS;

internal static class HostsCleanup
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static void RemoveManagedBlock()
    {
        try
        {
            if (!File.Exists(AppConstants.HostsPath))
                return;

            var original = ReadHosts();
            var updated = RemoveManagedSection(original);
            updated = RemoveMarkedSection(updated, AppConstants.LegacyManagedBegin, AppConstants.LegacyManagedEnd);
            if (!string.Equals(original, updated, StringComparison.Ordinal))
                WriteHosts(updated);
        }
        catch
        {
            // Hosts cleanup is best-effort so DNS setup can continue.
        }
    }

    internal static string RemoveManagedSection(string original) =>
        RemoveMarkedSection(original, AppConstants.ManagedBegin, AppConstants.ManagedEnd);

    internal static string RemoveMarkedSection(string original, string beginMarker, string endMarker)
    {
        var normalized = original.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var begin = normalized.IndexOf(beginMarker, StringComparison.Ordinal);
        if (begin < 0)
            return ToWindowsNewlines(normalized.TrimEnd('\n') + "\n");

        var end = normalized.IndexOf(endMarker, StringComparison.Ordinal);
        var before = normalized[..begin].TrimEnd('\n');
        var after = end >= 0
            ? normalized[(end + endMarker.Length)..].TrimStart('\n')
            : string.Empty;

        var remaining = after.Length == 0
            ? before
            : before.Length == 0 ? after : before + "\n" + after;

        return ToWindowsNewlines(remaining.TrimEnd('\n') + "\n");
    }

    private static string ReadHosts()
    {
        var bytes = File.ReadAllBytes(AppConstants.HostsPath);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        return Encoding.UTF8.GetString(bytes);
    }

    private static void WriteHosts(string content)
    {
        var path = AppConstants.HostsPath;
        if (File.Exists(path))
        {
            var attributes = File.GetAttributes(path);
            if (attributes.HasFlag(FileAttributes.ReadOnly))
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }

        File.WriteAllText(path, content, Utf8NoBom);
    }

    private static string ToWindowsNewlines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n");
}

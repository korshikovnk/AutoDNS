using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Microsoft.Win32;

namespace AutoDNS;

internal static class WindowsDns
{
    public static void Apply(DnsEndpoints endpoints)
    {
        if (endpoints.Ipv4.Count == 0)
            throw new InvalidOperationException("У поставщика нет IPv4 DNS-адресов.");

        foreach (var ip in DohServers(endpoints))
            RegisterDoh(ip, endpoints.DohTemplate);
        foreach (var ip in PlainServers(endpoints))
            DisableDohAutoUpgrade(ip);

        var adapters = GetTargetAdapters().ToList();
        if (adapters.Count == 0)
            throw new InvalidOperationException("Не найден сетевой адаптер для настройки DNS.");

        var applied = 0;
        Exception? lastError = null;
        foreach (var adapter in adapters)
        {
            try
            {
                ApplyToAdapter(adapter, endpoints);
                applied++;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        if (applied == 0)
            throw lastError ?? new InvalidOperationException("Не удалось применить DNS ни на одном адаптере.");

        BrowserDns.Apply(endpoints.DohTemplate);
        FlushDns();
    }

    public static void RestoreAutomatic()
    {
        foreach (var adapter in GetTargetAdapters())
        {
            try
            {
                if (TryGetIpv4Index(adapter, out var ipv4Index))
                    RunNetsh($"interface ipv4 set dnsservers name={ipv4Index} source=dhcp", throwOnError: false);
                if (TryGetIpv6Index(adapter, out var ipv6Index))
                    RunNetsh($"interface ipv6 set dnsservers name={ipv6Index} source=dhcp", throwOnError: false);
                ClearInterfaceDoh(adapter);
            }
            catch
            {
                // Uninstall should continue even if an adapter cannot be reset.
            }
        }

        BrowserDns.Restore();
        FlushDns();
    }

    internal static void FlushDns()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "ipconfig.exe"),
                Arguments = "/flushdns",
                CreateNoWindow = true,
                UseShellExecute = false
            });
            process?.WaitForExit(10_000);
        }
        catch
        {
            // DNS is already written; cache flush is best-effort.
        }
    }

    internal static bool IsTargetAdapter(NetworkInterface nic)
    {
        if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            return false;
        if (nic.OperationalStatus == OperationalStatus.NotPresent)
            return false;

        if (ContainsAny(nic.Name, "Loopback", "Bluetooth", "vEthernet"))
            return false;
        if (ContainsAny(nic.Description, "WAN Miniport", "Kernel Debug", "Teredo", "isatap", "6to4",
                "Bluetooth", "Hyper-V", "VirtualBox", "VMware", "Loopback", "Pseudo-Interface"))
            return false;

        return nic.Supports(NetworkInterfaceComponent.IPv4);
    }

    internal static IReadOnlyList<string> BuildIpv4Commands(int interfaceIndex, IReadOnlyList<string> ipv4)
    {
        var commands = new List<string>
        {
            $"interface ipv4 set dnsservers name={interfaceIndex} source=static address={ipv4[0]} register=primary validate=no"
        };
        for (var i = 1; i < ipv4.Count; i++)
        {
            commands.Add(
                $"interface ipv4 add dnsservers name={interfaceIndex} address={ipv4[i]} index={i + 1} validate=no");
        }

        return commands;
    }

    internal static string BuildDohAddArguments(string server, string template) =>
        $"dns add encryption server={FormatServer(server)} dohtemplate={template} autoupgrade=yes udpfallback=no";

    internal static string BuildDohSetArguments(string server, string template) =>
        $"dns set encryption server={FormatServer(server)} dohtemplate={template} autoupgrade=yes udpfallback=no";

    internal static string DohRegistryPath(string interfaceId, string server)
    {
        var family = IsIpv6(server) ? "Doh6" : "Doh";
        return $@"SYSTEM\CurrentControlSet\Services\Dnscache\InterfaceSpecificParameters\{interfaceId}\DohInterfaceSettings\{family}\{server}";
    }

    internal static IEnumerable<string> DohServers(DnsEndpoints endpoints)
    {
        if (endpoints.Ipv4.Count > 0)
            yield return endpoints.Ipv4[0];
        if (endpoints.Ipv6.Count > 0)
            yield return endpoints.Ipv6[0];
    }

    internal static IEnumerable<string> PlainServers(DnsEndpoints endpoints) =>
        endpoints.Ipv4.Skip(1).Concat(endpoints.Ipv6.Skip(1));

    private static IEnumerable<NetworkInterface> GetTargetAdapters()
    {
        var all = NetworkInterface.GetAllNetworkInterfaces().Where(IsTargetAdapter).ToList();
        var preferred = all
            .Where(n => n.NetworkInterfaceType is NetworkInterfaceType.Ethernet
                or NetworkInterfaceType.Wireless80211
                or NetworkInterfaceType.GigabitEthernet)
            .ToList();
        return preferred.Count > 0 ? preferred : all;
    }

    private static void ApplyToAdapter(NetworkInterface adapter, DnsEndpoints endpoints)
    {
        if (!TryGetIpv4Index(adapter, out var ipv4Index))
            throw new InvalidOperationException("У адаптера " + adapter.Name + " нет IPv4.");

        foreach (var command in BuildIpv4Commands(ipv4Index, endpoints.Ipv4))
            RunNetsh(command);

        if (TryGetIpv6Index(adapter, out var ipv6Index))
        {
            if (endpoints.Ipv6.Count > 0)
            {
                RunNetsh(
                    $"interface ipv6 set dnsservers name={ipv6Index} source=static address={endpoints.Ipv6[0]} register=primary validate=no",
                    throwOnError: false);
                for (var i = 1; i < endpoints.Ipv6.Count; i++)
                {
                    RunNetsh(
                        $"interface ipv6 add dnsservers name={ipv6Index} address={endpoints.Ipv6[i]} index={i + 1} validate=no",
                        throwOnError: false);
                }
            }
            else
            {
                RunNetsh(
                    $"interface ipv6 set dnsservers name={ipv6Index} source=static address=none",
                    throwOnError: false);
            }
        }

        WriteInterfaceDoh(adapter.Id, endpoints);
    }

    private static void RegisterDoh(string server, string template)
    {
        var added = RunNetsh(BuildDohAddArguments(server, template), throwOnError: false);
        if (added.ExitCode == 0)
            return;

        var updated = RunNetsh(BuildDohSetArguments(server, template), throwOnError: false);
        if (updated.ExitCode != 0 && !IsIpv6(server))
            throw new InvalidOperationException("Не удалось зарегистрировать DoH для " + server + ".");
    }

    private static void DisableDohAutoUpgrade(string server)
    {
        RunNetsh(
            $"dns set encryption server={FormatServer(server)} autoupgrade=no",
            throwOnError: false);
        RunNetsh(
            $"dns delete encryption server={FormatServer(server)}",
            throwOnError: false);
    }

    private static void WriteInterfaceDoh(string interfaceId, DnsEndpoints endpoints)
    {
        foreach (var ip in DohServers(endpoints))
            WriteDohFlags(interfaceId, ip, enabled: true);
        foreach (var ip in PlainServers(endpoints))
            WriteDohFlags(interfaceId, ip, enabled: false);
    }

    private static void ClearInterfaceDoh(NetworkInterface adapter)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\Dnscache\InterfaceSpecificParameters",
                writable: true);
            key?.DeleteSubKeyTree(adapter.Id, throwOnMissingSubKey: false);
        }
        catch
        {
            // Best-effort.
        }
    }

    private static void WriteDohFlags(string interfaceId, string server, bool enabled)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(DohRegistryPath(interfaceId, server), writable: true);
            key?.SetValue("DohFlags", enabled ? 2L : 0L, RegistryValueKind.QWord);
        }
        catch
        {
            // Per-interface flag is extra; global DoH registration still applies.
        }
    }

    private static bool TryGetIpv4Index(NetworkInterface adapter, out int index)
    {
        try
        {
            index = adapter.GetIPProperties().GetIPv4Properties().Index;
            return index > 0;
        }
        catch (NetworkInformationException)
        {
            index = 0;
            return false;
        }
    }

    private static bool TryGetIpv6Index(NetworkInterface adapter, out int index)
    {
        try
        {
            index = adapter.GetIPProperties().GetIPv6Properties().Index;
            return index > 0;
        }
        catch (NetworkInformationException)
        {
            index = 0;
            return false;
        }
    }

    private static (int ExitCode, string Output) RunNetsh(string arguments, bool throwOnError = true)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "netsh.exe"),
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(20_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new InvalidOperationException("Настройка DNS заняла слишком много времени.");
        }

        if (throwOnError && process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "Не удалось выполнить настройку DNS (код " + process.ExitCode + ").");
        }

        return (process.ExitCode, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
    }

    private static string FormatServer(string server) =>
        IsIpv6(server) ? "\"" + server + "\"" : server;

    private static bool IsIpv6(string server) =>
        IPAddress.TryParse(server, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6;

    private static bool ContainsAny(string text, params string[] parts)
    {
        foreach (var part in parts)
        {
            if (text.Contains(part, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}

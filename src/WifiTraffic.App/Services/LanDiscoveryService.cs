using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace WifiTraffic.Services;

public sealed record LanDevice(
    string IpAddress,
    string MacAddress,
    string HostName,
    string Status);

public sealed class LanDiscoveryService
{
    public async Task<IReadOnlyList<LanDevice>> ScanAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var local = GetLocalIpv4()
            ?? throw new InvalidOperationException("Could not detect an active local IPv4 network.");

        var bytes = local.Address.GetAddressBytes();
        var targets = Enumerable.Range(1, 254)
            .Select(i => new IPAddress(new byte[] { bytes[0], bytes[1], bytes[2], (byte)i }))
            .Where(ip => !ip.Equals(local.Address))
            .ToArray();

        progress?.Report($"Scanning {targets.Length} local addresses on {bytes[0]}.{bytes[1]}.{bytes[2]}.0/24...");

        var online = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>();
        using var gate = new SemaphoreSlim(64, 64);

        var tasks = targets.Select(async ip =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(ip, TimeSpan.FromMilliseconds(350), Array.Empty<byte>(), new PingOptions(64, true), cancellationToken);
                if (reply.Status == IPStatus.Success)
                    online[ip.ToString()] = true;
            }
            catch
            {
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);

        progress?.Report($"Found {online.Count} responding devices. Reading ARP table...");

        var arp = ReadArpTable();
        foreach (var ip in arp.Keys)
            online.TryAdd(ip, true);

        online.TryAdd(local.Address.ToString(), true);

        var rows = new List<LanDevice>();

        foreach (var ip in online.Keys
                     .Select(IPAddress.Parse)
                     .OrderBy(ip => BitConverter.ToUInt32(ip.GetAddressBytes().Reverse().ToArray(), 0)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ipText = ip.ToString();
            var mac = arp.TryGetValue(ipText, out var value) ? value : "";
            var host = await TryResolveHostAsync(ip, cancellationToken);

            if (ip.Equals(local.Address))
            {
                host = string.IsNullOrWhiteSpace(host)
                    ? $"{Environment.MachineName} (This PC)"
                    : $"{host} (This PC)";
            }

            rows.Add(new LanDevice(
                ipText,
                mac,
                host,
                "Online / recently seen"));
        }

        progress?.Report($"Scan complete: {rows.Count} devices found.");
        return rows;
    }

    private static (IPAddress Address, IPAddress Mask)? GetLocalIpv4()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(n => n.OperationalStatus == OperationalStatus.Up)
                     .OrderByDescending(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                     .ThenByDescending(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet))
        {
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            var props = nic.GetIPProperties();
            if (!props.GatewayAddresses.Any(g =>
                    g.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !g.Address.Equals(IPAddress.Any)))
                continue;

            var address = props.UnicastAddresses.FirstOrDefault(a =>
                a.Address.AddressFamily == AddressFamily.InterNetwork &&
                a.IPv4Mask is not null &&
                !a.Address.ToString().StartsWith("169.254."));

            if (address?.IPv4Mask is not null)
                return (address.Address, address.IPv4Mask);
        }

        return null;
    }

    private static Dictionary<string, string> ReadArpTable()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "arp",
                Arguments = "-a",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process is null)
                return result;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);

            var regex = new Regex(
                @"^\s*(?<ip>\d{1,3}(?:\.\d{1,3}){3})\s+(?<mac>[0-9a-fA-F-]{17})\s+",
                RegexOptions.Multiline);

            foreach (Match match in regex.Matches(output))
            {
                var ip = match.Groups["ip"].Value;
                var mac = match.Groups["mac"].Value.ToUpperInvariant().Replace('-', ':');

                if (!ip.EndsWith(".255") && mac != "FF:FF:FF:FF:FF:FF")
                    result[ip] = mac;
            }
        }
        catch
        {
        }

        return result;
    }

    private static async Task<string> TryResolveHostAsync(IPAddress ip, CancellationToken token)
    {
        try
        {
            var entry = await Dns.GetHostEntryAsync(ip, token).WaitAsync(TimeSpan.FromMilliseconds(600), token);
            return entry.HostName ?? "";
        }
        catch
        {
            return "";
        }
    }
}

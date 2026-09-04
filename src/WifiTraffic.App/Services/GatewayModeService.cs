using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace WifiTraffic.Services;

public sealed class GatewayModeService
{
    private static readonly string[] HotspotKeywords =
    [
        "wi-fi direct",
        "wifi direct",
        "mobile hotspot",
        "local area connection*",
        "hosted network",
        "virtual adapter"
    ];

    public void OpenMobileHotspotSettings()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "ms-settings:network-mobilehotspot",
            UseShellExecute = true
        });
    }

    public bool IsLikelyGatewayCaptureAdapter(string captureName, string captureDescription)
    {
        var haystack = $"{captureName} {captureDescription}".ToLowerInvariant();

        if (HotspotKeywords.Any(haystack.Contains))
            return true;

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            var nicText = $"{nic.Name} {nic.Description} {nic.Id}".ToLowerInvariant();
            var textLooksRelated =
                (!string.IsNullOrWhiteSpace(nic.Description) &&
                 haystack.Contains(nic.Description.ToLowerInvariant())) ||
                (!string.IsNullOrWhiteSpace(nic.Name) &&
                 haystack.Contains(nic.Name.ToLowerInvariant()));

            if (!textLooksRelated)
                continue;

            if (LooksLikeWindowsHotspotInterface(nic))
                return true;
        }

        return false;
    }

    public IReadOnlyList<string> GetGatewayInterfaceSummary()
    {
        var results = new List<string>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (!LooksLikeWindowsHotspotInterface(nic))
                continue;

            var addresses = nic.GetIPProperties().UnicastAddresses
                .Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(x => x.Address.ToString())
                .ToArray();

            var addressText = addresses.Length == 0 ? "no IPv4 yet" : string.Join(", ", addresses);
            results.Add($"{nic.Name} — {addressText}");
        }

        return results;
    }

    private static bool LooksLikeWindowsHotspotInterface(NetworkInterface nic)
    {
        var text = $"{nic.Name} {nic.Description}".ToLowerInvariant();

        if (HotspotKeywords.Any(text.Contains))
            return true;

        if (nic.OperationalStatus != OperationalStatus.Up)
            return false;

        foreach (var address in nic.GetIPProperties().UnicastAddresses)
        {
            if (address.Address.AddressFamily != AddressFamily.InterNetwork)
                continue;

            // 192.168.137.1 is the classic Windows ICS subnet.
            if (address.Address.Equals(IPAddress.Parse("192.168.137.1")))
                return true;
        }

        return false;
    }
}

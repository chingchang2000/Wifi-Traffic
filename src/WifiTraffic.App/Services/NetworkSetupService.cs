using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace WifiTraffic.Services;

public sealed record RouterDnsInfo(string InterfaceName, string LanIp, string GatewayIp);

public sealed class NetworkSetupService
{
    public RouterDnsInfo? GetRouterDnsInfo()
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n =>
                n.OperationalStatus == OperationalStatus.Up &&
                n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            .Select(n => new
            {
                Nic = n,
                Props = n.GetIPProperties(),
                Address = n.GetIPProperties().UnicastAddresses
                    .FirstOrDefault(a =>
                        a.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(a.Address) &&
                        !a.Address.ToString().StartsWith("169.254.")),
                Gateway = n.GetIPProperties().GatewayAddresses
                    .Select(g => g.Address)
                    .FirstOrDefault(a =>
                        a.AddressFamily == AddressFamily.InterNetwork &&
                        !a.Equals(IPAddress.Any))
            })
            .Where(x => x.Address is not null && x.Gateway is not null)
            .OrderByDescending(x => x.Nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            .ThenByDescending(x => x.Nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
            .ToList();

        var selected = candidates.FirstOrDefault();
        if (selected?.Address is null || selected.Gateway is null)
            return null;

        return new RouterDnsInfo(
            selected.Nic.Name,
            selected.Address.Address.ToString(),
            selected.Gateway.ToString());
    }

    public void EnsureDnsFirewallRules()
    {
        AddFirewallRule("WiFi Traffic DNS Sensor UDP", "UDP");
        AddFirewallRule("WiFi Traffic DNS Sensor TCP", "TCP");
    }

    public void OpenRouterAdminPage()
    {
        var info = GetRouterDnsInfo()
            ?? throw new InvalidOperationException("Could not detect the router/default gateway.");

        Process.Start(new ProcessStartInfo
        {
            FileName = $"http://{info.GatewayIp}",
            UseShellExecute = true
        });
    }

    private static void AddFirewallRule(string name, string protocol)
    {
        var args =
            $"advfirewall firewall add rule name=\"{name}\" dir=in action=allow " +
            $"protocol={protocol} localport=53 profile=private remoteip=localsubnet";

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });

        process?.WaitForExit(5000);
    }
}

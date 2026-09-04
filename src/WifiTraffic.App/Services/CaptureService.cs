using System.Net;
using System.Net.NetworkInformation;
using PacketDotNet;
using SharpPcap;
using WifiTraffic.Models;

namespace WifiTraffic.Services;

public sealed class CaptureService : IDisposable
{
    private ICaptureDevice? _device;
    private readonly HashSet<string> _localAddresses = new(StringComparer.OrdinalIgnoreCase);
    private readonly GatewayModeService _gatewayMode = new();

    public event EventHandler<TrafficRecord>? TrafficObserved;
    public event EventHandler<string>? CaptureError;

    public bool IsRunning => _device is not null;

    public CaptureService()
    {
        RefreshLocalAddresses();
    }

    public IReadOnlyList<CaptureAdapter> GetAdapters()
    {
        var list = new List<CaptureAdapter>();

        foreach (var device in CaptureDeviceList.Instance)
        {
            var description = device.Description ?? "";
            list.Add(new CaptureAdapter(
                device.Name,
                device.Name,
                description,
                _gatewayMode.IsLikelyGatewayCaptureAdapter(device.Name, description)));
        }

        return list
            .OrderByDescending(x => x.IsGatewayCandidate)
            .ThenBy(x => x.Description)
            .ToList();
    }

    public void Start(string adapterId)
    {
        Stop();
        RefreshLocalAddresses();

        var device = CaptureDeviceList.Instance
            .FirstOrDefault(d => string.Equals(d.Name, adapterId, StringComparison.OrdinalIgnoreCase));

        if (device is null)
            throw new InvalidOperationException("Network adapter was not found.");

        device.OnPacketArrival += OnPacketArrival;

        try
        {
            device.Open(DeviceModes.Promiscuous, 1000);
            device.Filter = "ip or ip6";
            _device = device;
            device.StartCapture();
        }
        catch
        {
            device.OnPacketArrival -= OnPacketArrival;
            try
            {
                device.Close();
            }
            catch
            {
            }

            _device = null;
            throw;
        }
    }

    public void Stop()
    {
        var device = _device;
        _device = null;

        if (device is null)
            return;

        try
        {
            device.StopCapture();
        }
        catch
        {
        }

        device.OnPacketArrival -= OnPacketArrival;

        try
        {
            device.Close();
        }
        catch
        {
        }
    }

    private void OnPacketArrival(object sender, PacketCapture e)
    {
        try
        {
            var raw = e.GetPacket();
            var packet = Packet.ParsePacket(raw.LinkLayerType, raw.Data);
            var ip = packet.Extract<IPPacket>();

            if (ip is null)
                return;

            var sourceIp = ip.SourceAddress?.ToString() ?? "";
            var destinationIp = ip.DestinationAddress?.ToString() ?? "";

            var sourcePort = 0;
            var destinationPort = 0;
            var protocol = ip.Protocol.ToString();
            byte[]? payload = null;

            var tcp = packet.Extract<TcpPacket>();
            if (tcp is not null)
            {
                sourcePort = tcp.SourcePort;
                destinationPort = tcp.DestinationPort;
                protocol = "TCP";
                payload = tcp.PayloadData;
            }
            else
            {
                var udp = packet.Extract<UdpPacket>();
                if (udp is not null)
                {
                    sourcePort = udp.SourcePort;
                    destinationPort = udp.DestinationPort;
                    protocol = "UDP";
                    payload = udp.PayloadData;
                }
            }

            var domain = ProtocolInspector.TryExtractDomain(payload, sourcePort, destinationPort);
            var direction = GetDirection(sourceIp, destinationIp);

            TrafficObserved?.Invoke(this, new TrafficRecord
            {
                Timestamp = DateTime.Now,
                Adapter = _device?.Description ?? _device?.Name ?? "",
                SourceIp = sourceIp,
                DestinationIp = destinationIp,
                SourcePort = sourcePort,
                DestinationPort = destinationPort,
                Protocol = protocol,
                Direction = direction,
                Domain = domain,
                Bytes = raw.Data.Length
            });
        }
        catch (Exception ex)
        {
            CaptureError?.Invoke(this, ex.Message);
        }
    }

    private string GetDirection(string source, string destination)
    {
        var sourceLocal = _localAddresses.Contains(source);
        var destinationLocal = _localAddresses.Contains(destination);

        if (sourceLocal && !destinationLocal)
            return "This PC → Internet";

        if (!sourceLocal && destinationLocal)
            return "Internet → This PC";

        if (sourceLocal && destinationLocal)
            return "This PC / Local";

        if (IsPrivateAddress(source) && !IsPrivateAddress(destination))
            return "Client → Internet";

        if (!IsPrivateAddress(source) && IsPrivateAddress(destination))
            return "Internet → Client";

        if (IsPrivateAddress(source) && IsPrivateAddress(destination))
            return "Client / Local";

        return "Observed";
    }

    private void RefreshLocalAddresses()
    {
        _localAddresses.Clear();

        foreach (var address in NetworkInterface.GetAllNetworkInterfaces()
                     .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                     .Select(a => a.Address.ToString()))
        {
            _localAddresses.Add(address);
        }
    }

    private static bool IsPrivateAddress(string value)
    {
        if (!IPAddress.TryParse(value, out var address))
            return false;

        if (IPAddress.IsLoopback(address))
            return true;

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal;

        var bytes = address.GetAddressBytes();

        return bytes[0] == 10 ||
               (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168) ||
               (bytes[0] == 169 && bytes[1] == 254);
    }

    public void Dispose() => Stop();
}

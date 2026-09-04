using System.Net.NetworkInformation;
using PacketDotNet;
using SharpPcap;
using WifiTraffic.Models;

namespace WifiTraffic.Services;

public sealed class CaptureService : IDisposable
{
    private ICaptureDevice? _device;
    private readonly HashSet<string> _localAddresses;

    public event EventHandler<TrafficRecord>? TrafficObserved;
    public event EventHandler<string>? CaptureError;

    public bool IsRunning => _device is not null;

    public CaptureService()
    {
        _localAddresses = NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<CaptureAdapter> GetAdapters()
    {
        var list = new List<CaptureAdapter>();

        foreach (var device in CaptureDeviceList.Instance)
        {
            list.Add(new CaptureAdapter(
                device.Name,
                device.Name,
                device.Description ?? ""));
        }

        return list;
    }

    public void Start(string adapterId)
    {
        Stop();

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
            return "Outbound";
        if (!sourceLocal && destinationLocal)
            return "Inbound";
        if (sourceLocal && destinationLocal)
            return "Local";

        return "Observed";
    }

    public void Dispose() => Stop();
}

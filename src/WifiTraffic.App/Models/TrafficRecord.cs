namespace WifiTraffic.Models;

public sealed class TrafficRecord
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Adapter { get; set; } = "";
    public string SourceIp { get; set; } = "";
    public string DestinationIp { get; set; } = "";
    public int SourcePort { get; set; }
    public int DestinationPort { get; set; }
    public string Protocol { get; set; } = "";
    public string Direction { get; set; } = "";
    public string Domain { get; set; } = "";
    public int Bytes { get; set; }

    public string Endpoint => DestinationPort > 0 ? $"{DestinationIp}:{DestinationPort}" : DestinationIp;
}

public sealed record CaptureAdapter(
    string Id,
    string Name,
    string Description,
    bool IsGatewayCandidate = false)
{
    public string DisplayName
    {
        get
        {
            var label = string.IsNullOrWhiteSpace(Description) ? Name : Description;
            return IsGatewayCandidate ? $"★ WHOLE NETWORK — {label}" : label;
        }
    }
}

public sealed record OverviewStats(
    long PacketCount,
    long TotalBytes,
    long UniqueDomains,
    long UniqueSources);

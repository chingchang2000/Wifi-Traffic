using System.Text;

namespace WifiTraffic.Services;

public static class ProtocolInspector
{
    public static string TryExtractDomain(byte[]? payload, int sourcePort, int destinationPort)
    {
        if (payload is null || payload.Length == 0)
            return "";

        if (sourcePort == 53 || destinationPort == 53)
        {
            var dns = TryParseDnsQuestion(payload);
            if (!string.IsNullOrWhiteSpace(dns))
                return dns;
        }

        if (sourcePort == 80 || destinationPort == 80 ||
            sourcePort == 8080 || destinationPort == 8080)
        {
            var host = TryParseHttpHost(payload);
            if (!string.IsNullOrWhiteSpace(host))
                return host;
        }

        if (sourcePort == 443 || destinationPort == 443)
        {
            var sni = TryParseTlsSni(payload);
            if (!string.IsNullOrWhiteSpace(sni))
                return sni;
        }

        return "";
    }

    private static string TryParseHttpHost(byte[] payload)
    {
        try
        {
            var text = Encoding.ASCII.GetString(payload, 0, Math.Min(payload.Length, 8192));
            foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                if (line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))
                    return CleanHost(line[5..].Trim());
            }
        }
        catch
        {
        }

        return "";
    }

    private static string TryParseDnsQuestion(byte[] payload)
    {
        try
        {
            if (payload.Length < 13)
                return "";

            var qdCount = (payload[4] << 8) | payload[5];
            if (qdCount < 1)
                return "";

            var offset = 12;
            var labels = new List<string>();

            while (offset < payload.Length)
            {
                var length = payload[offset++];
                if (length == 0)
                    break;

                if ((length & 0xC0) != 0 || offset + length > payload.Length)
                    return "";

                labels.Add(Encoding.ASCII.GetString(payload, offset, length));
                offset += length;
            }

            return CleanHost(string.Join(".", labels));
        }
        catch
        {
            return "";
        }
    }

    private static string TryParseTlsSni(byte[] data)
    {
        try
        {
            if (data.Length < 5 || data[0] != 0x16)
                return "";

            var recordLength = ReadU16(data, 3);
            if (recordLength + 5 > data.Length)
                return "";

            var p = 5;
            if (p + 4 > data.Length || data[p] != 0x01)
                return "";

            p += 4;
            p += 2;
            p += 32;
            if (p >= data.Length)
                return "";

            var sessionIdLength = data[p++];
            p += sessionIdLength;
            if (p + 2 > data.Length)
                return "";

            var cipherLength = ReadU16(data, p);
            p += 2 + cipherLength;
            if (p >= data.Length)
                return "";

            var compressionLength = data[p++];
            p += compressionLength;
            if (p + 2 > data.Length)
                return "";

            var extensionsLength = ReadU16(data, p);
            p += 2;
            var extensionsEnd = Math.Min(data.Length, p + extensionsLength);

            while (p + 4 <= extensionsEnd)
            {
                var type = ReadU16(data, p);
                var length = ReadU16(data, p + 2);
                p += 4;

                if (p + length > extensionsEnd)
                    return "";

                if (type == 0x0000 && length >= 5)
                {
                    var q = p;
                    var listLength = ReadU16(data, q);
                    q += 2;
                    var listEnd = Math.Min(p + length, q + listLength);

                    while (q + 3 <= listEnd)
                    {
                        var nameType = data[q++];
                        var nameLength = ReadU16(data, q);
                        q += 2;

                        if (q + nameLength > listEnd)
                            return "";

                        if (nameType == 0)
                            return CleanHost(Encoding.ASCII.GetString(data, q, nameLength));

                        q += nameLength;
                    }
                }

                p += length;
            }
        }
        catch
        {
        }

        return "";
    }

    private static int ReadU16(byte[] data, int offset) =>
        (data[offset] << 8) | data[offset + 1];

    private static string CleanHost(string host)
    {
        host = host.Trim().TrimEnd('.').ToLowerInvariant();

        var colon = host.LastIndexOf(':');
        if (colon > 0 && host.Count(c => c == ':') == 1)
            host = host[..colon];

        if (host.Length is < 1 or > 253)
            return "";

        return host.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_')
            ? host
            : "";
    }
}

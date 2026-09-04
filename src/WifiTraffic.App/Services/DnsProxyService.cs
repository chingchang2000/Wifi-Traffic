using System.Net;
using System.Net.Sockets;
using WifiTraffic.Models;

namespace WifiTraffic.Services;

public sealed class DnsProxyService : IDisposable
{
    private readonly SemaphoreSlim _concurrency = new(128, 128);
    private CancellationTokenSource? _cts;
    private UdpClient? _udp;
    private TcpListener? _tcp;

    public event EventHandler<TrafficRecord>? DomainObserved;
    public event EventHandler<string>? StatusChanged;

    public bool IsRunning => _cts is not null;

    public Task StartAsync()
    {
        if (IsRunning)
            return Task.CompletedTask;

        var cts = new CancellationTokenSource();

        try
        {
            var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, 53));

            var tcp = new TcpListener(IPAddress.Any, 53);
            tcp.Start(100);

            _cts = cts;
            _udp = udp;
            _tcp = tcp;

            _ = Task.Run(() => UdpLoopAsync(cts.Token));
            _ = Task.Run(() => TcpLoopAsync(cts.Token));

            StatusChanged?.Invoke(this, "DNS sensor is listening on UDP/TCP port 53.");
            return Task.CompletedTask;
        }
        catch
        {
            cts.Dispose();
            _udp?.Dispose();
            _udp = null;

            try { _tcp?.Stop(); } catch { }
            _tcp = null;

            throw;
        }
    }

    public void Stop()
    {
        var cts = _cts;
        _cts = null;

        if (cts is null)
            return;

        try { cts.Cancel(); } catch { }
        try { _udp?.Dispose(); } catch { }
        try { _tcp?.Stop(); } catch { }

        _udp = null;
        _tcp = null;
        cts.Dispose();

        StatusChanged?.Invoke(this, "DNS sensor stopped.");
    }

    private async Task UdpLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var udp = _udp;
                if (udp is null)
                    break;

                var result = await udp.ReceiveAsync(token);
                _ = HandleUdpRequestAsync(result, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"DNS UDP warning: {ex.Message}");
                await Task.Delay(250, token);
            }
        }
    }

    private async Task HandleUdpRequestAsync(UdpReceiveResult request, CancellationToken token)
    {
        await _concurrency.WaitAsync(token);
        try
        {
            var domain = TryParseQuestionName(request.Buffer);
            var response = await ForwardUdpAsync(request.Buffer, token);

            if (response is not null)
            {
                var udp = _udp;
                if (udp is not null)
                    await udp.SendAsync(response, response.Length, request.RemoteEndPoint);
            }

            if (!string.IsNullOrWhiteSpace(domain))
                EmitObservation(request.RemoteEndPoint, domain, request.Buffer.Length + (response?.Length ?? 0));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"DNS request warning: {ex.Message}");
        }
        finally
        {
            _concurrency.Release();
        }
    }

    private async Task TcpLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var listener = _tcp;
                if (listener is null)
                    break;

                var client = await listener.AcceptTcpClientAsync(token);
                _ = HandleTcpClientAsync(client, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"DNS TCP warning: {ex.Message}");
                await Task.Delay(250, token);
            }
        }
    }

    private async Task HandleTcpClientAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            await _concurrency.WaitAsync(token);
            try
            {
                var remote = client.Client.RemoteEndPoint as IPEndPoint;
                if (remote is null)
                    return;

                var stream = client.GetStream();

                var lengthBytes = new byte[2];
                await stream.ReadExactlyAsync(lengthBytes, token);
                var length = (lengthBytes[0] << 8) | lengthBytes[1];

                if (length <= 0 || length > 65535)
                    return;

                var query = new byte[length];
                await stream.ReadExactlyAsync(query, token);

                var domain = TryParseQuestionName(query);
                var response = await ForwardTcpAsync(query, token);

                if (response is not null)
                {
                    var responseLength = new[]
                    {
                        (byte)(response.Length >> 8),
                        (byte)(response.Length & 0xFF)
                    };

                    await stream.WriteAsync(responseLength, token);
                    await stream.WriteAsync(response, token);
                    await stream.FlushAsync(token);
                }

                if (!string.IsNullOrWhiteSpace(domain))
                    EmitObservation(remote, domain, query.Length + (response?.Length ?? 0));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"DNS TCP client warning: {ex.Message}");
            }
            finally
            {
                _concurrency.Release();
            }
        }
    }

    private static async Task<byte[]?> ForwardUdpAsync(byte[] query, CancellationToken token)
    {
        foreach (var resolver in new[] { "1.1.1.1", "8.8.8.8" })
        {
            try
            {
                using var upstream = new UdpClient(AddressFamily.InterNetwork);
                upstream.Connect(IPAddress.Parse(resolver), 53);

                await upstream.SendAsync(query, query.Length);
                var response = await upstream.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(3), token);
                return response.Buffer;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }
        }

        return null;
    }

    private static async Task<byte[]?> ForwardTcpAsync(byte[] query, CancellationToken token)
    {
        foreach (var resolver in new[] { "1.1.1.1", "8.8.8.8" })
        {
            try
            {
                using var upstream = new TcpClient(AddressFamily.InterNetwork);
                await upstream.ConnectAsync(IPAddress.Parse(resolver), 53, token);

                var stream = upstream.GetStream();
                var lengthBytes = new[]
                {
                    (byte)(query.Length >> 8),
                    (byte)(query.Length & 0xFF)
                };

                await stream.WriteAsync(lengthBytes, token);
                await stream.WriteAsync(query, token);
                await stream.FlushAsync(token);

                var responseLengthBytes = new byte[2];
                await stream.ReadExactlyAsync(responseLengthBytes, token);
                var responseLength = (responseLengthBytes[0] << 8) | responseLengthBytes[1];

                if (responseLength <= 0 || responseLength > 65535)
                    return null;

                var response = new byte[responseLength];
                await stream.ReadExactlyAsync(response, token);
                return response;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }
        }

        return null;
    }

    private void EmitObservation(IPEndPoint remote, string domain, int bytes)
    {
        DomainObserved?.Invoke(this, new TrafficRecord
        {
            Timestamp = DateTime.Now,
            Adapter = "Router DNS Mode",
            SourceIp = remote.Address.ToString(),
            DestinationIp = "DNS resolver",
            SourcePort = remote.Port,
            DestinationPort = 53,
            Protocol = "DNS",
            Direction = "Device → DNS",
            Domain = domain,
            Bytes = bytes
        });
    }

    private static string TryParseQuestionName(byte[] packet)
    {
        try
        {
            if (packet.Length < 13)
                return "";

            var questionCount = (packet[4] << 8) | packet[5];
            if (questionCount < 1)
                return "";

            var offset = 12;
            var labels = new List<string>();

            while (offset < packet.Length)
            {
                var length = packet[offset++];

                if (length == 0)
                    break;

                if ((length & 0xC0) != 0 || offset + length > packet.Length)
                    return "";

                labels.Add(System.Text.Encoding.ASCII.GetString(packet, offset, length));
                offset += length;
            }

            var host = string.Join(".", labels).Trim().TrimEnd('.').ToLowerInvariant();

            if (host.Length is < 1 or > 253)
                return "";

            return host.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_')
                ? host
                : "";
        }
        catch
        {
            return "";
        }
    }

    public void Dispose() => Stop();
}

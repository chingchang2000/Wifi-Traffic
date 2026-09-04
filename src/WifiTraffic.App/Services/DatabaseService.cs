using System.IO;
using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using WifiTraffic.Models;

namespace WifiTraffic.Services;

public sealed class DatabaseService
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly Channel<TrafficRecord> _queue = Channel.CreateUnbounded<TrafficRecord>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    public string DatabasePath { get; }

    public DatabaseService()
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WifiTraffic");

        Directory.CreateDirectory(dataDir);
        DatabasePath = Path.Combine(dataDir, "wifi-traffic.db");
        _connectionString = $"Data Source={DatabasePath};Cache=Shared";

        _ = Task.Run(WriterLoopAsync);
    }

    public async Task InitializeAsync()
    {
        await _writeGate.WaitAsync();
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;
                CREATE TABLE IF NOT EXISTS traffic (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    timestamp TEXT NOT NULL,
                    adapter TEXT NOT NULL,
                    source_ip TEXT NOT NULL,
                    destination_ip TEXT NOT NULL,
                    source_port INTEGER NOT NULL,
                    destination_port INTEGER NOT NULL,
                    protocol TEXT NOT NULL,
                    direction TEXT NOT NULL,
                    domain TEXT NOT NULL,
                    bytes INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_traffic_timestamp ON traffic(timestamp DESC);
                CREATE INDEX IF NOT EXISTS idx_traffic_domain ON traffic(domain);
                CREATE INDEX IF NOT EXISTS idx_traffic_source_ip ON traffic(source_ip);
                """;
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public bool Enqueue(TrafficRecord record) => _queue.Writer.TryWrite(record);

    private async Task WriterLoopAsync()
    {
        var batch = new List<TrafficRecord>(200);

        while (await _queue.Reader.WaitToReadAsync())
        {
            batch.Clear();

            while (batch.Count < 200 && _queue.Reader.TryRead(out var record))
                batch.Add(record);

            if (batch.Count == 0)
                continue;

            try
            {
                await InsertBatchAsync(batch);
            }
            catch
            {
                // Capture must remain responsive even if disk/database access temporarily fails.
            }
        }
    }

    private async Task InsertBatchAsync(IReadOnlyList<TrafficRecord> records)
    {
        await _writeGate.WaitAsync();
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO traffic
                (timestamp, adapter, source_ip, destination_ip, source_port, destination_port, protocol, direction, domain, bytes)
                VALUES
                ($timestamp, $adapter, $source, $destination, $sourcePort, $destinationPort, $protocol, $direction, $domain, $bytes);
                """;

            var timestamp = command.Parameters.Add("$timestamp", SqliteType.Text);
            var adapter = command.Parameters.Add("$adapter", SqliteType.Text);
            var source = command.Parameters.Add("$source", SqliteType.Text);
            var destination = command.Parameters.Add("$destination", SqliteType.Text);
            var sourcePort = command.Parameters.Add("$sourcePort", SqliteType.Integer);
            var destinationPort = command.Parameters.Add("$destinationPort", SqliteType.Integer);
            var protocol = command.Parameters.Add("$protocol", SqliteType.Text);
            var direction = command.Parameters.Add("$direction", SqliteType.Text);
            var domain = command.Parameters.Add("$domain", SqliteType.Text);
            var bytes = command.Parameters.Add("$bytes", SqliteType.Integer);

            foreach (var record in records)
            {
                timestamp.Value = record.Timestamp.ToUniversalTime().ToString("O");
                adapter.Value = record.Adapter;
                source.Value = record.SourceIp;
                destination.Value = record.DestinationIp;
                sourcePort.Value = record.SourcePort;
                destinationPort.Value = record.DestinationPort;
                protocol.Value = record.Protocol;
                direction.Value = record.Direction;
                domain.Value = record.Domain;
                bytes.Value = record.Bytes;

                await command.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<OverviewStats> GetStatsAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                COUNT(*),
                COALESCE(SUM(bytes), 0),
                COUNT(DISTINCT NULLIF(domain, '')),
                COUNT(DISTINCT source_ip)
            FROM traffic;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return new OverviewStats(0, 0, 0, 0);

        return new OverviewStats(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3));
    }

    public async Task<List<(string Domain, long Hits, long Bytes)>> GetTopDomainsAsync(int limit = 50)
    {
        var result = new List<(string, long, long)>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT domain, COUNT(*) AS hits, SUM(bytes) AS bytes
            FROM traffic
            WHERE domain <> ''
            GROUP BY domain
            ORDER BY hits DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add((reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2)));

        return result;
    }

    public async Task<List<TrafficRecord>> GetRecentAsync(int limit = 500)
    {
        var result = new List<TrafficRecord>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, timestamp, adapter, source_ip, destination_ip, source_port,
                   destination_port, protocol, direction, domain, bytes
            FROM traffic
            ORDER BY id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new TrafficRecord
            {
                Id = reader.GetInt64(0),
                Timestamp = DateTime.Parse(reader.GetString(1)).ToLocalTime(),
                Adapter = reader.GetString(2),
                SourceIp = reader.GetString(3),
                DestinationIp = reader.GetString(4),
                SourcePort = reader.GetInt32(5),
                DestinationPort = reader.GetInt32(6),
                Protocol = reader.GetString(7),
                Direction = reader.GetString(8),
                Domain = reader.GetString(9),
                Bytes = reader.GetInt32(10)
            });
        }

        return result;
    }

    public async Task ClearAsync()
    {
        await _writeGate.WaitAsync();
        try
        {
            while (_queue.Reader.TryRead(out _))
            {
            }

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM traffic;";
            await delete.ExecuteNonQueryAsync();

            var vacuum = connection.CreateCommand();
            vacuum.CommandText = "VACUUM;";
            await vacuum.ExecuteNonQueryAsync();
        }
        finally
        {
            _writeGate.Release();
        }
    }
}

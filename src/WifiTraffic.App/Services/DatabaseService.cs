using Microsoft.Data.Sqlite;
using WifiTraffic.Models;

namespace WifiTraffic.Services;

public sealed class DatabaseService
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string DatabasePath { get; }

    public DatabaseService()
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WifiTraffic");

        Directory.CreateDirectory(dataDir);
        DatabasePath = Path.Combine(dataDir, "wifi-traffic.db");
        _connectionString = $"Data Source={DatabasePath};Cache=Shared";
    }

    public async Task InitializeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode=WAL;
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
            _gate.Release();
        }
    }

    public async Task InsertAsync(TrafficRecord record)
    {
        await _gate.WaitAsync();
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO traffic
                (timestamp, adapter, source_ip, destination_ip, source_port, destination_port, protocol, direction, domain, bytes)
                VALUES
                ($timestamp, $adapter, $source, $destination, $sourcePort, $destinationPort, $protocol, $direction, $domain, $bytes);
                """;

            command.Parameters.AddWithValue("$timestamp", record.Timestamp.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$adapter", record.Adapter);
            command.Parameters.AddWithValue("$source", record.SourceIp);
            command.Parameters.AddWithValue("$destination", record.DestinationIp);
            command.Parameters.AddWithValue("$sourcePort", record.SourcePort);
            command.Parameters.AddWithValue("$destinationPort", record.DestinationPort);
            command.Parameters.AddWithValue("$protocol", record.Protocol);
            command.Parameters.AddWithValue("$direction", record.Direction);
            command.Parameters.AddWithValue("$domain", record.Domain);
            command.Parameters.AddWithValue("$bytes", record.Bytes);

            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            _gate.Release();
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
        await _gate.WaitAsync();
        try
        {
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
            _gate.Release();
        }
    }
}

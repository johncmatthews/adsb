using System.Data.Common;
using System.Data.SQLite;
using System.Globalization;
using System.Threading.Channels;
using Adsb.Server.Contracts;
using Microsoft.Extensions.Options;

namespace Adsb.Server.Services;

/// <summary>
/// Buffers and archives every published aircraft telemetry event in SQLite for later analysis.
/// </summary>
public sealed class TrafficLogStore : BackgroundService
{
    private const int QueueCapacity = 10_000;
    private const int BatchSize = 250;

    private readonly TrafficLoggingOptions options;
    private readonly CaptureStatusService status;
    private readonly ILogger<TrafficLogStore> logger;
    private readonly Channel<AircraftTelemetryEvent> queue;
    private readonly SemaphoreSlim initializeLock = new(1, 1);
    private int initialized;

    /// <summary>
    /// Creates an all-traffic archive store using the configured enable switch and SQLite path.
    /// </summary>
    public TrafficLogStore(
        IOptions<AdsbServerOptions> options,
        CaptureStatusService status,
        ILogger<TrafficLogStore> logger)
    {
        this.options = options.Value.TrafficLogging;
        this.status = status;
        this.logger = logger;
        DatabasePath = Path.GetFullPath(this.options.DatabasePath);
        queue = Channel.CreateBounded<AircraftTelemetryEvent>(
            new BoundedChannelOptions(QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true
            });
    }

    /// <summary>
    /// Whether incoming all-aircraft telemetry is configured to be archived.
    /// </summary>
    public bool Enabled => options.Enabled;

    /// <summary>
    /// Absolute SQLite database path used by the all-traffic archive.
    /// </summary>
    public string DatabasePath { get; }

    /// <summary>
    /// Queues one telemetry event for durable storage, applying backpressure rather than discarding events when full.
    /// </summary>
    /// <returns>True when logging is enabled and the event was queued; otherwise false.</returns>
    public async ValueTask<bool> EnqueueAsync(
        AircraftTelemetryEvent telemetry,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return false;
        }

        await queue.Writer.WriteAsync(telemetry, cancellationToken);
        return true;
    }

    /// <summary>
    /// Returns archived telemetry in chronological order using optional aircraft and time filters.
    /// </summary>
    public async Task<IReadOnlyList<AircraftTelemetryEvent>> QueryEventsAsync(
        TrafficQuery query,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        using var connection = OpenConnection();
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();

        var where = new List<string>();
        AddFilter(command, where, "icao", "$icao", query.Icao);
        AddFilter(command, where, "tail_number", "$tail_number", query.TailNumber);
        AddFilter(command, where, "flight_number", "$flight_number", query.FlightNumber);
        AddFilter(command, where, "callsign", "$callsign", query.Callsign);

        if (query.FromUtc.HasValue)
        {
            where.Add("received_at_utc >= $from_utc");
            Add(command, "$from_utc", query.FromUtc.Value.UtcDateTime.ToString("O"));
        }

        if (query.ToUtc.HasValue)
        {
            where.Add("received_at_utc <= $to_utc");
            Add(command, "$to_utc", query.ToUtc.Value.UtcDateTime.ToString("O"));
        }

        Add(command, "$limit", Math.Clamp(query.Limit, 1, 100_000));
        command.CommandText =
            $"""
             SELECT
                 received_at_utc,
                 icao,
                 tail_number,
                 flight_number,
                 callsign,
                 downlink_format,
                 type_code,
                 description,
                 altitude_feet,
                 gnss_height_meters,
                 latitude,
                 longitude,
                 ground_speed_knots,
                 track_degrees,
                 vertical_rate_fpm,
                 heading_degrees,
                 airspeed_knots,
                 airspeed_type,
                 signal_db,
                 crc_ok,
                 crc_remainder,
                 raw_hex
             FROM aircraft_telemetry
             {(where.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", where))}
             ORDER BY received_at_utc ASC
             LIMIT $limit;
             """;

        var events = new List<AircraftTelemetryEvent>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(ReadTelemetry(reader));
        }

        return events;
    }

    /// <summary>
    /// Returns archive size, aircraft count, and time coverage without loading individual telemetry rows.
    /// </summary>
    public async Task<TrafficArchiveStatsDto> GetStatsAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        using var connection = OpenConnection();
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                COUNT(*),
                COUNT(DISTINCT icao),
                MIN(received_at_utc),
                MAX(received_at_utc)
            FROM aircraft_telemetry;
            """;

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new TrafficArchiveStatsDto(
            Enabled,
            reader.GetInt64(0),
            reader.GetInt64(1),
            ReadTimestamp(reader, 2),
            ReadTimestamp(reader, 3));
    }

    /// <summary>
    /// Initializes the archive before the host starts later services that can enqueue telemetry.
    /// </summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (Enabled)
        {
            await EnsureInitializedAsync(cancellationToken);
        }

        await base.StartAsync(cancellationToken);
    }

    /// <summary>
    /// Starts the single archive writer when all-traffic logging is enabled.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Enabled)
        {
            logger.LogInformation("All-aircraft traffic logging is disabled.");
            return;
        }

        await EnsureInitializedAsync(stoppingToken);
        logger.LogInformation("All-aircraft traffic logging enabled -> {DatabasePath}", DatabasePath);

        try
        {
            while (await queue.Reader.WaitToReadAsync(stoppingToken))
            {
                var batch = ReadBatch();
                await WriteBatchAsync(batch, CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            await DrainQueueAsync();
        }
    }

    /// <summary>
    /// Completes the input queue before stopping so pending telemetry can be flushed to SQLite.
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        queue.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// Creates the archive schema and indexes once, allowing API queries even while new logging is disabled.
    /// </summary>
    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref initialized) != 0)
        {
            return;
        }

        await initializeLock.WaitAsync(cancellationToken);
        try
        {
            if (initialized != 0)
            {
                return;
            }

            var directory = Path.GetDirectoryName(DatabasePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var connection = OpenConnection();
            await connection.OpenAsync(cancellationToken);
            await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken);
            await ExecuteAsync(connection, "PRAGMA synchronous = NORMAL;", cancellationToken);
            await ExecuteAsync(connection, "PRAGMA busy_timeout = 5000;", cancellationToken);
            await ExecuteAsync(
                connection,
                """
                CREATE TABLE IF NOT EXISTS aircraft_telemetry (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    received_at_utc TEXT NOT NULL,
                    icao TEXT NULL,
                    tail_number TEXT NULL,
                    flight_number TEXT NULL,
                    callsign TEXT NULL,
                    downlink_format INTEGER NOT NULL,
                    type_code INTEGER NULL,
                    description TEXT NOT NULL,
                    altitude_feet INTEGER NULL,
                    gnss_height_meters INTEGER NULL,
                    latitude REAL NULL,
                    longitude REAL NULL,
                    ground_speed_knots INTEGER NULL,
                    track_degrees REAL NULL,
                    vertical_rate_fpm INTEGER NULL,
                    heading_degrees INTEGER NULL,
                    airspeed_knots INTEGER NULL,
                    airspeed_type TEXT NULL,
                    signal_db REAL NOT NULL,
                    crc_ok INTEGER NOT NULL,
                    crc_remainder TEXT NOT NULL,
                    raw_hex TEXT NOT NULL,
                    created_at_utc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
                );
                """,
                cancellationToken);
            await ExecuteAsync(
                connection,
                "CREATE INDEX IF NOT EXISTS ix_aircraft_telemetry_received_at ON aircraft_telemetry(received_at_utc);",
                cancellationToken);
            await ExecuteAsync(
                connection,
                "CREATE INDEX IF NOT EXISTS ix_aircraft_telemetry_icao_time ON aircraft_telemetry(icao, received_at_utc);",
                cancellationToken);
            await ExecuteAsync(
                connection,
                "CREATE INDEX IF NOT EXISTS ix_aircraft_telemetry_tail_time ON aircraft_telemetry(tail_number, received_at_utc);",
                cancellationToken);
            await ExecuteAsync(
                connection,
                "CREATE INDEX IF NOT EXISTS ix_aircraft_telemetry_flight_time ON aircraft_telemetry(flight_number, received_at_utc);",
                cancellationToken);
            Volatile.Write(ref initialized, 1);
        }
        finally
        {
            initializeLock.Release();
        }
    }

    /// <summary>
    /// Reads up to one transaction-sized batch from the in-memory queue.
    /// </summary>
    private List<AircraftTelemetryEvent> ReadBatch()
    {
        var batch = new List<AircraftTelemetryEvent>(BatchSize);
        while (batch.Count < BatchSize && queue.Reader.TryRead(out var telemetry))
        {
            batch.Add(telemetry);
        }

        return batch;
    }

    /// <summary>
    /// Flushes all queued events during graceful server shutdown.
    /// </summary>
    private async Task DrainQueueAsync()
    {
        while (queue.Reader.TryPeek(out _))
        {
            var batch = ReadBatch();
            await WriteBatchAsync(batch, CancellationToken.None);
        }
    }

    /// <summary>
    /// Writes one telemetry batch in a SQLite transaction and updates the durable event counter after commit.
    /// </summary>
    private async Task WriteBatchAsync(
        IReadOnlyList<AircraftTelemetryEvent> batch,
        CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return;
        }

        using var connection = OpenConnection();
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        foreach (var telemetry in batch)
        {
            using var command = CreateInsertCommand(connection, transaction, telemetry);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
        status.RecordTrafficEvents(batch.Count);
    }

    /// <summary>
    /// Creates a parameterized insert command for one normalized telemetry event.
    /// </summary>
    private static SQLiteCommand CreateInsertCommand(
        SQLiteConnection connection,
        SQLiteTransaction transaction,
        AircraftTelemetryEvent telemetry)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO aircraft_telemetry (
                received_at_utc,
                icao,
                tail_number,
                flight_number,
                callsign,
                downlink_format,
                type_code,
                description,
                altitude_feet,
                gnss_height_meters,
                latitude,
                longitude,
                ground_speed_knots,
                track_degrees,
                vertical_rate_fpm,
                heading_degrees,
                airspeed_knots,
                airspeed_type,
                signal_db,
                crc_ok,
                crc_remainder,
                raw_hex
            ) VALUES (
                $received_at_utc,
                $icao,
                $tail_number,
                $flight_number,
                $callsign,
                $downlink_format,
                $type_code,
                $description,
                $altitude_feet,
                $gnss_height_meters,
                $latitude,
                $longitude,
                $ground_speed_knots,
                $track_degrees,
                $vertical_rate_fpm,
                $heading_degrees,
                $airspeed_knots,
                $airspeed_type,
                $signal_db,
                $crc_ok,
                $crc_remainder,
                $raw_hex
            );
            """;

        Add(command, "$received_at_utc", telemetry.ReceivedAtUtc.UtcDateTime.ToString("O"));
        Add(command, "$icao", telemetry.Icao);
        Add(command, "$tail_number", telemetry.TailNumber);
        Add(command, "$flight_number", telemetry.FlightNumber);
        Add(command, "$callsign", telemetry.Callsign);
        Add(command, "$downlink_format", telemetry.DownlinkFormat);
        Add(command, "$type_code", telemetry.TypeCode);
        Add(command, "$description", telemetry.Description);
        Add(command, "$altitude_feet", telemetry.AltitudeFeet);
        Add(command, "$gnss_height_meters", telemetry.GnssHeightMeters);
        Add(command, "$latitude", telemetry.Latitude);
        Add(command, "$longitude", telemetry.Longitude);
        Add(command, "$ground_speed_knots", telemetry.GroundSpeedKnots);
        Add(command, "$track_degrees", telemetry.TrackDegrees);
        Add(command, "$vertical_rate_fpm", telemetry.VerticalRateFeetPerMinute);
        Add(command, "$heading_degrees", telemetry.HeadingDegrees);
        Add(command, "$airspeed_knots", telemetry.AirspeedKnots);
        Add(command, "$airspeed_type", telemetry.AirspeedType);
        Add(command, "$signal_db", telemetry.SignalDb);
        Add(command, "$crc_ok", telemetry.CrcOk ? 1 : 0);
        Add(command, "$crc_remainder", telemetry.CrcRemainder);
        Add(command, "$raw_hex", telemetry.RawHex);
        return command;
    }

    /// <summary>
    /// Opens a new SQLite connection for archive writes and queries.
    /// </summary>
    private SQLiteConnection OpenConnection() => new($"Data Source={DatabasePath};Version=3;");

    /// <summary>
    /// Rehydrates one archive row into the normalized telemetry contract.
    /// </summary>
    private static AircraftTelemetryEvent ReadTelemetry(DbDataReader reader) =>
        new(
            DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture),
            ReadString(reader, 1),
            ReadString(reader, 2),
            ReadString(reader, 3),
            ReadString(reader, 4),
            reader.GetInt32(5),
            ReadInt(reader, 6),
            reader.GetString(7),
            ReadInt(reader, 8),
            ReadInt(reader, 9),
            ReadDouble(reader, 10),
            ReadDouble(reader, 11),
            ReadInt(reader, 12),
            ReadDouble(reader, 13),
            ReadInt(reader, 14),
            ReadInt(reader, 15),
            ReadInt(reader, 16),
            ReadString(reader, 17),
            reader.GetDouble(18),
            reader.GetInt32(19) != 0,
            reader.GetString(20),
            reader.GetString(21));

    /// <summary>
    /// Executes a schema or SQLite pragma command during archive initialization.
    /// </summary>
    private static async Task ExecuteAsync(
        SQLiteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Adds an equality predicate when an optional archive query value is present.
    /// </summary>
    private static void AddFilter(
        SQLiteCommand command,
        List<string> where,
        string column,
        string parameter,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        where.Add($"{column} = {parameter}");
        Add(command, parameter, value.Trim().ToUpperInvariant());
    }

    /// <summary>
    /// Adds a SQLite parameter, translating null reference values into database NULL.
    /// </summary>
    private static void Add(SQLiteCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    /// <summary>
    /// Reads a nullable text column from a data reader.
    /// </summary>
    private static string? ReadString(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    /// <summary>
    /// Reads a nullable integer column from a data reader.
    /// </summary>
    private static int? ReadInt(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    /// <summary>
    /// Reads a nullable floating-point column from a data reader.
    /// </summary>
    private static double? ReadDouble(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);

    /// <summary>
    /// Reads a nullable ISO 8601 timestamp from a data reader.
    /// </summary>
    private static DateTimeOffset? ReadTimestamp(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture);
}

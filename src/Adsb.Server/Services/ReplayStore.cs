using System.Globalization;
using System.Data.Common;
using Adsb.Server.Contracts;
using Microsoft.Extensions.Options;
using System.Data.SQLite;

namespace Adsb.Server.Services;

/// <summary>
/// SQLite replay store for telemetry events captured because they matched the server watchlist.
/// </summary>
public sealed class ReplayStore
{
    private readonly string databasePath;
    private bool initialized;
    private readonly SemaphoreSlim initializeLock = new(1, 1);

    /// <summary>
    /// Creates a replay store for the configured SQLite database path.
    /// </summary>
    public ReplayStore(IOptions<AdsbServerOptions> options)
    {
        databasePath = Path.GetFullPath(options.Value.Replay.DatabasePath);
    }

    /// <summary>
    /// Absolute SQLite database path used by the replay store.
    /// </summary>
    public string DatabasePath => databasePath;

    /// <summary>
    /// Writes a matched telemetry event and the watchlist identifier that caused it to be retained.
    /// </summary>
    public async Task RecordAsync(AircraftTelemetryEvent telemetry, WatchlistMatchDto match, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        using var connection = OpenConnection();
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO watchlist_telemetry (
                received_at_utc,
                watchlist_label,
                matched_identifier_type,
                matched_identifier,
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
                $watchlist_label,
                $matched_identifier_type,
                $matched_identifier,
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
        Add(command, "$watchlist_label", match.Label);
        Add(command, "$matched_identifier_type", match.IdentifierType);
        Add(command, "$matched_identifier", match.IdentifierValue);
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

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Returns recent replay session summaries grouped by watchlist label and aircraft identifiers.
    /// </summary>
    public async Task<IReadOnlyList<ReplaySessionDto>> GetSessionsAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        using var connection = OpenConnection();
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                COALESCE(watchlist_label, ''),
                COALESCE(icao, ''),
                COALESCE(tail_number, ''),
                COALESCE(flight_number, ''),
                COALESCE(callsign, ''),
                MIN(received_at_utc),
                MAX(received_at_utc),
                COUNT(*)
            FROM watchlist_telemetry
            GROUP BY watchlist_label, icao, tail_number, flight_number, callsign
            ORDER BY MAX(received_at_utc) DESC
            LIMIT 200;
            """;

        var sessions = new List<ReplaySessionDto>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var label = NullIfEmpty(reader.GetString(0));
            var icao = NullIfEmpty(reader.GetString(1));
            var tail = NullIfEmpty(reader.GetString(2));
            var flight = NullIfEmpty(reader.GetString(3));
            var callsign = NullIfEmpty(reader.GetString(4));
            var start = DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture);
            var end = DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture);
            var count = reader.GetInt64(7);
            var key = string.Join(
                ":",
                new[] { label, icao, tail, flight, callsign }.Where(value => !string.IsNullOrWhiteSpace(value)));

            sessions.Add(new ReplaySessionDto(key, label, icao, tail, flight, callsign, start, end, count));
        }

        return sessions;
    }

    /// <summary>
    /// Queries stored watchlist telemetry in chronological order using optional identity and UTC time filters.
    /// </summary>
    /// <remarks>
    /// The query limit is clamped to protect the server from accidentally returning an unbounded replay result.
    /// </remarks>
    public async Task<IReadOnlyList<AircraftTelemetryEvent>> QueryEventsAsync(
        ReplayQuery query,
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

        var limit = Math.Clamp(query.Limit, 1, 100_000);
        Add(command, "$limit", limit);
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
             FROM watchlist_telemetry
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
    /// Creates the replay database schema and indexes once, using a lock so concurrent requests share initialization.
    /// </summary>
    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (initialized)
        {
            return;
        }

        await initializeLock.WaitAsync(cancellationToken);
        try
        {
            if (initialized)
            {
                return;
            }

            var directory = Path.GetDirectoryName(databasePath);
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
                CREATE TABLE IF NOT EXISTS watchlist_telemetry (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    received_at_utc TEXT NOT NULL,
                    watchlist_label TEXT NULL,
                    matched_identifier_type TEXT NOT NULL,
                    matched_identifier TEXT NULL,
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

            await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS ix_watchlist_telemetry_received_at ON watchlist_telemetry(received_at_utc);", cancellationToken);
            await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS ix_watchlist_telemetry_icao ON watchlist_telemetry(icao);", cancellationToken);
            await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS ix_watchlist_telemetry_tail_number ON watchlist_telemetry(tail_number);", cancellationToken);
            await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS ix_watchlist_telemetry_flight_number ON watchlist_telemetry(flight_number);", cancellationToken);
            initialized = true;
        }
        finally
        {
            initializeLock.Release();
        }
    }

    /// <summary>
    /// Opens a new SQLite connection; callers own the connection lifetime for each operation.
    /// </summary>
    private SQLiteConnection OpenConnection() => new($"Data Source={databasePath};Version=3;");

    /// <summary>
    /// Rehydrates one database row into the normalized telemetry contract returned by replay APIs.
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
    /// Executes a schema or SQLite pragma command during database initialization.
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
    /// Adds an equality predicate and SQLite parameter when an optional replay filter value is present.
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
    /// Converts grouped empty strings from SQL COALESCE expressions back to null values.
    /// </summary>
    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

using Adsb.Decoding;
using System.Data.SQLite;

namespace Adsb.Tracking;

public sealed class WatchlistTelemetryRecorder : IDisposable
{
    private readonly AircraftWatchlist watchlist;
    private readonly SQLiteConnection? connection;

    private WatchlistTelemetryRecorder(AircraftWatchlist watchlist, SQLiteConnection? connection, string? databasePath)
    {
        this.watchlist = watchlist;
        this.connection = connection;
        DatabasePath = databasePath;
    }

    public bool Enabled => connection is not null;
    public string? DatabasePath { get; }
    public int WatchlistCount => watchlist.Count;

    public static WatchlistTelemetryRecorder Open(string? watchlistPath, string databasePath)
    {
        if (string.IsNullOrWhiteSpace(watchlistPath))
        {
            return new WatchlistTelemetryRecorder(AircraftWatchlist.Empty, null, null);
        }

        var watchlist = AircraftWatchlist.Load(watchlistPath);
        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connection = new SQLiteConnection($"Data Source={fullPath};Version=3;");

        connection.Open();
        Initialize(connection);
        return new WatchlistTelemetryRecorder(watchlist, connection, fullPath);
    }

    public bool TryRecord(ModeSMessage message, AircraftSnapshot? snapshot)
    {
        if (connection is null)
        {
            return false;
        }

        var identity = new AircraftIdentityTelemetry(
            message.Icao ?? snapshot?.Icao,
            snapshot?.Callsign ?? message.Callsign,
            snapshot?.TailNumber,
            snapshot?.FlightNumber);

        if (!watchlist.TryMatch(identity, out var match))
        {
            return false;
        }

        Insert(message, snapshot, match);
        return true;
    }

    public void Dispose()
    {
        connection?.Dispose();
    }

    private static void Initialize(SQLiteConnection connection)
    {
        Execute(connection, "PRAGMA journal_mode = WAL;");
        Execute(connection, "PRAGMA synchronous = NORMAL;");
        Execute(connection, "PRAGMA busy_timeout = 5000;");

        Execute(
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
            """);

        Execute(
            connection,
            "CREATE INDEX IF NOT EXISTS ix_watchlist_telemetry_received_at ON watchlist_telemetry(received_at_utc);");
        Execute(
            connection,
            "CREATE INDEX IF NOT EXISTS ix_watchlist_telemetry_icao ON watchlist_telemetry(icao);");
        Execute(
            connection,
            "CREATE INDEX IF NOT EXISTS ix_watchlist_telemetry_tail_number ON watchlist_telemetry(tail_number);");
        Execute(
            connection,
            "CREATE INDEX IF NOT EXISTS ix_watchlist_telemetry_flight_number ON watchlist_telemetry(flight_number);");
    }

    private void Insert(ModeSMessage message, AircraftSnapshot? snapshot, WatchlistMatch match)
    {
        using var command = connection!.CreateCommand();
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

        var velocity = message.Velocity;
        Add(command, "$received_at_utc", message.ReceivedAt.UtcDateTime.ToString("O"));
        Add(command, "$watchlist_label", match.Label);
        Add(command, "$matched_identifier_type", match.IdentifierType);
        Add(command, "$matched_identifier", match.IdentifierValue);
        Add(command, "$icao", message.Icao ?? snapshot?.Icao);
        Add(command, "$tail_number", snapshot?.TailNumber);
        Add(command, "$flight_number", snapshot?.FlightNumber);
        Add(command, "$callsign", message.Callsign ?? snapshot?.Callsign);
        Add(command, "$downlink_format", message.DownlinkFormat);
        Add(command, "$type_code", message.TypeCode);
        Add(command, "$description", message.Description);
        Add(command, "$altitude_feet", message.AltitudeFeet ?? snapshot?.AltitudeFeet);
        Add(command, "$gnss_height_meters", message.GnssHeightMeters ?? snapshot?.GnssHeightMeters);
        Add(command, "$latitude", snapshot?.Latitude);
        Add(command, "$longitude", snapshot?.Longitude);
        Add(command, "$ground_speed_knots", velocity?.GroundSpeedKnots ?? snapshot?.GroundSpeedKnots);
        Add(command, "$track_degrees", velocity?.TrackDegrees ?? snapshot?.TrackDegrees);
        Add(command, "$vertical_rate_fpm", velocity?.VerticalRateFeetPerMinute ?? snapshot?.VerticalRateFeetPerMinute);
        Add(command, "$heading_degrees", velocity?.HeadingDegrees);
        Add(command, "$airspeed_knots", velocity?.AirspeedKnots);
        Add(command, "$airspeed_type", velocity?.AirspeedType);
        Add(command, "$signal_db", message.SignalDb);
        Add(command, "$crc_ok", message.CrcOk ? 1 : 0);
        Add(command, "$crc_remainder", message.CrcRemainder.ToString("X6"));
        Add(command, "$raw_hex", message.RawHex);

        command.ExecuteNonQuery();
    }

    private static void Execute(SQLiteConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    private static void Add(SQLiteCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }
}

namespace Adsb.Server.Contracts;

/// <summary>
/// Normalized aircraft telemetry event published by the server and stored for watchlist replay.
/// </summary>
/// <param name="ReceivedAtUtc">UTC timestamp assigned when the frame was received.</param>
/// <param name="Icao">ICAO 24-bit address, when known.</param>
/// <param name="TailNumber">Resolved aircraft registration, when known.</param>
/// <param name="FlightNumber">Operational flight number derived from the ADS-B callsign, when known.</param>
/// <param name="Callsign">Raw ADS-B callsign or flight ID, when transmitted.</param>
/// <param name="DownlinkFormat">Mode S downlink format.</param>
/// <param name="TypeCode">ADS-B extended squitter type code, when present.</param>
/// <param name="Description">Human-readable message family.</param>
/// <param name="AltitudeFeet">Barometric altitude in feet, when known.</param>
/// <param name="GnssHeightMeters">GNSS height in meters, when known.</param>
/// <param name="Latitude">Decoded latitude in degrees, when known.</param>
/// <param name="Longitude">Decoded longitude in degrees, when known.</param>
/// <param name="GroundSpeedKnots">Ground speed in knots, when known.</param>
/// <param name="TrackDegrees">Track over ground in degrees, when known.</param>
/// <param name="VerticalRateFeetPerMinute">Vertical rate in feet per minute, when known.</param>
/// <param name="HeadingDegrees">Aircraft heading in degrees for heading/airspeed messages.</param>
/// <param name="AirspeedKnots">Indicated or true airspeed in knots, when known.</param>
/// <param name="AirspeedType">Airspeed source label, usually IAS or TAS.</param>
/// <param name="SignalDb">Estimated signal level in decibels above the local sample floor.</param>
/// <param name="CrcOk">Whether the Mode S CRC/parity check passed.</param>
/// <param name="CrcRemainder">Computed 24-bit CRC/parity remainder as uppercase hexadecimal text.</param>
/// <param name="RawHex">Original Mode S frame bytes as uppercase hexadecimal text.</param>
public sealed record AircraftTelemetryEvent(
    DateTimeOffset ReceivedAtUtc,
    string? Icao,
    string? TailNumber,
    string? FlightNumber,
    string? Callsign,
    int DownlinkFormat,
    int? TypeCode,
    string Description,
    int? AltitudeFeet,
    int? GnssHeightMeters,
    double? Latitude,
    double? Longitude,
    int? GroundSpeedKnots,
    double? TrackDegrees,
    int? VerticalRateFeetPerMinute,
    int? HeadingDegrees,
    int? AirspeedKnots,
    string? AirspeedType,
    double SignalDb,
    bool CrcOk,
    string CrcRemainder,
    string RawHex);

/// <summary>
/// SignalR event emitted when live telemetry matches a configured watchlist entry.
/// </summary>
/// <param name="ReceivedAtUtc">UTC timestamp of the telemetry that matched.</param>
/// <param name="WatchlistLabel">Optional user label from the matching watchlist entry.</param>
/// <param name="IdentifierType">Identifier class that matched, such as icao, tail, flight, or callsign.</param>
/// <param name="IdentifierValue">Observed identifier value that matched the watchlist.</param>
/// <param name="Telemetry">Full telemetry event that was recorded for replay.</param>
public sealed record WatchlistMatchEvent(
    DateTimeOffset ReceivedAtUtc,
    string? WatchlistLabel,
    string IdentifierType,
    string? IdentifierValue,
    AircraftTelemetryEvent Telemetry);

/// <summary>
/// Capture health and throughput counters exposed over REST and SignalR.
/// </summary>
/// <param name="StartedAtUtc">UTC time when the server process created the status tracker.</param>
/// <param name="CaptureEnabled">Whether RTL-SDR capture is enabled in configuration.</param>
/// <param name="DeviceConnected">Whether the capture service currently has an open RTL-SDR device.</param>
/// <param name="FramesDecoded">Number of frames decoded from the sample stream.</param>
/// <param name="EventsPublished">Number of telemetry events published after filtering.</param>
/// <param name="WatchlistEventsRecorded">Number of matched watchlist events written to replay storage.</param>
/// <param name="LastEventAtUtc">Receive time of the latest published telemetry event.</param>
/// <param name="LastError">Most recent capture error message, if any.</param>
public sealed record ServerStatusDto(
    DateTimeOffset StartedAtUtc,
    bool CaptureEnabled,
    bool DeviceConnected,
    long FramesDecoded,
    long EventsPublished,
    long WatchlistEventsRecorded,
    DateTimeOffset? LastEventAtUtc,
    string? LastError);

/// <summary>
/// Summary of a replayable watchlist capture grouped by aircraft identifiers.
/// </summary>
/// <param name="SessionKey">Stable composite key for the grouped replay session.</param>
/// <param name="WatchlistLabel">Optional watchlist label associated with the capture.</param>
/// <param name="Icao">ICAO address represented in the session.</param>
/// <param name="TailNumber">Tail number represented in the session.</param>
/// <param name="FlightNumber">Flight number represented in the session.</param>
/// <param name="Callsign">Callsign represented in the session.</param>
/// <param name="StartedAtUtc">Timestamp of the first event in the session.</param>
/// <param name="EndedAtUtc">Timestamp of the latest event in the session.</param>
/// <param name="EventCount">Number of stored events in the session.</param>
public sealed record ReplaySessionDto(
    string SessionKey,
    string? WatchlistLabel,
    string? Icao,
    string? TailNumber,
    string? FlightNumber,
    string? Callsign,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    long EventCount);

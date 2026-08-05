namespace Adsb.Server.Contracts;

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

public sealed record WatchlistMatchEvent(
    DateTimeOffset ReceivedAtUtc,
    string? WatchlistLabel,
    string IdentifierType,
    string? IdentifierValue,
    AircraftTelemetryEvent Telemetry);

public sealed record ServerStatusDto(
    DateTimeOffset StartedAtUtc,
    bool CaptureEnabled,
    bool DeviceConnected,
    long FramesDecoded,
    long EventsPublished,
    long WatchlistEventsRecorded,
    DateTimeOffset? LastEventAtUtc,
    string? LastError);

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

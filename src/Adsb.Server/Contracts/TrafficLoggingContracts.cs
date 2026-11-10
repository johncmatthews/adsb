namespace Adsb.Server.Contracts;

/// <summary>
/// Query options for retrieving archived all-aircraft telemetry.
/// </summary>
/// <param name="Icao">Optional ICAO address filter.</param>
/// <param name="TailNumber">Optional tail-number filter.</param>
/// <param name="FlightNumber">Optional flight-number filter.</param>
/// <param name="Callsign">Optional callsign filter.</param>
/// <param name="FromUtc">Optional inclusive lower UTC timestamp bound.</param>
/// <param name="ToUtc">Optional inclusive upper UTC timestamp bound.</param>
/// <param name="Limit">Maximum number of events to return before server clamping.</param>
public sealed record TrafficQuery(
    string? Icao,
    string? TailNumber,
    string? FlightNumber,
    string? Callsign,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    int Limit = 1000);

/// <summary>
/// Summary information about the all-aircraft telemetry archive.
/// </summary>
/// <param name="LoggingEnabled">Whether new all-aircraft events are currently configured to be recorded.</param>
/// <param name="EventCount">Total number of archived telemetry events.</param>
/// <param name="DistinctAircraftCount">Number of distinct non-null ICAO addresses in the archive.</param>
/// <param name="FirstEventAtUtc">Timestamp of the earliest archived event.</param>
/// <param name="LastEventAtUtc">Timestamp of the latest archived event.</param>
public sealed record TrafficArchiveStatsDto(
    bool LoggingEnabled,
    long EventCount,
    long DistinctAircraftCount,
    DateTimeOffset? FirstEventAtUtc,
    DateTimeOffset? LastEventAtUtc);

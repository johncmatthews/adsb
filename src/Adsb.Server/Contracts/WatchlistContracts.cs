namespace Adsb.Server.Contracts;

/// <summary>
/// Watchlist aircraft entry returned by the REST API and persisted to the watchlist JSON file.
/// </summary>
/// <param name="Id">Stable server-generated entry identifier.</param>
/// <param name="Label">Optional user-facing label for the watched aircraft.</param>
/// <param name="Icaos">ICAO addresses that should match this aircraft.</param>
/// <param name="TailNumbers">Tail numbers or registrations that should match this aircraft.</param>
/// <param name="FlightNumbers">Operational flight numbers that should match this aircraft.</param>
/// <param name="Callsigns">ADS-B callsigns that should match this aircraft.</param>
public sealed record WatchlistAircraftDto(
    string Id,
    string? Label,
    IReadOnlyList<string> Icaos,
    IReadOnlyList<string> TailNumbers,
    IReadOnlyList<string> FlightNumbers,
    IReadOnlyList<string> Callsigns);

/// <summary>
/// Request body used to create or replace a watchlist aircraft entry.
/// </summary>
/// <param name="Label">Optional user-facing label for the watched aircraft.</param>
/// <param name="Icaos">ICAO addresses to match.</param>
/// <param name="TailNumbers">Tail numbers or registrations to match.</param>
/// <param name="FlightNumbers">Operational flight numbers to match.</param>
/// <param name="Callsigns">ADS-B callsigns to match.</param>
public sealed record UpsertWatchlistAircraftRequest(
    string? Label,
    IReadOnlyList<string>? Icaos,
    IReadOnlyList<string>? TailNumbers,
    IReadOnlyList<string>? FlightNumbers,
    IReadOnlyList<string>? Callsigns);

/// <summary>
/// Internal match result connecting a telemetry event to the watchlist entry and identifier that matched it.
/// </summary>
/// <param name="WatchlistId">ID of the matching watchlist entry.</param>
/// <param name="Label">Optional label from the matching watchlist entry.</param>
/// <param name="IdentifierType">Identifier class that matched, such as icao, tail, flight, or callsign.</param>
/// <param name="IdentifierValue">Observed telemetry identifier that matched the entry.</param>
public sealed record WatchlistMatchDto(
    string WatchlistId,
    string? Label,
    string IdentifierType,
    string? IdentifierValue);

/// <summary>
/// Query options for retrieving stored replay events from the watchlist telemetry database.
/// </summary>
/// <param name="Icao">Optional ICAO filter.</param>
/// <param name="TailNumber">Optional tail-number filter.</param>
/// <param name="FlightNumber">Optional flight-number filter.</param>
/// <param name="Callsign">Optional callsign filter.</param>
/// <param name="FromUtc">Optional inclusive lower UTC timestamp bound.</param>
/// <param name="ToUtc">Optional inclusive upper UTC timestamp bound.</param>
/// <param name="Limit">Maximum number of events to return before server clamping.</param>
public sealed record ReplayQuery(
    string? Icao,
    string? TailNumber,
    string? FlightNumber,
    string? Callsign,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    int Limit = 1000);

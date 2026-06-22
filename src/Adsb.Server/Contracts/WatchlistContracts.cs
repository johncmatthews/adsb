namespace Adsb.Server.Contracts;

public sealed record WatchlistAircraftDto(
    string Id,
    string? Label,
    IReadOnlyList<string> Icaos,
    IReadOnlyList<string> TailNumbers,
    IReadOnlyList<string> FlightNumbers,
    IReadOnlyList<string> Callsigns);

public sealed record UpsertWatchlistAircraftRequest(
    string? Label,
    IReadOnlyList<string>? Icaos,
    IReadOnlyList<string>? TailNumbers,
    IReadOnlyList<string>? FlightNumbers,
    IReadOnlyList<string>? Callsigns);

public sealed record WatchlistMatchDto(
    string WatchlistId,
    string? Label,
    string IdentifierType,
    string? IdentifierValue);

public sealed record ReplayQuery(
    string? Icao,
    string? TailNumber,
    string? FlightNumber,
    string? Callsign,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    int Limit = 1000);

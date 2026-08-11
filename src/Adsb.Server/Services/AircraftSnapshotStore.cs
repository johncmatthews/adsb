using System.Collections.Concurrent;
using Adsb.Server.Contracts;

namespace Adsb.Server.Services;

/// <summary>
/// Thread-safe store of the latest known telemetry event per aircraft for snapshots and REST lookups.
/// </summary>
public sealed class AircraftSnapshotStore
{
    private readonly ConcurrentDictionary<string, AircraftTelemetryEvent> aircraft = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Inserts or replaces the latest telemetry event for an aircraft using the best available identifier as the key.
    /// </summary>
    public void Upsert(AircraftTelemetryEvent telemetry)
    {
        var key = telemetry.Icao ?? telemetry.TailNumber ?? telemetry.FlightNumber ?? telemetry.Callsign;
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        aircraft.AddOrUpdate(key, telemetry, (_, _) => telemetry);
    }

    /// <summary>
    /// Returns all latest aircraft snapshots sorted by their most stable display identifier.
    /// </summary>
    public IReadOnlyList<AircraftTelemetryEvent> GetAll() =>
        aircraft.Values
            .OrderBy(value => value.Icao ?? value.TailNumber ?? value.FlightNumber ?? value.Callsign)
            .ToArray();

    /// <summary>
    /// Finds the latest aircraft event by primary key or by any known ICAO, tail, flight, or callsign value.
    /// </summary>
    public bool TryGet(string identifier, out AircraftTelemetryEvent telemetry)
    {
        if (aircraft.TryGetValue(identifier, out telemetry!))
        {
            return true;
        }

        telemetry = aircraft.Values.FirstOrDefault(
            item =>
                EqualsIdentifier(item.Icao, identifier) ||
                EqualsIdentifier(item.TailNumber, identifier) ||
                EqualsIdentifier(item.FlightNumber, identifier) ||
                EqualsIdentifier(item.Callsign, identifier))!;

        return telemetry is not null;
    }

    /// <summary>
    /// Performs case-insensitive identifier comparison while treating missing values as non-matches.
    /// </summary>
    private static bool EqualsIdentifier(string? left, string right) =>
        left is not null && left.Equals(right, StringComparison.OrdinalIgnoreCase);
}

using System.Collections.Concurrent;
using Adsb.Server.Contracts;

namespace Adsb.Server.Services;

public sealed class AircraftSnapshotStore
{
    private readonly ConcurrentDictionary<string, AircraftTelemetryEvent> aircraft = new(StringComparer.OrdinalIgnoreCase);

    public void Upsert(AircraftTelemetryEvent telemetry)
    {
        var key = telemetry.Icao ?? telemetry.TailNumber ?? telemetry.FlightNumber ?? telemetry.Callsign;
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        aircraft.AddOrUpdate(key, telemetry, (_, _) => telemetry);
    }

    public IReadOnlyList<AircraftTelemetryEvent> GetAll() =>
        aircraft.Values
            .OrderBy(value => value.Icao ?? value.TailNumber ?? value.FlightNumber ?? value.Callsign)
            .ToArray();

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

    private static bool EqualsIdentifier(string? left, string right) =>
        left is not null && left.Equals(right, StringComparison.OrdinalIgnoreCase);
}

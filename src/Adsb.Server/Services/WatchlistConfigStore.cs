using System.Globalization;
using System.Text.Json;
using Adsb.Server.Contracts;
using Microsoft.Extensions.Options;

namespace Adsb.Server.Services;

/// <summary>
/// Thread-safe JSON-backed watchlist store used by the server REST API and live capture matcher.
/// </summary>
public sealed class WatchlistConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object gate = new();
    private readonly string path;
    private List<WatchlistAircraftDto>? entries;

    /// <summary>
    /// Creates a store bound to the configured watchlist path.
    /// </summary>
    public WatchlistConfigStore(IOptions<AdsbServerOptions> options)
    {
        path = System.IO.Path.GetFullPath(options.Value.Watchlist.Path);
    }

    /// <summary>
    /// Absolute path to the watchlist JSON file managed by this store.
    /// </summary>
    public string Path => path;

    /// <summary>
    /// Returns all configured aircraft entries after lazy-loading the backing JSON file.
    /// </summary>
    public IReadOnlyList<WatchlistAircraftDto> GetAll()
    {
        lock (gate)
        {
            EnsureLoaded();
            return entries!.ToArray();
        }
    }

    /// <summary>
    /// Adds a normalized watchlist aircraft entry and persists the updated JSON document.
    /// </summary>
    public WatchlistAircraftDto Add(UpsertWatchlistAircraftRequest request)
    {
        lock (gate)
        {
            EnsureLoaded();
            var item = Normalize(request, Guid.NewGuid().ToString("N"));
            entries!.Add(item);
            Save();
            return item;
        }
    }

    /// <summary>
    /// Replaces an existing watchlist entry while preserving its stable identifier.
    /// </summary>
    /// <returns>True when the entry existed and was updated.</returns>
    public bool TryUpdate(string id, UpsertWatchlistAircraftRequest request, out WatchlistAircraftDto item)
    {
        lock (gate)
        {
            EnsureLoaded();
            var index = entries!.FindIndex(entry => entry.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                item = default!;
                return false;
            }

            item = Normalize(request, entries[index].Id);
            entries[index] = item;
            Save();
            return true;
        }
    }

    /// <summary>
    /// Deletes a watchlist entry by ID and saves the file only when an entry was removed.
    /// </summary>
    public bool Delete(string id)
    {
        lock (gate)
        {
            EnsureLoaded();
            var removed = entries!.RemoveAll(entry => entry.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed)
            {
                Save();
            }

            return removed;
        }
    }

    /// <summary>
    /// Attempts to match telemetry against the current watchlist using ICAO, tail number, flight number, then callsign.
    /// </summary>
    public bool TryMatch(AircraftTelemetryEvent telemetry, out WatchlistMatchDto match)
    {
        lock (gate)
        {
            EnsureLoaded();
            foreach (var entry in entries!)
            {
                if (ContainsNormalized(entry.Icaos, telemetry.Icao, NormalizeIcao))
                {
                    match = new WatchlistMatchDto(entry.Id, entry.Label, "icao", telemetry.Icao);
                    return true;
                }

                if (ContainsNormalized(entry.TailNumbers, telemetry.TailNumber, NormalizeIdentifier))
                {
                    match = new WatchlistMatchDto(entry.Id, entry.Label, "tail", telemetry.TailNumber);
                    return true;
                }

                if (ContainsNormalized(entry.FlightNumbers, telemetry.FlightNumber, NormalizeIdentifier))
                {
                    match = new WatchlistMatchDto(entry.Id, entry.Label, "flight", telemetry.FlightNumber);
                    return true;
                }

                if (ContainsNormalized(entry.Callsigns, telemetry.Callsign, NormalizeIdentifier))
                {
                    match = new WatchlistMatchDto(entry.Id, entry.Label, "callsign", telemetry.Callsign);
                    return true;
                }
            }

            match = default!;
            return false;
        }
    }

    /// <summary>
    /// Loads the watchlist JSON once on first use, treating a missing file as an empty watchlist.
    /// </summary>
    private void EnsureLoaded()
    {
        if (entries is not null)
        {
            return;
        }

        if (!File.Exists(path))
        {
            entries = [];
            return;
        }

        var text = File.ReadAllText(path);
        entries = string.IsNullOrWhiteSpace(text) ? [] : LoadJson(text);
    }

    /// <summary>
    /// Writes the current watchlist entries as an object with an aircraft array, creating the directory if needed.
    /// </summary>
    private void Save()
    {
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var document = new WatchlistDocument(entries!);
        File.WriteAllText(path, JsonSerializer.Serialize(document, JsonOptions));
    }

    /// <summary>
    /// Parses a watchlist JSON document, accepting either a root array or an object with an aircraft array.
    /// </summary>
    private static List<WatchlistAircraftDto> LoadJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var aircraft = root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray()
            : root.TryGetProperty("aircraft", out var configuredAircraft) &&
              configuredAircraft.ValueKind == JsonValueKind.Array
                ? configuredAircraft.EnumerateArray()
                : throw new ArgumentException("Watchlist JSON must be an array or an object with an aircraft array.");

        var entries = new List<WatchlistAircraftDto>();
        foreach (var item in aircraft)
        {
            var entry = new WatchlistAircraftDto(
                GetString(item, "id") ?? Guid.NewGuid().ToString("N"),
                GetString(item, "label", "name", "description"),
                GetStrings(item, "icao", "icaos", "icao24", "hex", "hexid"),
                GetStrings(item, "tail", "tails", "tailNumber", "tailNumbers", "tail_number", "registration", "registrations"),
                GetStrings(item, "flight", "flights", "flightNumber", "flightNumbers", "flight_number"),
                GetStrings(item, "callsign", "callsigns"));

            if (!IsEmpty(entry))
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    /// <summary>
    /// Normalizes incoming REST request values and ensures the entry contains at least one usable identifier.
    /// </summary>
    private static WatchlistAircraftDto Normalize(UpsertWatchlistAircraftRequest request, string id)
    {
        var item = new WatchlistAircraftDto(
            id,
            string.IsNullOrWhiteSpace(request.Label) ? null : request.Label.Trim(),
            NormalizeValues(request.Icaos, NormalizeIcao),
            NormalizeValues(request.TailNumbers, NormalizeIdentifier),
            NormalizeValues(request.FlightNumbers, NormalizeIdentifier),
            NormalizeValues(request.Callsigns, NormalizeIdentifier));

        if (IsEmpty(item))
        {
            throw new ArgumentException("Watchlist aircraft must include at least one ICAO, tail, flight, or callsign identifier.");
        }

        return item;
    }

    /// <summary>
    /// Checks whether a watchlist DTO has no configured identifiers and should be rejected or ignored.
    /// </summary>
    private static bool IsEmpty(WatchlistAircraftDto item) =>
        item.Icaos.Count == 0 &&
        item.TailNumbers.Count == 0 &&
        item.FlightNumbers.Count == 0 &&
        item.Callsigns.Count == 0;

    /// <summary>
    /// Normalizes, de-duplicates, and removes empty identifier values from a request collection.
    /// </summary>
    private static IReadOnlyList<string> NormalizeValues(
        IReadOnlyList<string>? values,
        Func<string?, string?> normalize) =>
        values?
            .Select(normalize)
            .Where(value => value is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => value!)
            .ToArray() ?? [];

    /// <summary>
    /// Reads one or more string properties from a JSON entry, accepting either scalar strings or string arrays.
    /// </summary>
    private static IReadOnlyList<string> GetStrings(JsonElement item, params string[] propertyNames)
    {
        var values = new List<string>();
        foreach (var name in propertyNames)
        {
            if (!item.TryGetProperty(name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                AddIfPresent(values, property.GetString());
            }
            else if (property.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in property.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.String)
                    {
                        AddIfPresent(values, element.GetString());
                    }
                }
            }
        }

        return values;
    }

    /// <summary>
    /// Reads the first matching scalar string property from a JSON entry.
    /// </summary>
    private static string? GetString(JsonElement item, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (item.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }
        }

        return null;
    }

    /// <summary>
    /// Adds a non-empty JSON value to a collection before normalization removes duplicates.
    /// </summary>
    private static void AddIfPresent(List<string> values, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values.Add(value);
        }
    }

    /// <summary>
    /// Compares a normalized observed telemetry identifier against a configured identifier collection.
    /// </summary>
    private static bool ContainsNormalized(
        IReadOnlyList<string> configuredValues,
        string? observedValue,
        Func<string?, string?> normalize)
    {
        var observed = normalize(observedValue);
        return observed is not null && configuredValues.Any(value => normalize(value) == observed);
    }

    /// <summary>
    /// Normalizes ICAO addresses to six uppercase hexadecimal characters, accepting an optional 0x prefix.
    /// </summary>
    private static string? NormalizeIcao(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        normalized = normalized.StartsWith("0X", StringComparison.Ordinal)
            ? normalized[2..]
            : normalized;

        return normalized.Length == 6 &&
               int.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)
            ? normalized
            : null;
    }

    /// <summary>
    /// Normalizes tail, flight, and callsign identifiers for case-insensitive matching.
    /// </summary>
    private static string? NormalizeIdentifier(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant().Replace(" ", string.Empty, StringComparison.Ordinal);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    /// <summary>
    /// Serialized watchlist file shape used when the server writes the JSON document.
    /// </summary>
    private sealed record WatchlistDocument(IReadOnlyList<WatchlistAircraftDto> Aircraft);
}

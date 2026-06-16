using System.Globalization;
using System.Text.Json;

namespace Adsb.Tracking;

public sealed class AircraftWatchlist
{
    public static AircraftWatchlist Empty { get; } = new(Array.Empty<WatchlistEntry>());

    private readonly IReadOnlyList<WatchlistEntry> entries;

    private AircraftWatchlist(IReadOnlyList<WatchlistEntry> entries)
    {
        this.entries = entries;
    }

    public int Count => entries.Count;

    public static AircraftWatchlist Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Empty;
        }

        var text = File.ReadAllText(path);
        var trimmed = text.AsSpan().TrimStart();
        var entries = trimmed is ['{', ..] or ['[', ..]
            ? LoadJson(text)
            : LoadDelimited(text);

        return new AircraftWatchlist(entries);
    }

    public bool TryMatch(AircraftIdentityTelemetry telemetry, out WatchlistMatch match)
    {
        foreach (var entry in entries)
        {
            if (entry.TryMatch(telemetry, out match))
            {
                return true;
            }
        }

        match = default;
        return false;
    }

    private static IReadOnlyList<WatchlistEntry> LoadJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var aircraft = root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray()
            : root.TryGetProperty("aircraft", out var configuredAircraft) &&
              configuredAircraft.ValueKind == JsonValueKind.Array
                ? configuredAircraft.EnumerateArray()
                : throw new ArgumentException("Watchlist JSON must be an array or an object with an aircraft array.");

        var entries = new List<WatchlistEntry>();
        foreach (var item in aircraft)
        {
            var entry = new WatchlistEntry(
                GetString(item, "label", "name", "description"),
                GetStrings(item, "icao", "icaos", "icao24", "hex", "hexid"),
                GetStrings(item, "tail", "tails", "tail_number", "tailNumbers", "registration", "registrations"),
                GetStrings(item, "flight", "flights", "flight_number", "flightNumbers"),
                GetStrings(item, "callsign", "callsigns"));

            if (!entry.IsEmpty)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    private static IReadOnlyList<WatchlistEntry> LoadDelimited(string text)
    {
        var entries = new List<WatchlistEntry>();
        string[]? header = null;

        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var fields = ParseCsvLine(line);
            if (fields.Count == 0)
            {
                continue;
            }

            if (header is null && LooksLikeHeader(fields))
            {
                header = fields.Select(NormalizeColumnName).ToArray();
                continue;
            }

            entries.Add(header is null
                ? EntryFromSingleLine(fields[0])
                : EntryFromHeader(fields, header));
        }

        return entries.Where(entry => !entry.IsEmpty).ToArray();
    }

    private static WatchlistEntry EntryFromHeader(IReadOnlyList<string> fields, IReadOnlyList<string> header)
    {
        var label = GetField(fields, header, "label", "name", "description");
        return new WatchlistEntry(
            label,
            Values(GetField(fields, header, "icao", "icao24", "hex", "hexid")),
            Values(GetField(fields, header, "tail", "tail_number", "registration", "reg", "r")),
            Values(GetField(fields, header, "flight", "flight_number")),
            Values(GetField(fields, header, "callsign")));
    }

    private static WatchlistEntry EntryFromSingleLine(string value)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            return WatchlistEntry.Empty;
        }

        if (NormalizeIcao(normalized) is not null)
        {
            return new WatchlistEntry(normalized, [normalized], [], [], []);
        }

        if (LooksLikeTailNumber(normalized))
        {
            return new WatchlistEntry(normalized, [], [normalized], [], []);
        }

        return new WatchlistEntry(normalized, [], [], [normalized], [normalized]);
    }

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

    private static string? GetField(IReadOnlyList<string> fields, IReadOnlyList<string> header, params string[] names)
    {
        for (var i = 0; i < header.Count && i < fields.Count; i++)
        {
            if (names.Contains(header[i], StringComparer.Ordinal))
            {
                return fields[i];
            }
        }

        return null;
    }

    private static bool LooksLikeHeader(IReadOnlyList<string> fields) =>
        fields.Select(NormalizeColumnName).Any(
            field => field is "icao" or "icao24" or "hex" or "hexid" or "tail" or "tail_number" or "registration" or "flight" or "flight_number" or "callsign");

    private static string NormalizeColumnName(string value) =>
        value.Trim().ToLowerInvariant().Replace("-", "_", StringComparison.Ordinal);

    private static IReadOnlyList<string> Values(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Array.Empty<string>() : [value];

    private static void AddIfPresent(List<string> values, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values.Add(value);
        }
    }

    private static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new List<char>();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Add('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (c == ',' && !inQuotes)
            {
                fields.Add(new string(current.ToArray()).Trim());
                current.Clear();
                continue;
            }

            current.Add(c);
        }

        fields.Add(new string(current.ToArray()).Trim());
        return fields;
    }

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

    private static string? NormalizeIdentifier(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant().Replace(" ", string.Empty, StringComparison.Ordinal);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static bool LooksLikeTailNumber(string value)
    {
        var normalized = NormalizeIdentifier(value);
        return normalized is not null &&
               ((normalized.Length >= 2 && normalized[0] == 'N' && char.IsDigit(normalized[1])) ||
                normalized.Contains('-', StringComparison.Ordinal));
    }

    private sealed record WatchlistEntry(
        string? Label,
        IReadOnlyList<string> Icaos,
        IReadOnlyList<string> TailNumbers,
        IReadOnlyList<string> FlightNumbers,
        IReadOnlyList<string> Callsigns)
    {
        public static WatchlistEntry Empty { get; } = new(null, [], [], [], []);
        public bool IsEmpty => Icaos.Count == 0 && TailNumbers.Count == 0 && FlightNumbers.Count == 0 && Callsigns.Count == 0;

        public bool TryMatch(AircraftIdentityTelemetry telemetry, out WatchlistMatch match)
        {
            if (ContainsNormalized(Icaos, telemetry.Icao, NormalizeIcao))
            {
                match = new WatchlistMatch(Label, "icao", telemetry.Icao);
                return true;
            }

            if (ContainsNormalized(TailNumbers, telemetry.TailNumber, NormalizeIdentifier))
            {
                match = new WatchlistMatch(Label, "tail", telemetry.TailNumber);
                return true;
            }

            if (ContainsNormalized(FlightNumbers, telemetry.FlightNumber, NormalizeIdentifier))
            {
                match = new WatchlistMatch(Label, "flight", telemetry.FlightNumber);
                return true;
            }

            if (ContainsNormalized(Callsigns, telemetry.Callsign, NormalizeIdentifier))
            {
                match = new WatchlistMatch(Label, "callsign", telemetry.Callsign);
                return true;
            }

            match = default;
            return false;
        }

        private static bool ContainsNormalized(
            IReadOnlyList<string> configuredValues,
            string? observedValue,
            Func<string?, string?> normalize)
        {
            var observed = normalize(observedValue);
            return observed is not null && configuredValues.Any(value => normalize(value) == observed);
        }
    }
}

public readonly record struct AircraftIdentityTelemetry(
    string? Icao,
    string? Callsign,
    string? TailNumber,
    string? FlightNumber);

public readonly record struct WatchlistMatch(string? Label, string IdentifierType, string? IdentifierValue);

using System.Globalization;
using System.Text.RegularExpressions;

namespace Adsb.Decoding;

public sealed class AircraftIdentityResolver
{
    public static AircraftIdentityResolver Empty { get; } = new(null);

    private const int UnitedStatesIcaoStart = 0xA00001;
    private const int UnitedStatesRegistrationCount = 915_399;
    private const string LimitedAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private static readonly Regex OperationalFlightIdPattern = new("^[A-Z]{2,4}[0-9][A-Z0-9]*$", RegexOptions.Compiled);
    private readonly IReadOnlyDictionary<string, string> registry;

    private AircraftIdentityResolver(IReadOnlyDictionary<string, string>? registry)
    {
        this.registry = registry ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public static AircraftIdentityResolver Load(string? registryPath)
    {
        if (string.IsNullOrWhiteSpace(registryPath))
        {
            return Empty;
        }

        return new AircraftIdentityResolver(LoadRegistry(registryPath));
    }

    public AircraftIdentity Resolve(string? icao, string? callsign)
    {
        var tailNumber = ResolveTailNumber(icao);
        var flightNumber = ResolveFlightNumber(callsign, tailNumber);
        return new AircraftIdentity(tailNumber, flightNumber);
    }

    public string? ResolveTailNumber(string? icao)
    {
        var normalizedIcao = NormalizeIcao(icao);
        if (normalizedIcao is null)
        {
            return null;
        }

        if (registry.TryGetValue(normalizedIcao, out var registration))
        {
            return registration;
        }

        return TryDecodeUnitedStatesRegistration(normalizedIcao, out var usRegistration)
            ? usRegistration
            : null;
    }

    public static string? ResolveFlightNumber(string? callsign, string? tailNumber = null)
    {
        var normalized = NormalizeCallsign(callsign);
        if (normalized is null)
        {
            return null;
        }

        if (IsRegistrationLike(normalized) ||
            normalized.Equals(tailNumber, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return OperationalFlightIdPattern.IsMatch(normalized) ? normalized : null;
    }

    private static IReadOnlyDictionary<string, string> LoadRegistry(string registryPath)
    {
        var registrations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var reader = File.OpenText(registryPath);

        string? line;
        string[]? header = null;
        var icaoColumn = 0;
        var registrationColumn = 1;

        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = ParseCsvLine(line);
            if (fields.Count == 0)
            {
                continue;
            }

            if (header is null && TryReadHeader(fields, out icaoColumn, out registrationColumn))
            {
                header = fields.Select(field => field.Trim()).ToArray();
                continue;
            }

            header ??= Array.Empty<string>();
            if (fields.Count <= Math.Max(icaoColumn, registrationColumn))
            {
                continue;
            }

            var icao = NormalizeIcao(fields[icaoColumn]);
            var registration = NormalizeRegistration(fields[registrationColumn]);
            if (icao is not null && registration is not null)
            {
                registrations[icao] = registration;
            }
        }

        return registrations;
    }

    private static bool TryReadHeader(IReadOnlyList<string> fields, out int icaoColumn, out int registrationColumn)
    {
        icaoColumn = FindColumn(fields, "icao", "icao24", "hex", "hexid", "mode_s", "mode_s_hex");
        registrationColumn = FindColumn(fields, "r", "reg", "registration", "tail", "tail_number", "tailnumber", "n_number");
        return icaoColumn >= 0 && registrationColumn >= 0;
    }

    private static int FindColumn(IReadOnlyList<string> fields, params string[] names)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            var normalized = fields[i].Trim().ToLowerInvariant().Replace("-", "_", StringComparison.Ordinal);
            if (names.Contains(normalized, StringComparer.Ordinal))
            {
                return i;
            }
        }

        return -1;
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
                fields.Add(new string(current.ToArray()));
                current.Clear();
                continue;
            }

            current.Add(c);
        }

        fields.Add(new string(current.ToArray()));
        return fields;
    }

    private static string? NormalizeIcao(string? icao)
    {
        var normalized = icao?.Trim().ToUpperInvariant();
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

    private static string? NormalizeRegistration(string? registration)
    {
        var normalized = registration?.Trim().ToUpperInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? NormalizeCallsign(string? callsign)
    {
        var normalized = callsign?.Trim().ToUpperInvariant().Replace(" ", string.Empty, StringComparison.Ordinal);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static bool TryDecodeUnitedStatesRegistration(string icao, out string registration)
    {
        registration = string.Empty;
        var address = int.Parse(icao, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var offset = address - UnitedStatesIcaoStart;
        if (offset is < 0 or >= UnitedStatesRegistrationCount)
        {
            return false;
        }

        var firstDigit = (offset / 101_711) + 1;
        registration = "N" + firstDigit.ToString(CultureInfo.InvariantCulture);
        offset %= 101_711;

        if (offset <= 600)
        {
            registration += DecodeTwoLetterSuffix(offset);
            return true;
        }

        offset -= 601;
        var secondDigit = offset / 10_111;
        registration += secondDigit.ToString(CultureInfo.InvariantCulture);
        offset %= 10_111;

        if (offset <= 600)
        {
            registration += DecodeTwoLetterSuffix(offset);
            return true;
        }

        offset -= 601;
        var thirdDigit = offset / 951;
        registration += thirdDigit.ToString(CultureInfo.InvariantCulture);
        offset %= 951;

        if (offset <= 600)
        {
            registration += DecodeTwoLetterSuffix(offset);
            return true;
        }

        offset -= 601;
        var fourthDigit = offset / 35;
        registration += fourthDigit.ToString(CultureInfo.InvariantCulture);
        offset %= 35;

        if (offset <= 24)
        {
            registration += DecodeOneLetterSuffix(offset);
            return true;
        }

        offset -= 25;
        registration += offset.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private static string DecodeTwoLetterSuffix(int remainder)
    {
        if (remainder == 0)
        {
            return string.Empty;
        }

        remainder--;
        return LimitedAlphabet[remainder / 25] + DecodeOneLetterSuffix(remainder % 25);
    }

    private static string DecodeOneLetterSuffix(int remainder)
    {
        if (remainder == 0)
        {
            return string.Empty;
        }

        remainder--;
        return LimitedAlphabet[remainder].ToString();
    }

    private static bool IsRegistrationLike(string value)
    {
        if (value.Length < 2)
        {
            return false;
        }

        if (value[0] == 'N' && char.IsDigit(value[1]))
        {
            return true;
        }

        return value.Contains('-', StringComparison.Ordinal);
    }
}

public sealed record AircraftIdentity(string? TailNumber, string? FlightNumber);

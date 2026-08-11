using System.Globalization;
using System.Text.Json;
using Adsb.Server.Contracts;

namespace Adsb.Server.Compatibility;

/// <summary>
/// Converts normalized ADS-B telemetry into optional compatibility output formats used by existing receiver tools.
/// </summary>
public static class CompatibilityFormatters
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Serializes telemetry as one newline-delimited JSON object.
    /// </summary>
    public static string ToJsonLine(AircraftTelemetryEvent telemetry) =>
        JsonSerializer.Serialize(telemetry, JsonOptions) + "\n";

    /// <summary>
    /// Formats telemetry as an SBS/BaseStation MSG line using the closest message type for the fields present.
    /// </summary>
    public static string ToSbsLine(AircraftTelemetryEvent telemetry)
    {
        var date = telemetry.ReceivedAtUtc.UtcDateTime.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
        var time = telemetry.ReceivedAtUtc.UtcDateTime.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var messageType = telemetry.Latitude.HasValue && telemetry.Longitude.HasValue
            ? 3
            : telemetry.GroundSpeedKnots.HasValue || telemetry.TrackDegrees.HasValue
                ? 4
                : 1;

        return string.Join(
            ',',
            "MSG",
            messageType.ToString(CultureInfo.InvariantCulture),
            "1",
            "1",
            telemetry.Icao ?? string.Empty,
            "1",
            date,
            time,
            date,
            time,
            telemetry.Callsign ?? telemetry.FlightNumber ?? string.Empty,
            telemetry.AltitudeFeet?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            telemetry.GroundSpeedKnots?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            telemetry.TrackDegrees?.ToString("F0", CultureInfo.InvariantCulture) ?? string.Empty,
            telemetry.Latitude?.ToString("F6", CultureInfo.InvariantCulture) ?? string.Empty,
            telemetry.Longitude?.ToString("F6", CultureInfo.InvariantCulture) ?? string.Empty,
            telemetry.VerticalRateFeetPerMinute?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            telemetry.CrcOk ? "0" : "1") + "\n";
    }

    /// <summary>
    /// Encodes the raw Mode S frame in Beast binary format with timestamp, signal level, and byte stuffing.
    /// </summary>
    public static byte[] ToBeastFrame(AircraftTelemetryEvent telemetry)
    {
        var raw = Convert.FromHexString(telemetry.RawHex);
        var type = raw.Length == 14 ? (byte)'3' : (byte)'2';
        var signal = (byte)Math.Clamp((int)Math.Round((telemetry.SignalDb * 8) + 32), 0, 255);
        var timestamp = (ulong)Math.Max(0, telemetry.ReceivedAtUtc.ToUnixTimeMilliseconds()) * 12_000UL;

        var payload = new List<byte>(2 + 6 + 1 + raw.Length)
        {
            0x1A,
            type
        };

        for (var shift = 40; shift >= 0; shift -= 8)
        {
            payload.Add((byte)(timestamp >> shift));
        }

        payload.Add(signal);
        payload.AddRange(raw);
        return EscapeBeast(payload);
    }

    /// <summary>
    /// Applies Beast byte stuffing by duplicating each frame delimiter byte inside the payload.
    /// </summary>
    private static byte[] EscapeBeast(IReadOnlyList<byte> payload)
    {
        var escaped = new List<byte>(payload.Count + 2);
        foreach (var value in payload)
        {
            escaped.Add(value);
            if (value == 0x1A)
            {
                escaped.Add(value);
            }
        }

        return escaped.ToArray();
    }
}

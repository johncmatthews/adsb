using System.Globalization;
using System.Text.Json;
using Adsb.Server.Contracts;

namespace Adsb.Server.Compatibility;

public static class CompatibilityFormatters
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string ToJsonLine(AircraftTelemetryEvent telemetry) =>
        JsonSerializer.Serialize(telemetry, JsonOptions) + "\n";

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

using System.Globalization;
using Adsb.Decoding;

namespace Adsb.Formatting;

public static class ConsoleMessageFormatter
{
    public static string Format(ModeSMessage message, AircraftSnapshot? snapshot, bool includeRaw)
    {
        var parts = new List<string>
        {
            message.ReceivedAt.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "Z",
            $"DF={message.DownlinkFormat}"
        };

        if (message.Icao is not null)
        {
            parts.Add($"ICAO={message.Icao}");
        }

        if (message.TypeCode.HasValue)
        {
            parts.Add($"TC={message.TypeCode.Value}");
        }

        parts.Add(message.Description);

        if (!message.CrcOk)
        {
            parts.Add($"CRC=bad:{message.CrcRemainder:X6}");
        }

        AddIdentity(parts, message, snapshot);

        if (message.Category.HasValue)
        {
            parts.Add($"category={message.Category}");
        }

        AddAltitude(parts, message, snapshot);
        AddPosition(parts, snapshot);
        AddVelocity(parts, message, snapshot);

        parts.Add($"signal={message.SignalDb:F1}dB");

        if (includeRaw)
        {
            parts.Add($"raw={message.RawHex}");
        }

        return string.Join(" ", parts);
    }

    private static void AddIdentity(List<string> parts, ModeSMessage message, AircraftSnapshot? snapshot)
    {
        var callsign = message.Callsign ?? snapshot?.Callsign;
        var tailNumber = snapshot?.TailNumber;
        var flightNumber = snapshot?.FlightNumber;

        if (!string.IsNullOrWhiteSpace(tailNumber))
        {
            parts.Add($"tail={tailNumber}");
        }

        if (!string.IsNullOrWhiteSpace(flightNumber))
        {
            parts.Add($"flight={flightNumber}");
        }

        if (!string.IsNullOrWhiteSpace(callsign) &&
            !callsign.Equals(tailNumber, StringComparison.OrdinalIgnoreCase) &&
            !callsign.Equals(flightNumber, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add($"callsign={callsign}");
        }
    }

    private static void AddAltitude(List<string> parts, ModeSMessage message, AircraftSnapshot? snapshot)
    {
        var altitudeFeet = message.AltitudeFeet ?? snapshot?.AltitudeFeet;
        if (altitudeFeet.HasValue)
        {
            parts.Add($"alt={altitudeFeet.Value}ft");
        }

        var gnssHeight = message.GnssHeightMeters ?? snapshot?.GnssHeightMeters;
        if (gnssHeight.HasValue)
        {
            parts.Add($"gnss_h={gnssHeight.Value}m");
        }
    }

    private static void AddPosition(List<string> parts, AircraftSnapshot? snapshot)
    {
        if (snapshot?.Latitude is null || snapshot.Longitude is null)
        {
            return;
        }

        parts.Add($"lat={snapshot.Latitude.Value:F5}");
        parts.Add($"lon={snapshot.Longitude.Value:F5}");
    }

    private static void AddVelocity(List<string> parts, ModeSMessage message, AircraftSnapshot? snapshot)
    {
        var velocity = message.Velocity;
        var groundSpeed = velocity?.GroundSpeedKnots ?? snapshot?.GroundSpeedKnots;
        if (groundSpeed.HasValue)
        {
            parts.Add($"gs={groundSpeed.Value}kt");
        }

        var track = velocity?.TrackDegrees ?? snapshot?.TrackDegrees;
        if (track.HasValue)
        {
            parts.Add($"track={track.Value:F0}deg");
        }

        if (velocity?.HeadingDegrees is not null)
        {
            parts.Add($"hdg={velocity.HeadingDegrees.Value}deg");
        }

        if (velocity?.AirspeedKnots is not null)
        {
            parts.Add($"{velocity.AirspeedType?.ToLowerInvariant() ?? "airspeed"}={velocity.AirspeedKnots.Value}kt");
        }

        var verticalRate = velocity?.VerticalRateFeetPerMinute ?? snapshot?.VerticalRateFeetPerMinute;
        if (verticalRate.HasValue)
        {
            parts.Add($"vr={verticalRate.Value}fpm");
        }
    }
}

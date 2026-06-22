using Adsb.Decoding;
using Adsb.Server.Contracts;

namespace Adsb.Server.Services;

public static class AircraftTelemetryMapper
{
    public static AircraftTelemetryEvent Map(ModeSMessage message, AircraftSnapshot? snapshot)
    {
        var velocity = message.Velocity;
        return new AircraftTelemetryEvent(
            message.ReceivedAt.ToUniversalTime(),
            message.Icao ?? snapshot?.Icao,
            snapshot?.TailNumber,
            snapshot?.FlightNumber,
            message.Callsign ?? snapshot?.Callsign,
            message.DownlinkFormat,
            message.TypeCode,
            message.Description,
            message.AltitudeFeet ?? snapshot?.AltitudeFeet,
            message.GnssHeightMeters ?? snapshot?.GnssHeightMeters,
            snapshot?.Latitude,
            snapshot?.Longitude,
            velocity?.GroundSpeedKnots ?? snapshot?.GroundSpeedKnots,
            velocity?.TrackDegrees ?? snapshot?.TrackDegrees,
            velocity?.VerticalRateFeetPerMinute ?? snapshot?.VerticalRateFeetPerMinute,
            velocity?.HeadingDegrees,
            velocity?.AirspeedKnots,
            velocity?.AirspeedType,
            message.SignalDb,
            message.CrcOk,
            message.CrcRemainder.ToString("X6"),
            message.RawHex);
    }
}

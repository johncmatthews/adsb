namespace Adsb.Decoding;

public sealed class ModeSMessage
{
    public required DateTimeOffset ReceivedAt { get; init; }
    public required string RawHex { get; init; }
    public required int BitLength { get; init; }
    public required int DownlinkFormat { get; init; }
    public required bool CrcOk { get; init; }
    public required uint CrcRemainder { get; init; }
    public required double SignalDb { get; init; }
    public string? Icao { get; init; }
    public int? Capability { get; init; }
    public int? TypeCode { get; init; }
    public string Description { get; init; } = "Mode S";
    public string? Callsign { get; init; }
    public int? Category { get; init; }
    public int? AltitudeFeet { get; init; }
    public int? GnssHeightMeters { get; init; }
    public CprFrame? Cpr { get; init; }
    public VelocityReport? Velocity { get; init; }

    public bool IsExtendedSquitter => DownlinkFormat is 17 or 18;
}

public sealed record CprFrame(
    bool IsOdd,
    int EncodedLatitude,
    int EncodedLongitude,
    DateTimeOffset ReceivedAt,
    int? AltitudeFeet,
    int? GnssHeightMeters);

public sealed record VelocityReport(
    int Subtype,
    int? GroundSpeedKnots,
    double? TrackDegrees,
    int? VerticalRateFeetPerMinute,
    string? VerticalRateSource,
    int? HeadingDegrees,
    int? AirspeedKnots,
    string? AirspeedType);

public sealed record AircraftSnapshot(
    string Icao,
    string? Callsign,
    int? AltitudeFeet,
    int? GnssHeightMeters,
    double? Latitude,
    double? Longitude,
    int? GroundSpeedKnots,
    double? TrackDegrees,
    int? VerticalRateFeetPerMinute);

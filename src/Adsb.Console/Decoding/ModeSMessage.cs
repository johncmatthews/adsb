namespace Adsb.Decoding;

/// <summary>
/// Decoded representation of one Mode S frame, enriched with ADS-B fields when the frame is an extended squitter.
/// </summary>
public sealed class ModeSMessage
{
    /// <summary>UTC time assigned when the frame was recovered from the RTL-SDR sample stream.</summary>
    public required DateTimeOffset ReceivedAt { get; init; }
    /// <summary>Original Mode S frame bytes represented as uppercase hexadecimal text.</summary>
    public required string RawHex { get; init; }
    /// <summary>Frame length in bits, typically 56 or 112 for Mode S.</summary>
    public required int BitLength { get; init; }
    /// <summary>Five-bit Mode S downlink format from the start of the frame.</summary>
    public required int DownlinkFormat { get; init; }
    /// <summary>True when the Mode S CRC/parity remainder validates for this frame.</summary>
    public required bool CrcOk { get; init; }
    /// <summary>Computed 24-bit CRC/parity remainder, retained for diagnostics on invalid frames.</summary>
    public required uint CrcRemainder { get; init; }
    /// <summary>Estimated preamble signal level in decibels above the local sample floor.</summary>
    public required double SignalDb { get; init; }
    /// <summary>Six-character ICAO 24-bit address when the frame type exposes it directly.</summary>
    public string? Icao { get; init; }
    /// <summary>ADS-B capability bits for extended squitter frames.</summary>
    public int? Capability { get; init; }
    /// <summary>ADS-B extended squitter type code, if present.</summary>
    public int? TypeCode { get; init; }
    /// <summary>Human-readable message family for console and API display.</summary>
    public string Description { get; init; } = "Mode S";
    /// <summary>Aircraft callsign or flight ID decoded from ADS-B aircraft identification messages.</summary>
    public string? Callsign { get; init; }
    /// <summary>Aircraft category bits from an ADS-B aircraft identification message.</summary>
    public int? Category { get; init; }
    /// <summary>Barometric altitude in feet decoded from airborne position messages.</summary>
    public int? AltitudeFeet { get; init; }
    /// <summary>GNSS height in meters from ADS-B type codes that report geometric height instead of barometric altitude.</summary>
    public int? GnssHeightMeters { get; init; }
    /// <summary>Raw Compact Position Reporting payload used later by the state tracker to recover latitude and longitude.</summary>
    public CprFrame? Cpr { get; init; }
    /// <summary>Decoded velocity payload from ADS-B type code 19 messages.</summary>
    public VelocityReport? Velocity { get; init; }

    /// <summary>
    /// True when the downlink format is one of the ADS-B extended squitter formats this application publishes by default.
    /// </summary>
    public bool IsExtendedSquitter => DownlinkFormat is 17 or 18;
}

/// <summary>
/// Raw ADS-B Compact Position Reporting values from one airborne position frame.
/// </summary>
/// <param name="IsOdd">True for odd CPR frames; false for even frames.</param>
/// <param name="EncodedLatitude">The 17-bit encoded CPR latitude field.</param>
/// <param name="EncodedLongitude">The 17-bit encoded CPR longitude field.</param>
/// <param name="ReceivedAt">Timestamp used to decide whether even and odd CPR frames are close enough to pair.</param>
/// <param name="AltitudeFeet">Barometric altitude carried by the same frame, when available.</param>
/// <param name="GnssHeightMeters">GNSS height carried by the same frame, when available.</param>
public sealed record CprFrame(
    bool IsOdd,
    int EncodedLatitude,
    int EncodedLongitude,
    DateTimeOffset ReceivedAt,
    int? AltitudeFeet,
    int? GnssHeightMeters);

/// <summary>
/// Decoded ADS-B airborne velocity information, covering both ground vector and heading/airspeed subtypes.
/// </summary>
/// <param name="Subtype">ADS-B velocity subtype from type code 19.</param>
/// <param name="GroundSpeedKnots">Computed ground speed for ground-vector subtypes.</param>
/// <param name="TrackDegrees">Computed true track for ground-vector subtypes.</param>
/// <param name="VerticalRateFeetPerMinute">Vertical rate in feet per minute, when transmitted.</param>
/// <param name="VerticalRateSource">Altitude source for the vertical rate, usually baro or gnss.</param>
/// <param name="HeadingDegrees">Heading for heading/airspeed subtypes, when available.</param>
/// <param name="AirspeedKnots">Indicated or true airspeed for heading/airspeed subtypes.</param>
/// <param name="AirspeedType">Airspeed source label, usually IAS or TAS.</param>
public sealed record VelocityReport(
    int Subtype,
    int? GroundSpeedKnots,
    double? TrackDegrees,
    int? VerticalRateFeetPerMinute,
    string? VerticalRateSource,
    int? HeadingDegrees,
    int? AirspeedKnots,
    string? AirspeedType);

/// <summary>
/// Latest known aircraft state assembled from multiple ADS-B messages for the same ICAO address.
/// </summary>
/// <param name="Icao">ICAO 24-bit address used as the primary aircraft key.</param>
/// <param name="Callsign">Last decoded ADS-B callsign.</param>
/// <param name="TailNumber">Resolved aircraft registration, if known.</param>
/// <param name="FlightNumber">Operational flight number derived from the callsign, when distinguishable from a registration.</param>
/// <param name="AltitudeFeet">Last known barometric altitude.</param>
/// <param name="GnssHeightMeters">Last known GNSS height.</param>
/// <param name="Latitude">Last decoded latitude.</param>
/// <param name="Longitude">Last decoded longitude.</param>
/// <param name="GroundSpeedKnots">Last known ground speed.</param>
/// <param name="TrackDegrees">Last known track over ground.</param>
/// <param name="VerticalRateFeetPerMinute">Last known vertical rate.</param>
public sealed record AircraftSnapshot(
    string Icao,
    string? Callsign,
    string? TailNumber,
    string? FlightNumber,
    int? AltitudeFeet,
    int? GnssHeightMeters,
    double? Latitude,
    double? Longitude,
    int? GroundSpeedKnots,
    double? TrackDegrees,
    int? VerticalRateFeetPerMinute);

namespace Adsb.Decoding;

public sealed class ModeSDecoder
{
    private const string AircraftIdentificationCharacters =
        "#ABCDEFGHIJKLMNOPQRSTUVWXYZ##### ###############0123456789######";

    public ModeSMessage Decode(DemodulatedFrame frame)
    {
        var data = frame.Data.AsSpan();
        var downlinkFormat = (int)BitReader.GetBits(data, 0, 5);
        var crcRemainder = ModeSCrc.ComputeRemainder(data, frame.BitLength);
        var crcOk = crcRemainder == 0;

        var message = new ModeSMessage
        {
            ReceivedAt = frame.ReceivedAt,
            RawHex = frame.RawHex,
            BitLength = frame.BitLength,
            DownlinkFormat = downlinkFormat,
            CrcOk = crcOk,
            CrcRemainder = crcRemainder,
            SignalDb = frame.SignalDb,
            Description = DescribeDownlinkFormat(downlinkFormat)
        };

        if (frame.BitLength != 112 || downlinkFormat is not (17 or 18))
        {
            return message;
        }

        var capability = (int)BitReader.GetBits(data, 5, 3);
        var icao = Convert.ToHexString(data.Slice(1, 3));
        var typeCode = (int)BitReader.GetBits(data, 32, 5);
        var description = DescribeAdsbTypeCode(typeCode);

        var extended = new ModeSMessage
        {
            ReceivedAt = frame.ReceivedAt,
            RawHex = frame.RawHex,
            BitLength = frame.BitLength,
            DownlinkFormat = downlinkFormat,
            CrcOk = crcOk,
            CrcRemainder = crcRemainder,
            SignalDb = frame.SignalDb,
            Icao = icao,
            Capability = capability,
            TypeCode = typeCode,
            Description = description
        };

        return typeCode switch
        {
            >= 1 and <= 4 => DecodeAircraftIdentification(extended, data),
            >= 9 and <= 18 => DecodeAirbornePosition(extended, data, hasBarometricAltitude: true),
            >= 20 and <= 22 => DecodeAirbornePosition(extended, data, hasBarometricAltitude: false),
            19 => DecodeAirborneVelocity(extended, data),
            _ => extended
        };
    }

    private static ModeSMessage DecodeAircraftIdentification(ModeSMessage message, ReadOnlySpan<byte> data)
    {
        var chars = new char[8];
        for (var i = 0; i < chars.Length; i++)
        {
            var code = (int)BitReader.GetBits(data, 40 + (i * 6), 6);
            chars[i] = code < AircraftIdentificationCharacters.Length
                ? AircraftIdentificationCharacters[code]
                : '#';
        }

        var callsign = new string(chars).Replace("#", string.Empty, StringComparison.Ordinal).Trim();
        return Clone(
            message,
            callsign: string.IsNullOrWhiteSpace(callsign) ? null : callsign,
            category: (int)BitReader.GetBits(data, 37, 3));
    }

    private static ModeSMessage DecodeAirbornePosition(
        ModeSMessage message,
        ReadOnlySpan<byte> data,
        bool hasBarometricAltitude)
    {
        var altitudeCode = (int)BitReader.GetBits(data, 40, 12);
        var altitudeFeet = hasBarometricAltitude ? DecodeBarometricAltitude(altitudeCode) : null;
        int? gnssHeightMeters = hasBarometricAltitude || altitudeCode == 0 ? null : altitudeCode;
        var isOdd = BitReader.GetBit(data, 53) != 0;
        var cpr = new CprFrame(
            isOdd,
            (int)BitReader.GetBits(data, 54, 17),
            (int)BitReader.GetBits(data, 71, 17),
            message.ReceivedAt,
            altitudeFeet,
            gnssHeightMeters);

        return Clone(message, altitudeFeet: altitudeFeet, gnssHeightMeters: gnssHeightMeters, cpr: cpr);
    }

    private static ModeSMessage DecodeAirborneVelocity(ModeSMessage message, ReadOnlySpan<byte> data)
    {
        var subtype = (int)BitReader.GetBits(data, 37, 3);
        var verticalRate = DecodeVerticalRate(data, out var verticalRateSource);

        if (subtype is 1 or 2)
        {
            var scale = subtype == 2 ? 4 : 1;
            var eastWestRaw = (int)BitReader.GetBits(data, 46, 10);
            var northSouthRaw = (int)BitReader.GetBits(data, 57, 10);

            int? groundSpeed = null;
            double? track = null;
            if (eastWestRaw != 0 && northSouthRaw != 0)
            {
                var eastWest = (eastWestRaw - 1) * scale;
                var northSouth = (northSouthRaw - 1) * scale;

                if (BitReader.GetBit(data, 45) != 0)
                {
                    eastWest = -eastWest;
                }

                if (BitReader.GetBit(data, 56) != 0)
                {
                    northSouth = -northSouth;
                }

                groundSpeed = (int)Math.Round(Math.Sqrt((eastWest * eastWest) + (northSouth * northSouth)));
                track = NormalizeDegrees(Math.Atan2(eastWest, northSouth) * 180.0 / Math.PI);
            }

            return Clone(
                message,
                velocity: new VelocityReport(
                    subtype,
                    groundSpeed,
                    track,
                    verticalRate,
                    verticalRateSource,
                    HeadingDegrees: null,
                    AirspeedKnots: null,
                    AirspeedType: null));
        }

        if (subtype is 3 or 4)
        {
            var scale = subtype == 4 ? 4 : 1;
            var heading = BitReader.GetBit(data, 45) == 0
                ? null
                : (int?)Math.Round(BitReader.GetBits(data, 46, 10) * 360.0 / 1024.0);
            var airspeedRaw = (int)BitReader.GetBits(data, 57, 10);
            var airspeed = airspeedRaw == 0 ? null : (int?)((airspeedRaw - 1) * scale);
            var airspeedType = BitReader.GetBit(data, 56) == 0 ? "IAS" : "TAS";

            return Clone(
                message,
                velocity: new VelocityReport(
                    subtype,
                    GroundSpeedKnots: null,
                    TrackDegrees: null,
                    verticalRate,
                    verticalRateSource,
                    heading,
                    airspeed,
                    airspeedType));
        }

        return Clone(
            message,
            velocity: new VelocityReport(
                subtype,
                GroundSpeedKnots: null,
                TrackDegrees: null,
                verticalRate,
                verticalRateSource,
                HeadingDegrees: null,
                AirspeedKnots: null,
                AirspeedType: null));
    }

    private static int? DecodeBarometricAltitude(int altitudeCode)
    {
        if (altitudeCode == 0)
        {
            return null;
        }

        var qBitSet = (altitudeCode & 0x10) != 0;
        if (!qBitSet)
        {
            return null;
        }

        var n = ((altitudeCode & 0x0FE0) >> 1) | (altitudeCode & 0x000F);
        return (n * 25) - 1000;
    }

    private static int? DecodeVerticalRate(ReadOnlySpan<byte> data, out string? source)
    {
        source = BitReader.GetBit(data, 67) == 0 ? "baro" : "gnss";
        var sign = BitReader.GetBit(data, 68) == 0 ? 1 : -1;
        var raw = (int)BitReader.GetBits(data, 69, 9);
        if (raw == 0)
        {
            source = null;
            return null;
        }

        return sign * (raw - 1) * 64;
    }

    private static string DescribeDownlinkFormat(int downlinkFormat) =>
        downlinkFormat switch
        {
            0 => "Short air-air surveillance",
            4 => "Surveillance altitude reply",
            5 => "Surveillance identity reply",
            11 => "All-call reply",
            16 => "Long air-air surveillance",
            17 => "ADS-B extended squitter",
            18 => "ADS-B/TIS-B extended squitter",
            20 => "Comm-B altitude reply",
            21 => "Comm-B identity reply",
            _ => "Mode S"
        };

    private static string DescribeAdsbTypeCode(int typeCode) =>
        typeCode switch
        {
            >= 1 and <= 4 => "Aircraft identification",
            >= 5 and <= 8 => "Surface position",
            >= 9 and <= 18 => "Airborne position (barometric altitude)",
            19 => "Airborne velocity",
            >= 20 and <= 22 => "Airborne position (GNSS height)",
            28 => "Aircraft status",
            29 => "Target state and status",
            31 => "Aircraft operational status",
            _ => "ADS-B extended squitter"
        };

    private static double NormalizeDegrees(double degrees)
    {
        var normalized = degrees % 360.0;
        return normalized < 0 ? normalized + 360.0 : normalized;
    }

    private static ModeSMessage Clone(
        ModeSMessage source,
        string? callsign = null,
        int? category = null,
        int? altitudeFeet = null,
        int? gnssHeightMeters = null,
        CprFrame? cpr = null,
        VelocityReport? velocity = null) =>
        new()
        {
            ReceivedAt = source.ReceivedAt,
            RawHex = source.RawHex,
            BitLength = source.BitLength,
            DownlinkFormat = source.DownlinkFormat,
            CrcOk = source.CrcOk,
            CrcRemainder = source.CrcRemainder,
            SignalDb = source.SignalDb,
            Icao = source.Icao,
            Capability = source.Capability,
            TypeCode = source.TypeCode,
            Description = source.Description,
            Callsign = callsign ?? source.Callsign,
            Category = category ?? source.Category,
            AltitudeFeet = altitudeFeet ?? source.AltitudeFeet,
            GnssHeightMeters = gnssHeightMeters ?? source.GnssHeightMeters,
            Cpr = cpr ?? source.Cpr,
            Velocity = velocity ?? source.Velocity
        };
}

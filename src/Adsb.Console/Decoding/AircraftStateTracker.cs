namespace Adsb.Decoding;

public sealed class AircraftStateTracker
{
    private readonly Dictionary<string, MutableAircraftState> aircraft = new(StringComparer.OrdinalIgnoreCase);
    private readonly double? receiverLatitude;
    private readonly double? receiverLongitude;
    private readonly AircraftIdentityResolver identityResolver;

    public AircraftStateTracker(
        double? receiverLatitude = null,
        double? receiverLongitude = null,
        AircraftIdentityResolver? identityResolver = null)
    {
        this.receiverLatitude = receiverLatitude;
        this.receiverLongitude = receiverLongitude;
        this.identityResolver = identityResolver ?? AircraftIdentityResolver.Empty;
    }

    public AircraftSnapshot? Apply(ModeSMessage message)
    {
        if (message.Icao is null)
        {
            return null;
        }

        var state = GetOrCreate(message.Icao);

        if (!string.IsNullOrWhiteSpace(message.Callsign))
        {
            state.Callsign = message.Callsign;
        }

        if (message.AltitudeFeet.HasValue)
        {
            state.AltitudeFeet = message.AltitudeFeet;
        }

        if (message.GnssHeightMeters.HasValue)
        {
            state.GnssHeightMeters = message.GnssHeightMeters;
        }

        if (message.Velocity is not null)
        {
            state.GroundSpeedKnots = message.Velocity.GroundSpeedKnots ?? state.GroundSpeedKnots;
            state.TrackDegrees = message.Velocity.TrackDegrees ?? state.TrackDegrees;
            state.VerticalRateFeetPerMinute =
                message.Velocity.VerticalRateFeetPerMinute ?? state.VerticalRateFeetPerMinute;
        }

        if (message.Cpr is not null)
        {
            if (message.Cpr.IsOdd)
            {
                state.OddCpr = message.Cpr;
            }
            else
            {
                state.EvenCpr = message.Cpr;
            }

            if (TryDecodeGlobalPosition(state.EvenCpr, state.OddCpr, out var globalLatitude, out var globalLongitude))
            {
                state.Latitude = globalLatitude;
                state.Longitude = globalLongitude;
            }
            else if (receiverLatitude.HasValue &&
                     receiverLongitude.HasValue &&
                     TryDecodeLocalPosition(
                         message.Cpr,
                         receiverLatitude.Value,
                         receiverLongitude.Value,
                         out var localLatitude,
                         out var localLongitude))
            {
                state.Latitude = localLatitude;
                state.Longitude = localLongitude;
            }
        }

        var identity = identityResolver.Resolve(message.Icao, state.Callsign);
        state.TailNumber = identity.TailNumber;
        state.FlightNumber = identity.FlightNumber;

        return new AircraftSnapshot(
            message.Icao,
            state.Callsign,
            state.TailNumber,
            state.FlightNumber,
            state.AltitudeFeet,
            state.GnssHeightMeters,
            state.Latitude,
            state.Longitude,
            state.GroundSpeedKnots,
            state.TrackDegrees,
            state.VerticalRateFeetPerMinute);
    }

    private MutableAircraftState GetOrCreate(string icao)
    {
        if (!aircraft.TryGetValue(icao, out var state))
        {
            state = new MutableAircraftState();
            aircraft.Add(icao, state);
        }

        return state;
    }

    private static bool TryDecodeGlobalPosition(
        CprFrame? even,
        CprFrame? odd,
        out double latitude,
        out double longitude)
    {
        latitude = 0;
        longitude = 0;

        if (even is null || odd is null)
        {
            return false;
        }

        if (Math.Abs((even.ReceivedAt - odd.ReceivedAt).TotalSeconds) > 10)
        {
            return false;
        }

        var evenLat = even.EncodedLatitude / 131_072.0;
        var oddLat = odd.EncodedLatitude / 131_072.0;
        var evenLon = even.EncodedLongitude / 131_072.0;
        var oddLon = odd.EncodedLongitude / 131_072.0;

        var latitudeIndex = Math.Floor((59 * evenLat) - (60 * oddLat) + 0.5);
        var recoveredEvenLatitude = (360.0 / 60.0) * (Modulo(latitudeIndex, 60) + evenLat);
        var recoveredOddLatitude = (360.0 / 59.0) * (Modulo(latitudeIndex, 59) + oddLat);

        if (recoveredEvenLatitude >= 270)
        {
            recoveredEvenLatitude -= 360;
        }

        if (recoveredOddLatitude >= 270)
        {
            recoveredOddLatitude -= 360;
        }

        if (CprMath.NL(recoveredEvenLatitude) != CprMath.NL(recoveredOddLatitude))
        {
            return false;
        }

        var useOdd = odd.ReceivedAt > even.ReceivedAt;
        var selectedLatitude = useOdd ? recoveredOddLatitude : recoveredEvenLatitude;
        var longitudeZones = Math.Max(CprMath.NL(selectedLatitude) - (useOdd ? 1 : 0), 1);
        var longitudeIndex = Math.Floor(
            ((CprMath.NL(selectedLatitude) - 1) * evenLon) -
            (CprMath.NL(selectedLatitude) * oddLon) +
            0.5);

        var dLon = 360.0 / longitudeZones;
        var selectedLongitude = dLon * (Modulo(longitudeIndex, longitudeZones) + (useOdd ? oddLon : evenLon));
        if (selectedLongitude > 180)
        {
            selectedLongitude -= 360;
        }

        latitude = selectedLatitude;
        longitude = selectedLongitude;
        return true;
    }

    private static bool TryDecodeLocalPosition(
        CprFrame cpr,
        double referenceLatitude,
        double referenceLongitude,
        out double latitude,
        out double longitude)
    {
        latitude = 0;
        longitude = 0;

        var encodedLatitude = cpr.EncodedLatitude / 131_072.0;
        var encodedLongitude = cpr.EncodedLongitude / 131_072.0;
        var oddOffset = cpr.IsOdd ? 1 : 0;
        var dLat = 360.0 / (60 - oddOffset);
        var latitudeIndex = Math.Floor(referenceLatitude / dLat) +
                            Math.Floor((Modulo(referenceLatitude, dLat) / dLat) - encodedLatitude + 0.5);

        latitude = dLat * (latitudeIndex + encodedLatitude);
        if (latitude >= 270)
        {
            latitude -= 360;
        }

        var longitudeZones = Math.Max(CprMath.NL(latitude) - oddOffset, 1);
        var dLon = 360.0 / longitudeZones;
        var longitudeIndex = Math.Floor(referenceLongitude / dLon) +
                             Math.Floor((Modulo(referenceLongitude, dLon) / dLon) - encodedLongitude + 0.5);

        longitude = dLon * (longitudeIndex + encodedLongitude);
        if (longitude > 180)
        {
            longitude -= 360;
        }

        return latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;
    }

    private static double Modulo(double value, double divisor) =>
        value - (divisor * Math.Floor(value / divisor));

    private sealed class MutableAircraftState
    {
        public string? Callsign { get; set; }
        public string? TailNumber { get; set; }
        public string? FlightNumber { get; set; }
        public int? AltitudeFeet { get; set; }
        public int? GnssHeightMeters { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public int? GroundSpeedKnots { get; set; }
        public double? TrackDegrees { get; set; }
        public int? VerticalRateFeetPerMinute { get; set; }
        public CprFrame? EvenCpr { get; set; }
        public CprFrame? OddCpr { get; set; }
    }
}

using Adsb.Decoding;

var tests = new ProtocolTests();
tests.CrcAcceptsKnownGoodFrame();
tests.CrcReturnsKnownRemainderForBadFrame();
tests.DecodesAircraftIdentification();
tests.DecodesAirbornePositionAndAltitude();
tests.DecodesGlobalCprPosition();
tests.DecodesGroundSpeedVelocity();

Console.WriteLine("Protocol tests passed.");

internal sealed class ProtocolTests
{
    public void CrcAcceptsKnownGoodFrame()
    {
        var frame = Frame("8D406B902015A678D4D220AA4BDA");
        AssertEqual(0u, ModeSCrc.ComputeRemainder(frame.Data, frame.BitLength), "CRC good frame");
    }

    public void CrcReturnsKnownRemainderForBadFrame()
    {
        var frame = Frame("8D4CA251204994B1C36E60A5343D");
        AssertEqual(16u, ModeSCrc.ComputeRemainder(frame.Data, frame.BitLength), "CRC bad frame remainder");
    }

    public void DecodesAircraftIdentification()
    {
        var message = Decode("8D4840D6202CC371C32CE0576098");
        AssertEqual(true, message.CrcOk, "ident CRC");
        AssertEqual(17, message.DownlinkFormat, "ident DF");
        AssertEqual("4840D6", message.Icao, "ident ICAO");
        AssertEqual(4, message.TypeCode, "ident TC");
        AssertEqual("KLM1023", message.Callsign, "ident callsign");
        AssertEqual(0, message.Category, "ident category");
    }

    public void DecodesAirbornePositionAndAltitude()
    {
        var message = Decode("8D40621D58C382D690C8AC2863A7");
        AssertEqual(true, message.CrcOk, "position CRC");
        AssertEqual("40621D", message.Icao, "position ICAO");
        AssertEqual(11, message.TypeCode, "position TC");
        AssertEqual(38_000, message.AltitudeFeet, "position altitude");
        AssertEqual(false, message.Cpr?.IsOdd, "position CPR even");
        AssertEqual(93_000, message.Cpr?.EncodedLatitude, "position encoded lat");
        AssertEqual(51_372, message.Cpr?.EncodedLongitude, "position encoded lon");
    }

    public void DecodesGlobalCprPosition()
    {
        var tracker = new AircraftStateTracker();
        tracker.Apply(Decode("8D40621D58C382D690C8AC2863A7", DateTimeOffset.UnixEpoch));
        var snapshot = tracker.Apply(Decode("8D40621D58C386435CC412692AD6", DateTimeOffset.UnixEpoch.AddSeconds(1)));

        AssertNear(52.26578, snapshot?.Latitude, 0.0001, "CPR latitude");
        AssertNear(3.93891, snapshot?.Longitude, 0.0001, "CPR longitude");
    }

    public void DecodesGroundSpeedVelocity()
    {
        var message = Decode("8D485020994409940838175B284F");
        AssertEqual(true, message.CrcOk, "velocity CRC");
        AssertEqual(19, message.TypeCode, "velocity TC");
        AssertEqual(1, message.Velocity?.Subtype, "velocity subtype");
        AssertEqual(159, message.Velocity?.GroundSpeedKnots, "velocity ground speed");
        AssertNear(182.88, message.Velocity?.TrackDegrees, 0.01, "velocity track");
        AssertEqual(-832, message.Velocity?.VerticalRateFeetPerMinute, "velocity vertical rate");
        AssertEqual("baro", message.Velocity?.VerticalRateSource, "velocity vertical source");
    }

    private static ModeSMessage Decode(string hex, DateTimeOffset? receivedAt = null)
    {
        var decoder = new ModeSDecoder();
        return decoder.Decode(Frame(hex, receivedAt));
    }

    private static DemodulatedFrame Frame(string hex, DateTimeOffset? receivedAt = null) =>
        new(Convert.FromHexString(hex), hex.Length * 4, SignalDb: 12.5, receivedAt ?? DateTimeOffset.UnixEpoch);

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
        }
    }

    private static void AssertNear(double expected, double? actual, double tolerance, string label)
    {
        if (!actual.HasValue || Math.Abs(expected - actual.Value) > tolerance)
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
        }
    }
}

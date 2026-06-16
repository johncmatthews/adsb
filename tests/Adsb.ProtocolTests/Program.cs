using Adsb.Decoding;
using Adsb.Formatting;
using Adsb.Tracking;
using Microsoft.Data.Sqlite;

var tests = new ProtocolTests();
tests.CrcAcceptsKnownGoodFrame();
tests.CrcReturnsKnownRemainderForBadFrame();
tests.DecodesAircraftIdentification();
tests.DecodesAirbornePositionAndAltitude();
tests.DecodesGlobalCprPosition();
tests.DecodesGroundSpeedVelocity();
tests.DerivesUnitedStatesTailNumberFromIcao();
tests.LoadsTailNumberFromRegistryCsv();
tests.FormatsTailAndFlightIdentifiers();
tests.WatchlistMatchesJsonConfigByTailNumber();
tests.RecordsMatchedTelemetryToSqlite();

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

    public void DerivesUnitedStatesTailNumberFromIcao()
    {
        var resolver = AircraftIdentityResolver.Empty;
        AssertEqual("N1", resolver.ResolveTailNumber("A00001"), "US N-number start");
        AssertEqual("N999ZZ", resolver.ResolveTailNumber("ADF669"), "US N-number end");
        AssertEqual("N456TS", resolver.ResolveTailNumber("A58A20"), "US N-number vanity");
        AssertEqual("N97LM", resolver.ResolveTailNumber("AD8252"), "US N-number short suffix");
    }

    public void LoadsTailNumberFromRegistryCsv()
    {
        var path = Path.Combine(Path.GetTempPath(), $"adsb-registry-{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(path, "icao24,r\n4840d6,PH-BQP\n");
            var resolver = AircraftIdentityResolver.Load(path);
            AssertEqual("PH-BQP", resolver.ResolveTailNumber("4840D6"), "CSV registration lookup");
        }
        finally
        {
            File.Delete(path);
        }
    }

    public void FormatsTailAndFlightIdentifiers()
    {
        var path = Path.Combine(Path.GetTempPath(), $"adsb-registry-{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(path, "icao24,r\n4840D6,PH-BQP\n");
            var tracker = new AircraftStateTracker(identityResolver: AircraftIdentityResolver.Load(path));
            var message = Decode("8D4840D6202CC371C32CE0576098");
            var snapshot = tracker.Apply(message);
            var formatted = ConsoleMessageFormatter.Format(message, snapshot, includeRaw: false);

            AssertContains("tail=PH-BQP", formatted, "formatted tail");
            AssertContains("flight=KLM1023", formatted, "formatted flight");
            AssertDoesNotContain("callsign=KLM1023", formatted, "formatted redundant callsign");
        }
        finally
        {
            File.Delete(path);
        }
    }

    public void WatchlistMatchesJsonConfigByTailNumber()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "watchlist.json");
            File.WriteAllText(
                path,
                """
                {
                  "aircraft": [
                    { "label": "sample aircraft", "tail": "N456TS" }
                  ]
                }
                """);

            var watchlist = AircraftWatchlist.Load(path);
            var matched = watchlist.TryMatch(
                new AircraftIdentityTelemetry("A58A20", null, "N456TS", null),
                out var match);

            AssertEqual(true, matched, "watchlist tail match");
            AssertEqual("sample aircraft", match.Label, "watchlist label");
            AssertEqual("tail", match.IdentifierType, "watchlist identifier type");
            AssertEqual("N456TS", match.IdentifierValue, "watchlist identifier value");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public void RecordsMatchedTelemetryToSqlite()
    {
        var directory = CreateTempDirectory();
        try
        {
            var watchlistPath = Path.Combine(directory, "watchlist.json");
            var databasePath = Path.Combine(directory, "telemetry.sqlite");
            File.WriteAllText(
                watchlistPath,
                """
                {
                  "aircraft": [
                    { "label": "KLM sample", "flight": "KLM1023" }
                  ]
                }
                """);

            var tracker = new AircraftStateTracker();
            var message = Decode("8D4840D6202CC371C32CE0576098", DateTimeOffset.Parse("2026-06-16T12:34:56Z"));
            var snapshot = tracker.Apply(message);

            using (var recorder = WatchlistTelemetryRecorder.Open(watchlistPath, databasePath))
            {
                AssertEqual(true, recorder.TryRecord(message, snapshot), "telemetry recorded");
            }

            using var connection = new SqliteConnection($"Data Source={databasePath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT watchlist_label, matched_identifier_type, matched_identifier, icao, flight_number, raw_hex, type_code
                FROM watchlist_telemetry
                """;

            using var reader = command.ExecuteReader();
            AssertEqual(true, reader.Read(), "telemetry row exists");
            AssertEqual("KLM sample", reader.GetString(0), "telemetry label");
            AssertEqual("flight", reader.GetString(1), "telemetry match type");
            AssertEqual("KLM1023", reader.GetString(2), "telemetry match value");
            AssertEqual("4840D6", reader.GetString(3), "telemetry ICAO");
            AssertEqual("KLM1023", reader.GetString(4), "telemetry flight number");
            AssertEqual(message.RawHex, reader.GetString(5), "telemetry raw frame");
            AssertEqual(4, reader.GetInt32(6), "telemetry type code");
            AssertEqual(false, reader.Read(), "telemetry row count");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ModeSMessage Decode(string hex, DateTimeOffset? receivedAt = null)
    {
        var decoder = new ModeSDecoder();
        return decoder.Decode(Frame(hex, receivedAt));
    }

    private static DemodulatedFrame Frame(string hex, DateTimeOffset? receivedAt = null) =>
        new(Convert.FromHexString(hex), hex.Length * 4, SignalDb: 12.5, receivedAt ?? DateTimeOffset.UnixEpoch);

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"adsb-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

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

    private static void AssertContains(string expected, string actual, string label)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{label}: expected '{actual}' to contain '{expected}'");
        }
    }

    private static void AssertDoesNotContain(string unexpected, string actual, string label)
    {
        if (actual.Contains(unexpected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{label}: expected '{actual}' not to contain '{unexpected}'");
        }
    }
}

using System.Globalization;

namespace Adsb;

/// <summary>
/// Command-line configuration for the console ADS-B receiver and watchlist recorder.
/// </summary>
public sealed class AppOptions
{
    /// <summary>Zero-based RTL-SDR device index passed to librtlsdr.</summary>
    public uint DeviceIndex { get; private init; }
    /// <summary>Center frequency in hertz; ADS-B uses 1090 MHz by default.</summary>
    public uint FrequencyHz { get; private init; } = 1_090_000_000;
    /// <summary>Sample rate in samples per second; the demodulator currently supports 2 Msps.</summary>
    public uint SampleRate { get; private init; } = 2_000_000;
    /// <summary>Tuner gain selection, either automatic, maximum supported gain, or a manual tenth-dB value.</summary>
    public GainSetting Gain { get; private init; } = GainSetting.Max;
    /// <summary>Frequency correction applied by the tuner in parts per million.</summary>
    public int FrequencyCorrectionPpm { get; private init; }
    /// <summary>Number of asynchronous native buffers requested from librtlsdr.</summary>
    public uint AsyncBufferCount { get; private init; } = 16;
    /// <summary>Size in bytes of each asynchronous native sample buffer.</summary>
    public uint AsyncBufferSize { get; private init; } = 262_144;
    /// <summary>Optional explicit path to the native librtlsdr library.</summary>
    public string? DriverPath { get; private init; }
    /// <summary>Optional ICAO-to-registration CSV used to enrich aircraft identity output.</summary>
    public string? RegistryPath { get; private init; }
    /// <summary>Optional aircraft watchlist file that enables SQLite telemetry recording.</summary>
    public string? WatchlistPath { get; private init; }
    /// <summary>SQLite database path used when watchlist recording is enabled.</summary>
    public string TelemetryDatabasePath { get; private init; } = "adsb-watchlist.sqlite";
    /// <summary>Whether decoded frames with invalid CRC remainders should still be printed.</summary>
    public bool IncludeInvalidFrames { get; private init; }
    /// <summary>Whether non-ADS-B Mode S frames should be printed alongside extended squitter frames.</summary>
    public bool IncludeNonAdsbFrames { get; private init; }
    /// <summary>Whether console output includes raw hexadecimal frames.</summary>
    public bool ShowRawFrames { get; private init; }
    /// <summary>Whether startup should list visible RTL-SDR devices and exit.</summary>
    public bool ListDevices { get; private init; }
    /// <summary>Whether startup should print command-line usage and exit.</summary>
    public bool ShowHelp { get; private init; }
    /// <summary>Receiver latitude used as a reference for local CPR position recovery.</summary>
    public double? ReceiverLatitude { get; private init; }
    /// <summary>Receiver longitude used as a reference for local CPR position recovery.</summary>
    public double? ReceiverLongitude { get; private init; }

    /// <summary>
    /// Parses command-line switches into strongly typed receiver, decoder, and watchlist options.
    /// </summary>
    /// <param name="args">Command-line arguments after the executable name.</param>
    /// <returns>Validated application options ready to use by the console entry point.</returns>
    public static AppOptions Parse(string[] args)
    {
        var options = new AppOptions();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h":
                case "--help":
                    options = withHelp();
                    break;
                case "--list-devices":
                    options = withListDevices();
                    break;
                case "--device":
                    options = withDevice(ParseUInt(RequireValue(args, ref i, arg), arg));
                    break;
                case "--freq":
                case "--frequency":
                    options = withFrequency(ParseFrequency(RequireValue(args, ref i, arg), arg));
                    break;
                case "--sample-rate":
                    options = withSampleRate(ParseFrequency(RequireValue(args, ref i, arg), arg));
                    break;
                case "--gain":
                    options = withGain(GainSetting.Parse(RequireValue(args, ref i, arg)));
                    break;
                case "--ppm":
                    options = withPpm(ParseInt(RequireValue(args, ref i, arg), arg));
                    break;
                case "--buffers":
                    options = withBufferCount(ParseUInt(RequireValue(args, ref i, arg), arg));
                    break;
                case "--buffer-size":
                    options = withBufferSize(ParseUInt(RequireValue(args, ref i, arg), arg));
                    break;
                case "--driver":
                    options = withDriver(RequireValue(args, ref i, arg));
                    break;
                case "--registry":
                    options = withRegistry(RequireValue(args, ref i, arg));
                    break;
                case "--watchlist":
                    options = withWatchlist(RequireValue(args, ref i, arg));
                    break;
                case "--telemetry-db":
                    options = withTelemetryDatabase(RequireValue(args, ref i, arg));
                    break;
                case "--include-invalid":
                    options = withIncludeInvalid();
                    break;
                case "--all-mode-s":
                    options = withIncludeNonAdsb();
                    break;
                case "--raw":
                    options = withRaw();
                    break;
                case "--receiver-lat":
                    options = withReceiverLatitude(ParseDouble(RequireValue(args, ref i, arg), arg));
                    break;
                case "--receiver-lon":
                    options = withReceiverLongitude(ParseDouble(RequireValue(args, ref i, arg), arg));
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        options.Validate();
        return options;

        AppOptions withHelp() => Copy(help: true);
        AppOptions withListDevices() => Copy(listDevices: true);
        AppOptions withDevice(uint value) => Copy(deviceIndex: value);
        AppOptions withFrequency(uint value) => Copy(frequencyHz: value);
        AppOptions withSampleRate(uint value) => Copy(sampleRate: value);
        AppOptions withGain(GainSetting value) => Copy(gain: value);
        AppOptions withPpm(int value) => Copy(ppm: value);
        AppOptions withBufferCount(uint value) => Copy(bufferCount: value);
        AppOptions withBufferSize(uint value) => Copy(bufferSize: value);
        AppOptions withDriver(string value) => Copy(driverPath: value);
        AppOptions withRegistry(string value) => Copy(registryPath: value);
        AppOptions withWatchlist(string value) => Copy(watchlistPath: value);
        AppOptions withTelemetryDatabase(string value) => Copy(telemetryDatabasePath: value);
        AppOptions withIncludeInvalid() => Copy(includeInvalid: true);
        AppOptions withIncludeNonAdsb() => Copy(includeNonAdsb: true);
        AppOptions withRaw() => Copy(showRaw: true);
        AppOptions withReceiverLatitude(double value) => Copy(receiverLatitude: value);
        AppOptions withReceiverLongitude(double value) => Copy(receiverLongitude: value);

        AppOptions Copy(
            uint? deviceIndex = null,
            uint? frequencyHz = null,
            uint? sampleRate = null,
            GainSetting? gain = null,
            int? ppm = null,
            uint? bufferCount = null,
            uint? bufferSize = null,
            string? driverPath = null,
            string? registryPath = null,
            string? watchlistPath = null,
            string? telemetryDatabasePath = null,
            bool? includeInvalid = null,
            bool? includeNonAdsb = null,
            bool? showRaw = null,
            bool? listDevices = null,
            bool? help = null,
            double? receiverLatitude = null,
            double? receiverLongitude = null) =>
            new()
            {
                DeviceIndex = deviceIndex ?? options.DeviceIndex,
                FrequencyHz = frequencyHz ?? options.FrequencyHz,
                SampleRate = sampleRate ?? options.SampleRate,
                Gain = gain ?? options.Gain,
                FrequencyCorrectionPpm = ppm ?? options.FrequencyCorrectionPpm,
                AsyncBufferCount = bufferCount ?? options.AsyncBufferCount,
                AsyncBufferSize = bufferSize ?? options.AsyncBufferSize,
                DriverPath = driverPath ?? options.DriverPath,
                RegistryPath = registryPath ?? options.RegistryPath,
                WatchlistPath = watchlistPath ?? options.WatchlistPath,
                TelemetryDatabasePath = telemetryDatabasePath ?? options.TelemetryDatabasePath,
                IncludeInvalidFrames = includeInvalid ?? options.IncludeInvalidFrames,
                IncludeNonAdsbFrames = includeNonAdsb ?? options.IncludeNonAdsbFrames,
                ShowRawFrames = showRaw ?? options.ShowRawFrames,
                ListDevices = listDevices ?? options.ListDevices,
                ShowHelp = help ?? options.ShowHelp,
                ReceiverLatitude = receiverLatitude ?? options.ReceiverLatitude,
                ReceiverLongitude = receiverLongitude ?? options.ReceiverLongitude
            };
    }

    /// <summary>
    /// Writes the user-facing command-line help text, including native driver setup hints.
    /// </summary>
    public static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("ADS-B RTL-SDR decoder");
        writer.WriteLine();
        writer.WriteLine("Usage:");
        writer.WriteLine("  dotnet run --project src/Adsb.Console -- [options]");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  --list-devices             List RTL-SDR devices and exit");
        writer.WriteLine("  --device <index>           RTL-SDR device index (default: 0)");
        writer.WriteLine("  --freq <hz|mhz|ghz>        Center frequency (default: 1090MHz)");
        writer.WriteLine("  --sample-rate <hz>         Sample rate; MVP demodulator expects 2000000");
        writer.WriteLine("  --gain <auto|max|db>       Tuner gain (default: max)");
        writer.WriteLine("  --ppm <value>              Frequency correction in PPM");
        writer.WriteLine("  --driver <path>            Explicit path to librtlsdr");
        writer.WriteLine("  --registry <csv>           Optional ICAO-to-tail CSV registry");
        writer.WriteLine("  --watchlist <file>         Aircraft watchlist config file");
        writer.WriteLine("  --telemetry-db <path>      SQLite file for watchlist telemetry");
        writer.WriteLine("  --receiver-lat <degrees>   Receiver latitude for local CPR fallback");
        writer.WriteLine("  --receiver-lon <degrees>   Receiver longitude for local CPR fallback");
        writer.WriteLine("  --raw                      Include raw Mode S frame hex in output");
        writer.WriteLine("  --include-invalid          Print frames even when CRC fails");
        writer.WriteLine("  --all-mode-s               Print non-ADS-B Mode S frames too");
        writer.WriteLine("  -h, --help                 Show help");
        writer.WriteLine();
        writer.WriteLine("Native dependency:");
        writer.WriteLine("  macOS:   brew install librtlsdr");
        writer.WriteLine("  Linux:   install librtlsdr / rtl-sdr from your package manager");
        writer.WriteLine("  Windows: place rtlsdr.dll where the app can load it, or pass --driver");
    }

    /// <summary>
    /// Validates option combinations that cannot be checked while individual switches are parsed.
    /// </summary>
    private void Validate()
    {
        if (SampleRate != 2_000_000)
        {
            throw new ArgumentException("The MVP demodulator currently expects --sample-rate 2000000.");
        }

        if (AsyncBufferCount == 0)
        {
            throw new ArgumentException("--buffers must be greater than zero.");
        }

        if (AsyncBufferSize < 16_384)
        {
            throw new ArgumentException("--buffer-size must be at least 16384 bytes.");
        }

        if (ReceiverLatitude is < -90 or > 90)
        {
            throw new ArgumentException("--receiver-lat must be between -90 and 90.");
        }

        if (ReceiverLongitude is < -180 or > 180)
        {
            throw new ArgumentException("--receiver-lon must be between -180 and 180.");
        }

        if (ReceiverLatitude.HasValue != ReceiverLongitude.HasValue)
        {
            throw new ArgumentException("Pass both --receiver-lat and --receiver-lon, or neither.");
        }

        if (!string.IsNullOrWhiteSpace(RegistryPath) && !File.Exists(RegistryPath))
        {
            throw new ArgumentException($"--registry file does not exist: {RegistryPath}");
        }

        if (!string.IsNullOrWhiteSpace(WatchlistPath) && !File.Exists(WatchlistPath))
        {
            throw new ArgumentException($"--watchlist file does not exist: {WatchlistPath}");
        }

        if (!string.IsNullOrWhiteSpace(TelemetryDatabasePath))
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(TelemetryDatabasePath));
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                throw new ArgumentException($"--telemetry-db directory does not exist: {directory}");
            }
        }
    }

    /// <summary>
    /// Reads the next command-line token as an option value and advances the parser index.
    /// </summary>
    private static string RequireValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        index++;
        return args[index];
    }

    /// <summary>
    /// Parses unsigned integer option values using invariant culture so CLI behavior is locale independent.
    /// </summary>
    private static uint ParseUInt(string value, string option)
    {
        if (!uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new ArgumentException($"{option} expects an unsigned integer.");
        }

        return parsed;
    }

    /// <summary>
    /// Parses signed integer option values using invariant culture so CLI behavior is locale independent.
    /// </summary>
    private static int ParseInt(string value, string option)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new ArgumentException($"{option} expects an integer.");
        }

        return parsed;
    }

    /// <summary>
    /// Parses floating-point option values such as receiver coordinates using invariant culture.
    /// </summary>
    private static double ParseDouble(string value, string option)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new ArgumentException($"{option} expects a number.");
        }

        return parsed;
    }

    /// <summary>
    /// Parses a frequency string with optional kHz, MHz, or GHz suffix and returns hertz.
    /// </summary>
    private static uint ParseFrequency(string value, string option)
    {
        var normalized = value.Trim().ToLowerInvariant();
        var multiplier = 1.0;

        if (normalized.EndsWith("ghz", StringComparison.Ordinal))
        {
            multiplier = 1_000_000_000;
            normalized = normalized[..^3];
        }
        else if (normalized.EndsWith("mhz", StringComparison.Ordinal))
        {
            multiplier = 1_000_000;
            normalized = normalized[..^3];
        }
        else if (normalized.EndsWith("khz", StringComparison.Ordinal))
        {
            multiplier = 1_000;
            normalized = normalized[..^3];
        }
        else if (normalized.EndsWith('g'))
        {
            multiplier = 1_000_000_000;
            normalized = normalized[..^1];
        }
        else if (normalized.EndsWith('m'))
        {
            multiplier = 1_000_000;
            normalized = normalized[..^1];
        }
        else if (normalized.EndsWith('k'))
        {
            multiplier = 1_000;
            normalized = normalized[..^1];
        }

        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new ArgumentException($"{option} expects a frequency such as 1090M or 2000000.");
        }

        var hz = parsed * multiplier;
        if (hz <= 0 || hz > uint.MaxValue)
        {
            throw new ArgumentException($"{option} is outside the supported frequency range.");
        }

        return (uint)Math.Round(hz);
    }
}

/// <summary>
/// Tuner gain mode requested from librtlsdr.
/// </summary>
public enum GainMode
{
    /// <summary>Let the tuner choose gain automatically.</summary>
    Auto,
    /// <summary>Use the highest tuner gain reported by the device.</summary>
    Max,
    /// <summary>Use the manual tenth-dB gain value carried by <see cref="GainSetting"/>.</summary>
    Manual
}

/// <summary>
/// Gain selection used when configuring the RTL-SDR tuner.
/// </summary>
/// <param name="Mode">How the gain should be selected.</param>
/// <param name="TenthDb">Manual gain in tenths of a decibel; ignored for automatic and max modes.</param>
public readonly record struct GainSetting(GainMode Mode, int TenthDb)
{
    /// <summary>Automatic tuner gain setting.</summary>
    public static GainSetting Auto { get; } = new(GainMode.Auto, 0);
    /// <summary>Highest available tuner gain setting.</summary>
    public static GainSetting Max { get; } = new(GainMode.Max, 0);

    /// <summary>
    /// Parses CLI gain text into automatic, maximum, or manual tenth-dB gain settings.
    /// </summary>
    public static GainSetting Parse(string value)
    {
        if (value.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return Auto;
        }

        if (value.Equals("max", StringComparison.OrdinalIgnoreCase))
        {
            return Max;
        }

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var db))
        {
            throw new ArgumentException("--gain expects auto, max, or a gain in dB.");
        }

        return new GainSetting(GainMode.Manual, (int)Math.Round(db * 10));
    }

    /// <summary>
    /// Formats the gain setting using the same vocabulary accepted by the command line.
    /// </summary>
    public override string ToString() =>
        Mode switch
        {
            GainMode.Auto => "auto",
            GainMode.Max => "max",
            _ => $"{TenthDb / 10.0:F1} dB"
        };
}

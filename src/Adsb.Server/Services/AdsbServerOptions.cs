namespace Adsb.Server.Services;

/// <summary>
/// Root configuration section for the ADS-B server, covering capture, identity, replay, and compatibility outputs.
/// </summary>
public sealed class AdsbServerOptions
{
    /// <summary>RTL-SDR capture pipeline settings.</summary>
    public CaptureOptions Capture { get; set; } = new();
    /// <summary>Aircraft identity enrichment settings.</summary>
    public IdentityOptions Identity { get; set; } = new();
    /// <summary>Watchlist JSON file settings.</summary>
    public WatchlistOptions Watchlist { get; set; } = new();
    /// <summary>Replay database settings.</summary>
    public ReplayOptions Replay { get; set; } = new();
    /// <summary>Optional SBS, Beast, and JSON lines TCP output settings.</summary>
    public CompatibilityOptions Compatibility { get; set; } = new();
}

/// <summary>
/// Configuration for opening and filtering the RTL-SDR ADS-B capture stream.
/// </summary>
public sealed class CaptureOptions
{
    /// <summary>Whether the server should open the RTL-SDR device on startup.</summary>
    public bool Enabled { get; set; }
    /// <summary>Zero-based RTL-SDR device index.</summary>
    public uint DeviceIndex { get; set; }
    /// <summary>Center frequency in hertz; ADS-B uses 1090 MHz by default.</summary>
    public uint FrequencyHz { get; set; } = 1_090_000_000;
    /// <summary>Sample rate in samples per second; the current demodulator expects 2 Msps.</summary>
    public uint SampleRate { get; set; } = 2_000_000;
    /// <summary>Tuner gain setting accepted by the shared console parser, such as auto, max, or a dB value.</summary>
    public string Gain { get; set; } = "max";
    /// <summary>Frequency correction in parts per million.</summary>
    public int Ppm { get; set; }
    /// <summary>Number of native async buffers used by librtlsdr.</summary>
    public uint BufferCount { get; set; } = 16;
    /// <summary>Size in bytes of each native async buffer.</summary>
    public uint BufferSize { get; set; } = 262_144;
    /// <summary>Optional explicit path to the native librtlsdr library.</summary>
    public string? DriverPath { get; set; }
    /// <summary>Receiver latitude used for local CPR fallback position decoding.</summary>
    public double? ReceiverLatitude { get; set; }
    /// <summary>Receiver longitude used for local CPR fallback position decoding.</summary>
    public double? ReceiverLongitude { get; set; }
    /// <summary>Whether invalid CRC frames should still be published.</summary>
    public bool IncludeInvalidFrames { get; set; }
    /// <summary>Whether non-ADS-B Mode S frames should be published.</summary>
    public bool IncludeNonAdsbFrames { get; set; }
}

/// <summary>
/// Configuration for optional aircraft registration lookup data.
/// </summary>
public sealed class IdentityOptions
{
    /// <summary>Optional ICAO-to-registration CSV path.</summary>
    public string? RegistryPath { get; set; }
}

/// <summary>
/// Configuration for the mutable server watchlist file.
/// </summary>
public sealed class WatchlistOptions
{
    /// <summary>Path to the JSON watchlist file read and written by the REST API.</summary>
    public string Path { get; set; } = "watchlist.json";
}

/// <summary>
/// Configuration for durable watchlist replay storage.
/// </summary>
public sealed class ReplayOptions
{
    /// <summary>SQLite database path used for matched watchlist telemetry.</summary>
    public string DatabasePath { get; set; } = "adsb-watchlist.sqlite";
}

/// <summary>
/// Optional TCP listener ports for compatibility feeds.
/// </summary>
public sealed class CompatibilityOptions
{
    /// <summary>SBS/BaseStation text output port, or null to disable it.</summary>
    public int? SbsTcpPort { get; set; }
    /// <summary>Newline-delimited JSON output port, or null to disable it.</summary>
    public int? JsonLinesTcpPort { get; set; }
    /// <summary>Mode S Beast binary output port, or null to disable it.</summary>
    public int? BeastTcpPort { get; set; }
}

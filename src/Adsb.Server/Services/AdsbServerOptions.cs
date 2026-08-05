namespace Adsb.Server.Services;

public sealed class AdsbServerOptions
{
    public CaptureOptions Capture { get; set; } = new();
    public IdentityOptions Identity { get; set; } = new();
    public WatchlistOptions Watchlist { get; set; } = new();
    public ReplayOptions Replay { get; set; } = new();
    public CompatibilityOptions Compatibility { get; set; } = new();
}

public sealed class CaptureOptions
{
    public bool Enabled { get; set; }
    public uint DeviceIndex { get; set; }
    public uint FrequencyHz { get; set; } = 1_090_000_000;
    public uint SampleRate { get; set; } = 2_000_000;
    public string Gain { get; set; } = "max";
    public int Ppm { get; set; }
    public uint BufferCount { get; set; } = 16;
    public uint BufferSize { get; set; } = 262_144;
    public string? DriverPath { get; set; }
    public double? ReceiverLatitude { get; set; }
    public double? ReceiverLongitude { get; set; }
    public bool IncludeInvalidFrames { get; set; }
    public bool IncludeNonAdsbFrames { get; set; }
}

public sealed class IdentityOptions
{
    public string? RegistryPath { get; set; }
}

public sealed class WatchlistOptions
{
    public string Path { get; set; } = "watchlist.json";
}

public sealed class ReplayOptions
{
    public string DatabasePath { get; set; } = "adsb-watchlist.sqlite";
}

public sealed class CompatibilityOptions
{
    public int? SbsTcpPort { get; set; }
    public int? JsonLinesTcpPort { get; set; }
    public int? BeastTcpPort { get; set; }
}

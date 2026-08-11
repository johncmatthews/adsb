var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<AdsbClientOptions>(builder.Configuration.GetSection("AdsbClient"));

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapGet(
    "/api/config",
    (Microsoft.Extensions.Options.IOptions<AdsbClientOptions> options) =>
        Results.Ok(options.Value));
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>
/// Configuration exposed to the static web client so it can connect to the ADS-B server and center the radar display.
/// </summary>
public sealed class AdsbClientOptions
{
    /// <summary>Default ADS-B server base URL shown in the client connection field.</summary>
    public string DefaultServerUrl { get; set; } = "http://127.0.0.1:5087";
    /// <summary>Receiver location and radar range used by the browser map.</summary>
    public ReceiverOptions Receiver { get; set; } = new();
}

/// <summary>
/// Receiver metadata used by the web radar display to place aircraft relative to the local station.
/// </summary>
public sealed class ReceiverOptions
{
    /// <summary>User-facing receiver label displayed at the center of the radar.</summary>
    public string Label { get; set; } = "Receiver";
    /// <summary>Receiver latitude in degrees, or null when the radar should show an unconfigured state.</summary>
    public double? Latitude { get; set; }
    /// <summary>Receiver longitude in degrees, or null when the radar should show an unconfigured state.</summary>
    public double? Longitude { get; set; }
    /// <summary>Maximum radar range in nautical miles.</summary>
    public double RangeNauticalMiles { get; set; } = 150;
}

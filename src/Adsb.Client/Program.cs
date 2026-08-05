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

public sealed class AdsbClientOptions
{
    public string DefaultServerUrl { get; set; } = "http://127.0.0.1:5087";
    public ReceiverOptions Receiver { get; set; } = new();
}

public sealed class ReceiverOptions
{
    public string Label { get; set; } = "Receiver";
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double RangeNauticalMiles { get; set; } = 150;
}

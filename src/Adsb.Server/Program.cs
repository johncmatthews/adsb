using System.Text.Json;
using Adsb.Server.Compatibility;
using Adsb.Server.Contracts;
using Adsb.Server.Hubs;
using Adsb.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AdsbServerOptions>(builder.Configuration.GetSection("Adsb"));
builder.Services.AddCors(
    options => options.AddDefaultPolicy(
        policy => policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin()));
builder.Services.AddSignalR();
builder.Services.AddSingleton<AircraftSnapshotStore>();
builder.Services.AddSingleton<TelemetryEventBus>();
builder.Services.AddSingleton<WatchlistConfigStore>();
builder.Services.AddSingleton<ReplayStore>();
builder.Services.AddSingleton<CaptureStatusService>();
builder.Services.AddHostedService<AdsbCaptureHostedService>();
builder.Services.AddHostedService<CompatibilityTcpServerHostedService>();

var app = builder.Build();

app.UseCors();

app.MapGet("/", () => Results.Redirect("/api/status"));
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/status", (CaptureStatusService status) => status.GetStatus());
app.MapGet("/api/stats", (CaptureStatusService status) => status.GetStatus());

app.MapHub<AdsbHub>("/hubs/adsb");

var aircraftApi = app.MapGroup("/api/aircraft");
aircraftApi.MapGet("/", (AircraftSnapshotStore aircraft) => aircraft.GetAll());
aircraftApi.MapGet(
    "/{identifier}",
    (string identifier, AircraftSnapshotStore aircraft) =>
        aircraft.TryGet(identifier, out var telemetry)
            ? Results.Ok(telemetry)
            : Results.NotFound());

var watchlistApi = app.MapGroup("/api/watchlist");
watchlistApi.MapGet("/", (WatchlistConfigStore watchlist) => watchlist.GetAll());
watchlistApi.MapPost(
    "/",
    (UpsertWatchlistAircraftRequest request, WatchlistConfigStore watchlist) =>
    {
        var item = watchlist.Add(request);
        return Results.Created($"/api/watchlist/{item.Id}", item);
    });
watchlistApi.MapPut(
    "/{id}",
    (string id, UpsertWatchlistAircraftRequest request, WatchlistConfigStore watchlist) =>
        watchlist.TryUpdate(id, request, out var item)
            ? Results.Ok(item)
            : Results.NotFound());
watchlistApi.MapDelete(
    "/{id}",
    (string id, WatchlistConfigStore watchlist) =>
        watchlist.Delete(id) ? Results.NoContent() : Results.NotFound());

var replayApi = app.MapGroup("/api/replay");
replayApi.MapGet(
    "/sessions",
    async (ReplayStore replayStore, CancellationToken cancellationToken) =>
        await replayStore.GetSessionsAsync(cancellationToken));
replayApi.MapGet(
    "/events",
    async (
        string? icao,
        string? tailNumber,
        string? flightNumber,
        string? callsign,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int? limit,
        ReplayStore replayStore,
        CancellationToken cancellationToken) =>
        await replayStore.QueryEventsAsync(
            new ReplayQuery(icao, tailNumber, flightNumber, callsign, fromUtc, toUtc, limit ?? 1000),
            cancellationToken));

var compatApi = app.MapGroup("/compat");
compatApi.MapGet(
    "/jsonl",
    async (HttpContext context, TelemetryEventBus eventBus) =>
    {
        context.Response.ContentType = "application/x-ndjson";
        await foreach (var telemetry in eventBus.Subscribe(context.RequestAborted).ReadAllAsync(context.RequestAborted))
        {
            await JsonSerializer.SerializeAsync(context.Response.Body, telemetry, cancellationToken: context.RequestAborted);
            await context.Response.WriteAsync("\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
        }
    });
compatApi.MapGet(
    "/sbs",
    async (HttpContext context, TelemetryEventBus eventBus) =>
    {
        context.Response.ContentType = "text/plain";
        await foreach (var telemetry in eventBus.Subscribe(context.RequestAborted).ReadAllAsync(context.RequestAborted))
        {
            await context.Response.WriteAsync(CompatibilityFormatters.ToSbsLine(telemetry), context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
        }
    });
compatApi.MapGet(
    "/beast",
    async (HttpContext context, TelemetryEventBus eventBus) =>
    {
        context.Response.ContentType = "application/octet-stream";
        await foreach (var telemetry in eventBus.Subscribe(context.RequestAborted).ReadAllAsync(context.RequestAborted))
        {
            await context.Response.Body.WriteAsync(CompatibilityFormatters.ToBeastFrame(telemetry), context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
        }
    });

app.Run();

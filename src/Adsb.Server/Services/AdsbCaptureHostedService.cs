using Adsb;
using Adsb.Decoding;
using Adsb.Radio;
using Adsb.Server.Contracts;
using Adsb.Server.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Adsb.Server.Services;

public sealed class AdsbCaptureHostedService : BackgroundService
{
    private readonly IOptions<AdsbServerOptions> options;
    private readonly AircraftSnapshotStore aircraft;
    private readonly TelemetryEventBus eventBus;
    private readonly WatchlistConfigStore watchlist;
    private readonly ReplayStore replayStore;
    private readonly CaptureStatusService status;
    private readonly IHubContext<AdsbHub> hubContext;
    private readonly ILogger<AdsbCaptureHostedService> logger;

    public AdsbCaptureHostedService(
        IOptions<AdsbServerOptions> options,
        AircraftSnapshotStore aircraft,
        TelemetryEventBus eventBus,
        WatchlistConfigStore watchlist,
        ReplayStore replayStore,
        CaptureStatusService status,
        IHubContext<AdsbHub> hubContext,
        ILogger<AdsbCaptureHostedService> logger)
    {
        this.options = options;
        this.aircraft = aircraft;
        this.eventBus = eventBus;
        this.watchlist = watchlist;
        this.replayStore = replayStore;
        this.status = status;
        this.hubContext = hubContext;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var serverOptions = options.Value;
        status.SetCaptureEnabled(serverOptions.Capture.Enabled);
        await replayStore.EnsureInitializedAsync(stoppingToken);

        if (!serverOptions.Capture.Enabled)
        {
            logger.LogInformation("ADS-B capture is disabled. Set Adsb:Capture:Enabled=true to open the RTL-SDR device.");
            return;
        }

        try
        {
            await RunCaptureAsync(serverOptions, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            status.SetDeviceConnected(false);
        }
        catch (Exception ex)
        {
            status.SetError(ex);
            logger.LogError(ex, "ADS-B capture stopped.");
        }
    }

    private async Task RunCaptureAsync(AdsbServerOptions serverOptions, CancellationToken stoppingToken)
    {
        var captureOptions = BuildAppOptions(serverOptions.Capture);
        RtlSdrNative.Configure(captureOptions.DriverPath);

        var identityResolver = AircraftIdentityResolver.Load(serverOptions.Identity.RegistryPath);
        using var device = RtlSdrDevice.Open(captureOptions);
        device.Configure();
        status.SetDeviceConnected(true);

        var demodulator = new AdsbDemodulator(captureOptions.SampleRate);
        var decoder = new ModeSDecoder();
        var tracker = new AircraftStateTracker(
            captureOptions.ReceiverLatitude,
            captureOptions.ReceiverLongitude,
            identityResolver);

        logger.LogInformation(
            "ADS-B capture listening on {FrequencyHz} Hz at {SampleRate} samples/s.",
            captureOptions.FrequencyHz,
            captureOptions.SampleRate);

        await device.RunAsync(
            async (samples, token) =>
            {
                var receivedAt = DateTimeOffset.UtcNow;
                foreach (var frame in demodulator.Process(samples.Span, receivedAt))
                {
                    var message = decoder.Decode(frame);
                    status.RecordDecodedFrame();
                    if (!ShouldPublish(message, captureOptions))
                    {
                        continue;
                    }

                    var snapshot = tracker.Apply(message);
                    var telemetry = AircraftTelemetryMapper.Map(message, snapshot);
                    aircraft.Upsert(telemetry);
                    await eventBus.PublishAsync(telemetry);
                    status.RecordPublishedEvent(telemetry.ReceivedAtUtc);
                    await hubContext.Clients.All.SendAsync("aircraftUpdated", telemetry, token);

                    if (watchlist.TryMatch(telemetry, out var match))
                    {
                        await replayStore.RecordAsync(telemetry, match, token);
                        status.RecordWatchlistEvent();
                        await hubContext.Clients.All.SendAsync(
                            "watchlistTelemetry",
                            new WatchlistMatchEvent(
                                telemetry.ReceivedAtUtc,
                                match.Label,
                                match.IdentifierType,
                                match.IdentifierValue,
                                telemetry),
                            token);
                    }
                }
            },
            stoppingToken);
    }

    private static bool ShouldPublish(ModeSMessage message, AppOptions options)
    {
        if (!options.IncludeInvalidFrames && !message.CrcOk)
        {
            return false;
        }

        if (!options.IncludeNonAdsbFrames && !message.IsExtendedSquitter)
        {
            return false;
        }

        return true;
    }

    private static AppOptions BuildAppOptions(CaptureOptions capture)
    {
        var args = new List<string>
        {
            "--device",
            capture.DeviceIndex.ToString(),
            "--freq",
            capture.FrequencyHz.ToString(),
            "--sample-rate",
            capture.SampleRate.ToString(),
            "--gain",
            capture.Gain,
            "--buffers",
            capture.BufferCount.ToString(),
            "--buffer-size",
            capture.BufferSize.ToString()
        };

        if (capture.Ppm != 0)
        {
            args.Add("--ppm");
            args.Add(capture.Ppm.ToString());
        }

        if (!string.IsNullOrWhiteSpace(capture.DriverPath))
        {
            args.Add("--driver");
            args.Add(capture.DriverPath);
        }

        if (capture.ReceiverLatitude.HasValue && capture.ReceiverLongitude.HasValue)
        {
            args.Add("--receiver-lat");
            args.Add(capture.ReceiverLatitude.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            args.Add("--receiver-lon");
            args.Add(capture.ReceiverLongitude.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (capture.IncludeInvalidFrames)
        {
            args.Add("--include-invalid");
        }

        if (capture.IncludeNonAdsbFrames)
        {
            args.Add("--all-mode-s");
        }

        return AppOptions.Parse(args.ToArray());
    }
}

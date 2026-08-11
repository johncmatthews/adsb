using System.Net;
using System.Net.Sockets;
using System.Text;
using Adsb.Server.Contracts;
using Adsb.Server.Services;
using Microsoft.Extensions.Options;

namespace Adsb.Server.Compatibility;

/// <summary>
/// Hosts optional TCP compatibility feeds that stream live telemetry in JSONL, SBS, or Beast formats.
/// </summary>
public sealed class CompatibilityTcpServerHostedService : BackgroundService
{
    private readonly TelemetryEventBus eventBus;
    private readonly CompatibilityOptions options;
    private readonly ILogger<CompatibilityTcpServerHostedService> logger;

    /// <summary>
    /// Creates the compatibility server with the live telemetry bus and configured listener ports.
    /// </summary>
    public CompatibilityTcpServerHostedService(
        TelemetryEventBus eventBus,
        IOptions<AdsbServerOptions> options,
        ILogger<CompatibilityTcpServerHostedService> logger)
    {
        this.eventBus = eventBus;
        this.options = options.Value.Compatibility;
        this.logger = logger;
    }

    /// <summary>
    /// Starts one TCP listener per enabled compatibility output and keeps them running until shutdown.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tasks = new List<Task>();
        if (options.JsonLinesTcpPort.HasValue)
        {
            tasks.Add(RunTextServerAsync("JSONL", options.JsonLinesTcpPort.Value, CompatibilityFormatters.ToJsonLine, stoppingToken));
        }

        if (options.SbsTcpPort.HasValue)
        {
            tasks.Add(RunTextServerAsync("SBS", options.SbsTcpPort.Value, CompatibilityFormatters.ToSbsLine, stoppingToken));
        }

        if (options.BeastTcpPort.HasValue)
        {
            tasks.Add(RunBinaryServerAsync("Beast", options.BeastTcpPort.Value, CompatibilityFormatters.ToBeastFrame, stoppingToken));
        }

        if (tasks.Count == 0)
        {
            return;
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Starts a text compatibility listener whose clients receive formatted telemetry lines.
    /// </summary>
    private async Task RunTextServerAsync(
        string name,
        int port,
        Func<AircraftTelemetryEvent, string> formatter,
        CancellationToken stoppingToken)
    {
        await RunServerAsync(
            name,
            port,
            async (client, token) =>
            {
                await using var stream = client.GetStream();
                await using var writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true };
                await foreach (var telemetry in eventBus.Subscribe(token).ReadAllAsync(token))
                {
                    await writer.WriteAsync(formatter(telemetry));
                }
            },
            stoppingToken);
    }

    /// <summary>
    /// Starts a binary compatibility listener whose clients receive encoded telemetry frames.
    /// </summary>
    private async Task RunBinaryServerAsync(
        string name,
        int port,
        Func<AircraftTelemetryEvent, byte[]> formatter,
        CancellationToken stoppingToken)
    {
        await RunServerAsync(
            name,
            port,
            async (client, token) =>
            {
                await using var stream = client.GetStream();
                await foreach (var telemetry in eventBus.Subscribe(token).ReadAllAsync(token))
                {
                    var frame = formatter(telemetry);
                    await stream.WriteAsync(frame, token);
                    await stream.FlushAsync(token);
                }
            },
            stoppingToken);
    }

    /// <summary>
    /// Accepts TCP clients for one compatibility feed and runs each client handler on its own task.
    /// </summary>
    private async Task RunServerAsync(
        string name,
        int port,
        Func<TcpClient, CancellationToken, Task> clientHandler,
        CancellationToken stoppingToken)
    {
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        logger.LogInformation("{Name} compatibility TCP output listening on port {Port}.", name, port);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken);
                _ = Task.Run(
                    async () =>
                    {
                        try
                        {
                            using (client)
                            {
                                await clientHandler(client, stoppingToken);
                            }
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                        {
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "{Name} compatibility client disconnected with an error.", name);
                        }
                    },
                    stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            listener.Stop();
        }
    }
}

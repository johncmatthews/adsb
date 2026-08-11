using System.Collections.Concurrent;
using System.Threading.Channels;
using Adsb.Server.Contracts;

namespace Adsb.Server.Services;

/// <summary>
/// In-process fan-out bus for normalized aircraft telemetry events.
/// </summary>
public sealed class TelemetryEventBus
{
    private readonly ConcurrentDictionary<Guid, Channel<AircraftTelemetryEvent>> subscribers = new();

    /// <summary>
    /// Creates a bounded subscription channel that receives future telemetry events until the cancellation token fires.
    /// </summary>
    /// <remarks>
    /// Subscriptions drop the oldest buffered events under backpressure so slow realtime clients cannot stall capture.
    /// Durable watchlist replay uses <see cref="ReplayStore"/> instead.
    /// </remarks>
    public ChannelReader<AircraftTelemetryEvent> Subscribe(CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<AircraftTelemetryEvent>(
            new BoundedChannelOptions(512)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });

        subscribers[id] = channel;
        cancellationToken.Register(
            static state =>
            {
                var (bus, subscriberId) = ((TelemetryEventBus, Guid))state!;
                if (bus.subscribers.TryRemove(subscriberId, out var removed))
                {
                    removed.Writer.TryComplete();
                }
            },
            (this, id));

        return channel.Reader;
    }

    /// <summary>
    /// Publishes one telemetry event to all current subscribers without waiting for slow consumers.
    /// </summary>
    public ValueTask PublishAsync(AircraftTelemetryEvent telemetry)
    {
        foreach (var subscriber in subscribers.Values)
        {
            subscriber.Writer.TryWrite(telemetry);
        }

        return ValueTask.CompletedTask;
    }
}

using System.Collections.Concurrent;
using System.Threading.Channels;
using Adsb.Server.Contracts;

namespace Adsb.Server.Services;

public sealed class TelemetryEventBus
{
    private readonly ConcurrentDictionary<Guid, Channel<AircraftTelemetryEvent>> subscribers = new();

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

    public ValueTask PublishAsync(AircraftTelemetryEvent telemetry)
    {
        foreach (var subscriber in subscribers.Values)
        {
            subscriber.Writer.TryWrite(telemetry);
        }

        return ValueTask.CompletedTask;
    }
}

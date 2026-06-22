using Adsb.Server.Contracts;

namespace Adsb.Server.Services;

public sealed class CaptureStatusService
{
    private readonly DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;
    private long framesDecoded;
    private long eventsPublished;
    private long watchlistEventsRecorded;
    private bool captureEnabled;
    private bool deviceConnected;
    private DateTimeOffset? lastEventAtUtc;
    private string? lastError;

    public void SetCaptureEnabled(bool enabled) => captureEnabled = enabled;
    public void SetDeviceConnected(bool connected) => deviceConnected = connected;

    public void RecordDecodedFrame()
    {
        Interlocked.Increment(ref framesDecoded);
    }

    public void RecordPublishedEvent(DateTimeOffset receivedAtUtc)
    {
        Interlocked.Increment(ref eventsPublished);
        lastEventAtUtc = receivedAtUtc;
    }

    public void RecordWatchlistEvent()
    {
        Interlocked.Increment(ref watchlistEventsRecorded);
    }

    public void SetError(Exception exception)
    {
        lastError = exception.Message;
        deviceConnected = false;
    }

    public ServerStatusDto GetStatus() =>
        new(
            startedAtUtc,
            captureEnabled,
            deviceConnected,
            Interlocked.Read(ref framesDecoded),
            Interlocked.Read(ref eventsPublished),
            Interlocked.Read(ref watchlistEventsRecorded),
            lastEventAtUtc,
            lastError);
}

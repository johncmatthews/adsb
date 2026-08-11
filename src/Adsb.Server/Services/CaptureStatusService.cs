using Adsb.Server.Contracts;

namespace Adsb.Server.Services;

/// <summary>
/// Tracks server capture counters and device state for health/status endpoints and SignalR clients.
/// </summary>
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

    /// <summary>
    /// Records whether the server configuration enables RTL-SDR capture.
    /// </summary>
    public void SetCaptureEnabled(bool enabled) => captureEnabled = enabled;
    /// <summary>
    /// Records whether the capture service currently has an open RTL-SDR device.
    /// </summary>
    public void SetDeviceConnected(bool connected) => deviceConnected = connected;

    /// <summary>
    /// Increments the count of frames that passed demodulation and were decoded into Mode S messages.
    /// </summary>
    public void RecordDecodedFrame()
    {
        Interlocked.Increment(ref framesDecoded);
    }

    /// <summary>
    /// Increments the published telemetry counter and records the timestamp of the newest event.
    /// </summary>
    public void RecordPublishedEvent(DateTimeOffset receivedAtUtc)
    {
        Interlocked.Increment(ref eventsPublished);
        lastEventAtUtc = receivedAtUtc;
    }

    /// <summary>
    /// Increments the durable watchlist capture counter after a matched event is written.
    /// </summary>
    public void RecordWatchlistEvent()
    {
        Interlocked.Increment(ref watchlistEventsRecorded);
    }

    /// <summary>
    /// Stores the latest capture failure message and marks the RTL-SDR device as disconnected.
    /// </summary>
    public void SetError(Exception exception)
    {
        lastError = exception.Message;
        deviceConnected = false;
    }

    /// <summary>
    /// Returns a point-in-time status DTO using atomic reads for counters updated by the capture loop.
    /// </summary>
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

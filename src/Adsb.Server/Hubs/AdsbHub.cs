using Adsb.Server.Contracts;
using Adsb.Server.Services;
using Microsoft.AspNetCore.SignalR;

namespace Adsb.Server.Hubs;

/// <summary>
/// SignalR hub exposing live ADS-B aircraft telemetry and server status to web clients.
/// </summary>
public sealed class AdsbHub : Hub
{
    private readonly AircraftSnapshotStore aircraft;
    private readonly CaptureStatusService status;

    /// <summary>
    /// Creates a hub instance backed by the shared aircraft snapshot and capture status services.
    /// </summary>
    public AdsbHub(AircraftSnapshotStore aircraft, CaptureStatusService status)
    {
        this.aircraft = aircraft;
        this.status = status;
    }

    /// <summary>
    /// Returns the current aircraft snapshot so new clients can populate their UI before live updates arrive.
    /// </summary>
    public IReadOnlyList<AircraftTelemetryEvent> GetAircraftSnapshot() => aircraft.GetAll();

    /// <summary>
    /// Returns capture status and counters for dashboard health displays.
    /// </summary>
    public ServerStatusDto GetStatus() => status.GetStatus();
}

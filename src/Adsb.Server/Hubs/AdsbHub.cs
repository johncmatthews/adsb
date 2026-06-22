using Adsb.Server.Contracts;
using Adsb.Server.Services;
using Microsoft.AspNetCore.SignalR;

namespace Adsb.Server.Hubs;

public sealed class AdsbHub : Hub
{
    private readonly AircraftSnapshotStore aircraft;
    private readonly CaptureStatusService status;

    public AdsbHub(AircraftSnapshotStore aircraft, CaptureStatusService status)
    {
        this.aircraft = aircraft;
        this.status = status;
    }

    public IReadOnlyList<AircraftTelemetryEvent> GetAircraftSnapshot() => aircraft.GetAll();

    public ServerStatusDto GetStatus() => status.GetStatus();
}

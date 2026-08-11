# ADS-B RTL-SDR Decoder

Cross-platform .NET console application for decoding 1090 MHz ADS-B / Mode S extended squitter frames from an RTL-SDR dongle.

## Requirements

- .NET 10 SDK
- RTL-SDR compatible USB dongle
- Native `librtlsdr` driver

Install the native driver:

```sh
# macOS
brew install librtlsdr

# Debian / Ubuntu
sudo apt install librtlsdr0 rtl-sdr
```

On Windows, place `rtlsdr.dll` somewhere the app can load it, or pass `--driver C:\path\to\rtlsdr.dll`.

## Run

```sh
dotnet run --project src/Adsb.Console -- --gain max
```

Useful options:

```sh
dotnet run --project src/Adsb.Console -- --list-devices
dotnet run --project src/Adsb.Console -- --device 0 --gain auto --ppm 1
dotnet run --project src/Adsb.Console -- --receiver-lat 40.7128 --receiver-lon -74.0060 --raw
dotnet run --project src/Adsb.Console -- --registry aircraft.csv
dotnet run --project src/Adsb.Console -- --watchlist watchlist.json --telemetry-db watchlist.sqlite
```

The MVP demodulator expects `--sample-rate 2000000`, tunes to `1090MHz` by default, validates ADS-B CRC, decodes common aircraft identification, airborne position, and airborne velocity messages, then prints one readable line per decoded frame.

ADS-B aircraft identification frames carry an 8-character flight ID / callsign. Airline traffic usually sends an operational flight ID such as `KLM1023`; many general aviation aircraft send a tail number instead. Tail/registration is not generally broadcast directly, so the app derives U.S. N-numbers from the ICAO address and can optionally load an ICAO-to-registration CSV for other aircraft.

Registry CSV files may use a dump1090-style header:

```csv
icao24,r
4840D6,PH-BQP
400AA2,G-EZEJ
```

## Watchlist Recording

Pass `--watchlist <file>` to record matched aircraft telemetry to SQLite. The default database path is `adsb-watchlist.sqlite`; use `--telemetry-db <path>` to choose another file.

Example watchlist:

```json
{
  "aircraft": [
    {
      "label": "club aircraft",
      "icao": "A58A20",
      "tail": "N456TS"
    },
    {
      "label": "KLM sample",
      "flight": "KLM1023"
    }
  ]
}
```

Each matching decoded message is written to the `watchlist_telemetry` table with UTC timestamp, matched watchlist identifier, ICAO, tail, flight number, callsign, position, altitude, speed, heading/track, vertical rate, raw frame, signal strength, and CRC status.

## Server

The ASP.NET Core server exposes the decode pipeline to future clients over SignalR and REST.

```sh
dotnet run --project src/Adsb.Server --urls http://127.0.0.1:5087
```

Capture is disabled by default so the server can start without an RTL-SDR attached. Enable live capture with configuration:

```sh
dotnet run --project src/Adsb.Server -- \
  --urls http://127.0.0.1:5087 \
  --Adsb:Capture:Enabled true \
  --Adsb:Watchlist:Path watchlist.json \
  --Adsb:Replay:DatabasePath watchlist.sqlite
```

The registry file is optional. U.S. N-numbers are derived automatically from the ICAO address. Set `--Adsb:Identity:RegistryPath aircraft.csv` only when you want non-U.S. or custom ICAO-to-registration lookups.

SignalR:

- Hub: `/hubs/adsb`
- Server-to-client events: `aircraftUpdated`, `watchlistTelemetry`
- Client-callable methods: `GetAircraftSnapshot`, `GetStatus`

REST endpoints:

- `GET /healthz`
- `GET /api/status`
- `GET /api/stats`
- `GET /api/aircraft`
- `GET /api/aircraft/{icao-or-tail-or-flight-or-callsign}`
- `GET /api/watchlist`
- `POST /api/watchlist`
- `PUT /api/watchlist/{id}`
- `DELETE /api/watchlist/{id}`
- `GET /api/replay/sessions`
- `GET /api/replay/events?icao=4840D6&limit=1000`

Compatibility streams:

- `GET /compat/jsonl`
- `GET /compat/sbs`
- `GET /compat/beast`

Optional TCP compatibility outputs can be enabled with config values:

```json
{
  "Adsb": {
    "Compatibility": {
      "SbsTcpPort": 30003,
      "JsonLinesTcpPort": 31000,
      "BeastTcpPort": 30005
    }
  }
}
```

## Web Client

The real-time dashboard lives in `src/Adsb.Client` and connects to the SignalR server.

Run the ADS-B server first:

```sh
dotnet run --project src/Adsb.Server -- \
  --urls http://127.0.0.1:5087 \
  --Adsb:Capture:Enabled true \
  --Adsb:Watchlist:Path watchlist.json \
  --Adsb:Replay:DatabasePath watchlist.sqlite
```

Then run the client:

```sh
dotnet run --project src/Adsb.Client -- \
  --urls http://127.0.0.1:5091 \
  --AdsbClient:Receiver:Label "Home" \
  --AdsbClient:Receiver:Latitude 40.7128 \
  --AdsbClient:Receiver:Longitude -74.0060 \
  --AdsbClient:Receiver:RangeNauticalMiles 150
```

Open `http://127.0.0.1:5091`. The dashboard defaults to `http://127.0.0.1:5087` as the SignalR server URL; you can change it on screen or pass `?server=http://host:port` in the browser URL.

The radar display is centered on the configured receiver location and plots aircraft by range and bearing relative to you. The same values can be stored in `src/Adsb.Client/appsettings.json`:

```json
{
  "AdsbClient": {
    "DefaultServerUrl": "http://127.0.0.1:5087",
    "Receiver": {
      "Label": "Home",
      "Latitude": 40.7128,
      "Longitude": -74.0060,
      "RangeNauticalMiles": 150
    }
  }
}
```

## Publish For Raspberry Pi

Use `dotnet publish` with the runtime identifier that matches the Raspberry Pi operating system architecture.

For a Raspberry Pi 3B+ running 64-bit Raspberry Pi OS:

```sh
dotnet publish src/Adsb.Server/Adsb.Server.csproj -c Release -r linux-arm64 --self-contained true -o publish/pi/server
dotnet publish src/Adsb.Client/Adsb.Client.csproj -c Release -r linux-arm64 --self-contained true -o publish/pi/client
```

For a Raspberry Pi 3B+ running 32-bit Raspberry Pi OS:

```sh
dotnet publish src/Adsb.Server/Adsb.Server.csproj -c Release -r linux-arm --self-contained true -o publish/pi/server
dotnet publish src/Adsb.Client/Adsb.Client.csproj -c Release -r linux-arm --self-contained true -o publish/pi/client
```

On the Pi, `uname -m` usually reports `aarch64` for a 64-bit OS and `armv7l` for a 32-bit OS. The publish output includes the .NET runtime when `--self-contained true` is used, but it does not include the native RTL-SDR driver. Install it on the Pi before running the server:

```sh
sudo apt install rtl-sdr librtlsdr0
```

Example output:

```text
14:22:01.125Z DF=17 ICAO=4840D6 TC=4 Aircraft identification tail=PH-BQP flight=KLM1023 category=0 signal=12.8dB
14:22:03.418Z DF=17 ICAO=40621D TC=11 Airborne position (barometric altitude) alt=38000ft lat=52.25720 lon=3.91937 signal=14.1dB
14:22:03.602Z DF=17 ICAO=485020 TC=19 Airborne velocity gs=159kt track=183deg vr=-832fpm signal=13.7dB
```

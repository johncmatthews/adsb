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
```

The MVP demodulator expects `--sample-rate 2000000`, tunes to `1090MHz` by default, validates ADS-B CRC, decodes common aircraft identification, airborne position, and airborne velocity messages, then prints one readable line per decoded frame.

Example output:

```text
14:22:01.125Z DF=17 ICAO=4840D6 TC=4 Aircraft identification callsign=KLM1023 category=0 signal=12.8dB
14:22:03.418Z DF=17 ICAO=40621D TC=11 Airborne position (barometric altitude) alt=38000ft lat=52.25720 lon=3.91937 signal=14.1dB
14:22:03.602Z DF=17 ICAO=485020 TC=19 Airborne velocity gs=159kt track=183deg vr=-832fpm signal=13.7dB
```

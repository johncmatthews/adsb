namespace Adsb.Radio;

/// <summary>
/// USB identity fields reported by librtlsdr for one discoverable RTL-SDR device.
/// </summary>
/// <param name="Index">Zero-based device index used when opening the dongle.</param>
/// <param name="Manufacturer">USB manufacturer string.</param>
/// <param name="Product">USB product string.</param>
/// <param name="Serial">USB serial string.</param>
public sealed record RtlSdrDeviceInfo(uint Index, string Manufacturer, string Product, string Serial);

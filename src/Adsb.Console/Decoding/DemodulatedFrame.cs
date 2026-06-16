namespace Adsb.Decoding;

public sealed record DemodulatedFrame(byte[] Data, int BitLength, double SignalDb, DateTimeOffset ReceivedAt)
{
    public string RawHex => Convert.ToHexString(Data);
}

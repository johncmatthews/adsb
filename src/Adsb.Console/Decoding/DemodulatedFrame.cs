namespace Adsb.Decoding;

/// <summary>
/// Binary Mode S frame recovered from the sample stream before protocol-level decoding.
/// </summary>
/// <param name="Data">Frame bytes packed most-significant bit first.</param>
/// <param name="BitLength">Number of valid frame bits in <paramref name="Data"/>.</param>
/// <param name="SignalDb">Estimated signal strength relative to the local preamble floor.</param>
/// <param name="ReceivedAt">UTC timestamp assigned to the sample buffer that contained the frame.</param>
public sealed record DemodulatedFrame(byte[] Data, int BitLength, double SignalDb, DateTimeOffset ReceivedAt)
{
    /// <summary>
    /// Uppercase hexadecimal representation used for logs, replay storage, and compatibility output.
    /// </summary>
    public string RawHex => Convert.ToHexString(Data);
}

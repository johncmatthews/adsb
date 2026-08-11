namespace Adsb.Decoding;

/// <summary>
/// Reads individual bits and bit fields from Mode S payloads packed most-significant bit first.
/// </summary>
public static class BitReader
{
    /// <summary>
    /// Reads a single bit using ADS-B bit numbering, where bit zero is the high bit of the first byte.
    /// </summary>
    public static int GetBit(ReadOnlySpan<byte> data, int bitOffset)
    {
        var value = data[bitOffset / 8];
        var mask = 1 << (7 - (bitOffset % 8));
        return (value & mask) == 0 ? 0 : 1;
    }

    /// <summary>
    /// Reads up to 32 consecutive bits and returns them as an unsigned integer.
    /// </summary>
    /// <param name="data">Packed byte buffer to read from.</param>
    /// <param name="bitOffset">Zero-based bit offset from the beginning of the buffer.</param>
    /// <param name="bitCount">Number of bits to read, from 0 through 32.</param>
    public static uint GetBits(ReadOnlySpan<byte> data, int bitOffset, int bitCount)
    {
        if (bitCount is < 0 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(bitCount), "bitCount must be between 0 and 32.");
        }

        uint value = 0;
        for (var i = 0; i < bitCount; i++)
        {
            value = (value << 1) | (uint)GetBit(data, bitOffset + i);
        }

        return value;
    }
}

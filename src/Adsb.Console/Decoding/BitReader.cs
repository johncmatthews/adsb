namespace Adsb.Decoding;

public static class BitReader
{
    public static int GetBit(ReadOnlySpan<byte> data, int bitOffset)
    {
        var value = data[bitOffset / 8];
        var mask = 1 << (7 - (bitOffset % 8));
        return (value & mask) == 0 ? 0 : 1;
    }

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

namespace Adsb.Decoding;

public static class ModeSCrc
{
    private const uint Generator = 0x1FFF409;

    public static uint ComputeRemainder(ReadOnlySpan<byte> message, int bitLength)
    {
        if (bitLength <= 24)
        {
            throw new ArgumentOutOfRangeException(nameof(bitLength), "Mode S messages must include a 24-bit parity field.");
        }

        var byteLength = (bitLength + 7) / 8;
        Span<byte> work = byteLength <= 32 ? stackalloc byte[byteLength] : new byte[byteLength];
        message[..byteLength].CopyTo(work);

        for (var bit = 0; bit < bitLength - 24; bit++)
        {
            if (BitReader.GetBit(work, bit) == 0)
            {
                continue;
            }

            XorGenerator(work, bit);
        }

        return BitReader.GetBits(work, bitLength - 24, 24);
    }

    private static void XorGenerator(Span<byte> data, int bitOffset)
    {
        for (var i = 0; i <= 24; i++)
        {
            if ((Generator & (1u << (24 - i))) == 0)
            {
                continue;
            }

            var target = bitOffset + i;
            data[target / 8] ^= (byte)(1 << (7 - (target % 8)));
        }
    }
}

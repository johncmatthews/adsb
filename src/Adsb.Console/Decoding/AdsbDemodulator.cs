namespace Adsb.Decoding;

public sealed class AdsbDemodulator
{
    private const int SamplesPerSecond = 2_000_000;
    private const int PreambleSamples = 16;
    private const int MaxFrameBits = 112;
    private const int ShortFrameBits = 56;
    private const int TailSamples = PreambleSamples + (MaxFrameBits * 2) + 32;

    private int[] previousMagnitudes = Array.Empty<int>();

    public AdsbDemodulator(uint sampleRate)
    {
        if (sampleRate != SamplesPerSecond)
        {
            throw new ArgumentException("The MVP demodulator expects 2,000,000 samples per second.", nameof(sampleRate));
        }
    }

    public IReadOnlyList<DemodulatedFrame> Process(ReadOnlySpan<byte> iqBytes, DateTimeOffset receivedAt)
    {
        var sampleCount = iqBytes.Length / 2;
        if (sampleCount == 0)
        {
            return Array.Empty<DemodulatedFrame>();
        }

        var frames = new List<DemodulatedFrame>();
        var magnitudes = new int[previousMagnitudes.Length + sampleCount];
        previousMagnitudes.CopyTo(magnitudes.AsSpan());
        ConvertToMagnitudes(iqBytes, magnitudes.AsSpan(previousMagnitudes.Length));

        var previousSampleCount = previousMagnitudes.Length;
        var searchLimit = magnitudes.Length - PreambleSamples - (ShortFrameBits * 2);
        for (var start = 0; start <= searchLimit; start++)
        {
            if (!LooksLikePreamble(magnitudes, start, out var signalDb, out var bitThreshold))
            {
                continue;
            }

            var dataStart = start + PreambleSamples;
            if (dataStart + 16 > magnitudes.Length)
            {
                continue;
            }

            var firstByte = ExtractFirstByte(magnitudes, dataStart);
            var downlinkFormat = firstByte >> 3;
            var bitLength = IsLongFrame(downlinkFormat) ? MaxFrameBits : ShortFrameBits;
            var frameEnd = dataStart + (bitLength * 2);
            if (frameEnd > magnitudes.Length || frameEnd <= previousSampleCount)
            {
                continue;
            }

            var frame = ExtractFrame(magnitudes, dataStart, bitLength, bitThreshold, signalDb, receivedAt);
            if (frame is not null)
            {
                frames.Add(frame);
                start = frameEnd - 1;
            }
        }

        var nextTailLength = Math.Min(TailSamples, magnitudes.Length);
        previousMagnitudes = new int[nextTailLength];
        magnitudes.AsSpan(magnitudes.Length - nextTailLength, nextTailLength).CopyTo(previousMagnitudes);

        return frames;
    }

    private static void ConvertToMagnitudes(ReadOnlySpan<byte> iqBytes, Span<int> magnitudes)
    {
        for (var i = 0; i < magnitudes.Length; i++)
        {
            var sample = i * 2;
            var inPhase = iqBytes[sample] - 127;
            var quadrature = iqBytes[sample + 1] - 127;
            magnitudes[i] = (inPhase * inPhase) + (quadrature * quadrature);
        }
    }

    private static bool LooksLikePreamble(
        ReadOnlySpan<int> magnitudes,
        int start,
        out double signalDb,
        out int bitThreshold)
    {
        var p0 = magnitudes[start];
        var p1 = magnitudes[start + 2];
        var p2 = magnitudes[start + 7];
        var p3 = magnitudes[start + 9];

        var floor =
            magnitudes[start + 1] +
            magnitudes[start + 3] +
            magnitudes[start + 4] +
            magnitudes[start + 5] +
            magnitudes[start + 6] +
            magnitudes[start + 8] +
            magnitudes[start + 10] +
            magnitudes[start + 11] +
            magnitudes[start + 12] +
            magnitudes[start + 13] +
            magnitudes[start + 14] +
            magnitudes[start + 15];

        var floorAverage = floor / 12.0;
        var pulseAverage = (p0 + p1 + p2 + p3) / 4.0;
        var minimumPulse = Math.Min(Math.Min(p0, p1), Math.Min(p2, p3));

        signalDb = 10.0 * Math.Log10((pulseAverage + 1.0) / (floorAverage + 1.0));
        bitThreshold = Math.Max(32, (int)((pulseAverage - floorAverage) * 0.12));

        if (pulseAverage < 800 || pulseAverage < floorAverage * 2.5 || minimumPulse < floorAverage * 1.7)
        {
            return false;
        }

        return p0 > magnitudes[start + 1] &&
               p1 > magnitudes[start + 1] &&
               p1 > magnitudes[start + 3] &&
               p2 > magnitudes[start + 6] &&
               p2 > magnitudes[start + 8] &&
               p3 > magnitudes[start + 8] &&
               p3 > magnitudes[start + 10];
    }

    private static byte ExtractFirstByte(ReadOnlySpan<int> magnitudes, int dataStart)
    {
        byte value = 0;
        for (var bit = 0; bit < 8; bit++)
        {
            if (ExtractBit(magnitudes, dataStart, bit, out _) != 0)
            {
                value |= (byte)(1 << (7 - bit));
            }
        }

        return value;
    }

    private static DemodulatedFrame? ExtractFrame(
        ReadOnlySpan<int> magnitudes,
        int dataStart,
        int bitLength,
        int bitThreshold,
        double signalDb,
        DateTimeOffset receivedAt)
    {
        var bytes = new byte[(bitLength + 7) / 8];
        var weakBits = 0;

        for (var bit = 0; bit < bitLength; bit++)
        {
            var decoded = ExtractBit(magnitudes, dataStart, bit, out var confidence);
            if (confidence < bitThreshold)
            {
                weakBits++;
            }

            if (decoded != 0)
            {
                bytes[bit / 8] |= (byte)(1 << (7 - (bit % 8)));
            }
        }

        if (weakBits > bitLength / 3)
        {
            return null;
        }

        return new DemodulatedFrame(bytes, bitLength, signalDb, receivedAt);
    }

    private static int ExtractBit(ReadOnlySpan<int> magnitudes, int dataStart, int bit, out int confidence)
    {
        var firstHalf = magnitudes[dataStart + (bit * 2)];
        var secondHalf = magnitudes[dataStart + (bit * 2) + 1];
        confidence = Math.Abs(firstHalf - secondHalf);
        return firstHalf > secondHalf ? 1 : 0;
    }

    private static bool IsLongFrame(int downlinkFormat) =>
        downlinkFormat is 16 or 17 or 18 or 19 or 20 or 21 or 24;
}

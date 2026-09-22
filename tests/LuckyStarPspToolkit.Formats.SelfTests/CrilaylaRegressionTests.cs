using System.Buffers.Binary;
using LuckyStarPspToolkit.Formats.Common;
using LuckyStarPspToolkit.Formats.Cri;

/// <summary>Independent reverse-bitstream examples covering CRILAYLA copy tokens and bounded rejection.</summary>
internal static partial class SelfTestRunner
{
    /// <summary>Decodes manually specified length tiers and rejects malformed frames before following impossible runs.</summary>
    private static void TestCrilaylaBackReferences()
    {
        // The three literal tokens seed the reverse output with A, B, C. Offset zero
        // in the wire token means distance three, so each copy repeats ABC with overlap.
        const string seed = "001000001001000010001000011";
        const string reference = "10000000000000";
        (int Length, string Bits)[] lengths =
        [
            (3, "00"), (5, "10"), (6, "11000"), (12, "11110"),
            (13, "1111100000"), (43, "1111111110"),
            (44, "111111111100000000"), (298, "111111111111111110"),
            (299, "11111111111111111100000000"),
            (554, "1111111111111111111111111100000000"),
            (700, "1111111111111111111111111110010010")
        ];
        foreach ((int length, string lengthBits) in lengths)
        {
            byte[] frame = MakeCrilaylaBitFixture(3 + length, seed + reference + lengthBits);
            byte[] original = frame.ToArray();
            byte[] output = CrilaylaCodec.Decompress(frame);
            SequenceEqual(frame.AsSpan(frame.Length - 256).ToArray(), output.AsSpan(0, 256).ToArray());
            byte[] expected = Enumerable.Range(0, 3 + length)
                .Select(index => (byte)('A' + (2 + length - index) % 3)).ToArray();
            SequenceEqual(expected, output.AsSpan(256).ToArray());
            SequenceEqual(original, frame);
        }

        // The largest 13-bit distance is 8191 + 3. Seed three distinct bytes,
        // fill the intervening bytes with X, then copy those earliest seed bytes.
        byte[] farOutput = CrilaylaCodec.Decompress(MakeCrilaylaBitFixture(8197,
            seed + string.Concat(Enumerable.Repeat("001011000", 8191)) + "11111111111111" + "00"));
        byte[] farExpected = "CBA"u8.ToArray().Concat(Enumerable.Repeat((byte)'X', 8191)).Concat("CBA"u8.ToArray()).ToArray();
        SequenceEqual(farExpected, farOutput.AsSpan(256).ToArray());

        byte[] literal = MakeCrilaylaBitFixture(3, seed);
        Throws("CRILAYLA_LIMIT", () => CrilaylaCodec.Inspect(literal,
            new FileLimits(MaximumInputBytes: literal.Length - 1)));
        Throws("CRILAYLA_LIMIT", () => CrilaylaCodec.Decompress(literal,
            new FileLimits(MaximumCrilaylaOutputBytes: int.MinValue)));
        Throws("CRILAYLA_REFERENCE", () => CrilaylaCodec.Decompress(
            MakeCrilaylaBitFixture(3, reference + "00")));
        Throws("CRILAYLA_EOF", () => CrilaylaCodec.Decompress(MakeCrilaylaBitFixture(3, "")));
        // This run exceeds the remaining three output bytes at its first length tier.
        // The missing final extension must not be scanned before rejecting that overrun.
        Throws("CRILAYLA_OUTPUT", () => CrilaylaCodec.Decompress(
            MakeCrilaylaBitFixture(6, seed + reference + "111111111111111111")));
        Equal(256, CrilaylaCodec.Decompress(MakeCrilaylaBitFixture(0, "")).Length);
    }

    /// <summary>Explicit allocation probe for the 256-MiB compressed-length overflow; excluded from routine fast self-tests.</summary>
    private static void TestCrilaylaLargeBitRange()
    {
        const int compressedSize = 256 * 1024 * 1024;
        byte[] frame = new byte[16 + compressedSize + 256];
        "CRILAYLA"u8.CopyTo(frame);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(12), compressedSize);
        // A single 0 + 01000001 literal starts at the tail of the compressed span.
        frame[16 + compressedSize - 1] = 0x20;
        frame[16 + compressedSize - 2] = 0x80;
        byte[] output = CrilaylaCodec.Decompress(frame);
        Equal(257, output.Length);
        Equal((byte)'A', output[256]);
    }

    /// <summary>Packs a literal wire-bit description in reverse byte order without using the production codec.</summary>
    /// <param name="bodySize">The declared output length excluding the 256-byte raw prefix.</param>
    /// <param name="bits">Manually specified read-order bits; final byte padding is zero.</param>
    /// <returns>A synthetic CRILAYLA frame with a deterministic, nonzero raw prefix.</returns>
    private static byte[] MakeCrilaylaBitFixture(int bodySize, string bits)
    {
        int compressedSize = (bits.Length + 7) / 8;
        byte[] frame = new byte[16 + compressedSize + 256];
        "CRILAYLA"u8.CopyTo(frame);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(8), checked((uint)bodySize));
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(12), checked((uint)compressedSize));
        for (int bit = 0; bit < bits.Length; bit++)
        {
            if (bits[bit] == '1') frame[16 + compressedSize - 1 - bit / 8] |= (byte)(1 << (7 - bit % 8));
        }
        for (int index = 0; index < 256; index++) frame[16 + compressedSize + index] = (byte)index;
        return frame;
    }
}

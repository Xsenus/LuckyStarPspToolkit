using System.Buffers.Binary;
using LuckyStarPspToolkit.Formats.Common;

namespace LuckyStarPspToolkit.Formats.Cri;

/// <summary>
/// Represents immutable crilayla info data exchanged by the toolkit.
/// </summary>
/// <param name="UncompressedBodySize">The uncompressed body size value used by this model or operation.</param>
/// <param name="CompressedDataSize">The compressed data size value used by this model or operation.</param>
/// <param name="ExtractedSize">The extracted size value used by this model or operation.</param>
public sealed record CrilaylaInfo(int UncompressedBodySize, int CompressedDataSize, int ExtractedSize);

/// <summary>
/// Provides crilayla codec operations with strict bounds and format validation.
/// </summary>
public static class CrilaylaCodec
{
    /// <summary>The fixed frame header size value used by this format or revision.</summary>
    private const int FrameHeaderSize = 0x10;
    /// <summary>The fixed raw header size value used by this format or revision.</summary>
    private const int RawHeaderSize = 0x100;
    /// <summary>The magic value used by this model or operation.</summary>
    private static ReadOnlySpan<byte> Magic => "CRILAYLA"u8;

    /// <summary>
    /// Determines whether frame.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <returns><see langword="true"/> when the condition is satisfied; otherwise <see langword="false"/>.</returns>
    public static bool IsFrame(ReadOnlySpan<byte> data) => data.Length >= FrameHeaderSize && data.StartsWith(Magic);

    /// <summary>
    /// Inspects the supplied input and returns a structured diagnostic result.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The validated operation result.</returns>
    public static CrilaylaInfo Inspect(ReadOnlySpan<byte> data, FileLimits? limits = null)
    {
        limits ??= FileLimits.Default;
        if (data.Length > limits.MaximumInputBytes)
        {
            throw new ToolkitException("CRILAYLA_LIMIT", $"CRILAYLA input {data.Length} exceeds configured limit.");
        }
        if (!IsFrame(data))
        {
            throw new ToolkitException("CRILAYLA_MAGIC", "Input is not a CRILAYLA frame.");
        }
        int uncompressedBody = CheckedSize(BinaryPrimitives.ReadUInt32LittleEndian(data[8..12]), "uncompressed body");
        int compressedSize = CheckedSize(BinaryPrimitives.ReadUInt32LittleEndian(data[12..16]), "compressed payload");
        long extractedSize = (long)uncompressedBody + RawHeaderSize;
        if (extractedSize > limits.MaximumCrilaylaOutputBytes)
        {
            throw new ToolkitException("CRILAYLA_LIMIT", $"CRILAYLA output {extractedSize} exceeds configured limit.");
        }
        long expectedFrame = (long)FrameHeaderSize + compressedSize + RawHeaderSize;
        if (expectedFrame != data.Length)
        {
            throw new ToolkitException("CRILAYLA_SIZE", $"CRILAYLA frame length is {data.Length}; header requires {expectedFrame}.");
        }
        return new CrilaylaInfo(uncompressedBody, compressedSize, checked(uncompressedBody + RawHeaderSize));
    }

    /// <summary>
    /// Decodes a bounded reverse-bitstream CRILAYLA frame, validating every output and back-reference range.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    public static byte[] Decompress(ReadOnlySpan<byte> data, FileLimits? limits = null)
    {
        CrilaylaInfo info = Inspect(data, limits);
        ReadOnlySpan<byte> compressed = data.Slice(FrameHeaderSize, info.CompressedDataSize);
        ReadOnlySpan<byte> rawHeader = data.Slice(FrameHeaderSize + info.CompressedDataSize, RawHeaderSize);
        ReverseBitReader bits = new(compressed);
        byte[] reverseOutput = new byte[info.UncompressedBodySize];
        int written = 0;
        while (written < reverseOutput.Length)
        {
            uint control = bits.ReadBits(1);
            if (control == 0)
            {
                reverseOutput[written++] = checked((byte)bits.ReadBits(8));
                continue;
            }

            int offset = checked((int)bits.ReadBits(13) + 3);
            if (offset > written)
            {
                throw new ToolkitException("CRILAYLA_REFERENCE", $"Invalid CRILAYLA back-reference offset {offset} at output {written}.");
            }
            int remaining = reverseOutput.Length - written;
            int length = 3;
            RequireRunFits(length, remaining);
            ReadOnlySpan<int> levels = [2, 3, 5, 8];
            foreach (int level in levels)
            {
                int value = checked((int)bits.ReadBits(level));
                RequireRunFits(value, remaining - length);
                length = checked(length + value);
                if (value != (1 << level) - 1)
                {
                    break;
                }
            }
            if (length >= 3 + 3 + 7 + 31 + 255)
            {
                while (true)
                {
                    int value = checked((int)bits.ReadBits(8));
                    RequireRunFits(value, remaining - length);
                    length = checked(length + value);
                    if (value != 0xFF)
                    {
                        break;
                    }
                }
            }

            for (int i = 0; i < length; i++)
            {
                reverseOutput[written] = reverseOutput[written - offset];
                written++;
            }
        }

        byte[] output = new byte[info.ExtractedSize];
        rawHeader.CopyTo(output);
        for (int i = 0; i < reverseOutput.Length; i++)
        {
            output[RawHeaderSize + i] = reverseOutput[reverseOutput.Length - 1 - i];
        }
        return output;
    }

    /// <summary>Rejects an impossible run while reading its length, before scanning further extension bytes or overflowing.</summary>
    /// <param name="increment">The minimum run length or next wire-encoded length increment.</param>
    /// <param name="remaining">Output capacity left after all previously validated increments.</param>
    private static void RequireRunFits(int increment, int remaining)
    {
        if (increment > remaining)
        {
            throw new ToolkitException("CRILAYLA_OUTPUT", "CRILAYLA back-reference exceeds declared output size.");
        }
    }

    /// <summary>
    /// Converts a CRILAYLA size field to an allocation-safe integer under the configured limit.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <param name="name">The logical name used for lookup or diagnostics.</param>
    /// <returns>The validated operation result.</returns>
    private static int CheckedSize(uint value, string name)
    {
        if (value > int.MaxValue)
        {
            throw new ToolkitException("CRILAYLA_SIZE", $"CRILAYLA {name} size {value} exceeds Int32.");
        }
        return (int)value;
    }

    /// <summary>
    /// Provides reverse bit reader operations with strict bounds and format validation.
    /// </summary>
    private ref struct ReverseBitReader
    {
        /// <summary>Stores the data state owned by this instance or type.</summary>
        private readonly ReadOnlySpan<byte> _data;
        /// <summary>Stores the bit position state owned by this instance or type.</summary>
        private long _bitPosition;

        /// <summary>
        /// Initializes a new instance with validated constructor state.
        /// </summary>
        /// <param name="data">The binary data to process.</param>
        public ReverseBitReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _bitPosition = 0;
        }

        /// <summary>
        /// Reads bits while enforcing the relevant format and safety invariants.
        /// </summary>
        /// <param name="count">The number of items to process.</param>
        /// <returns>The validated operation result.</returns>
        public uint ReadBits(int count)
        {
            if (count is < 1 or > 32)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            if (_bitPosition > (long)_data.Length * 8 - count)
            {
                throw new ToolkitException("CRILAYLA_EOF", "CRILAYLA compressed bitstream ended early.");
            }
            uint value = 0;
            for (int i = 0; i < count; i++)
            {
                long sourceBit = _bitPosition++;
                int byteIndex = _data.Length - 1 - checked((int)(sourceBit / 8));
                int bitIndex = 7 - (int)(sourceBit % 8);
                value = (value << 1) | (uint)((_data[byteIndex] >> bitIndex) & 1);
            }
            return value;
        }
    }
}

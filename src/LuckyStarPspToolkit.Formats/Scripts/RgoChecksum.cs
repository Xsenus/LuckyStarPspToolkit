using System.Buffers.Binary;
using LuckyStarPspToolkit.Formats.Common;

namespace LuckyStarPspToolkit.Formats.Scripts;

/// <summary>
/// Represents the toolkit's RGO checksum model or service.
/// </summary>
public static class RgoChecksum
{
    /// <summary>The fixed initial value used by this format or revision.</summary>
    private const ulong Initial = 0x1111111111111111UL;

    /// <summary>
    /// Computes the two wrapping 64-bit sums over all 16-byte blocks before the stored RGO checksum.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <returns>The validated operation result.</returns>
    public static (ulong Low, ulong High) Calculate(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16 || (data.Length & 0x0F) != 0)
        {
            throw new ToolkitException("SCRIPT_CHECKSUM_SIZE", $"Script length {data.Length} must be a positive multiple of 16.");
        }
        ulong low = Initial;
        ulong high = Initial;
        int contentLength = data.Length - 16;
        for (int offset = 0; offset < contentLength; offset += 16)
        {
            low = unchecked(low + BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)));
            high = unchecked(high + BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset + 8, 8)));
        }
        return (low, high);
    }

    /// <summary>
    /// Compares the stored 16-byte RGO checksum with a fresh calculation over the preceding file bytes.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <returns><see langword="true"/> when the condition is satisfied; otherwise <see langword="false"/>.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public static bool Verify(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16 || (data.Length & 0x0F) != 0)
        {
            return false;
        }
        (ulong low, ulong high) = Calculate(data);
        return BinaryPrimitives.ReadUInt64LittleEndian(data[^16..^8]) == low
            && BinaryPrimitives.ReadUInt64LittleEndian(data[^8..]) == high;
    }

    /// <summary>
    /// Applies the requested transformation after validating all preconditions.
    /// </summary>
    /// <param name="data">The binary data to process.</param>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    public static void Apply(Span<byte> data)
    {
        (ulong low, ulong high) = Calculate(data);
        BinaryPrimitives.WriteUInt64LittleEndian(data[^16..^8], low);
        BinaryPrimitives.WriteUInt64LittleEndian(data[^8..], high);
    }
}

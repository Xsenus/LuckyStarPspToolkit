using System.Buffers.Binary;
using System.IO.Compression;
using LuckyStarPspToolkit.Formats.Common;

namespace LuckyStarPspToolkit.Formats.Fonts;

/// <summary>
/// Represents the toolkit's rgba image model or service.
/// </summary>
public sealed class RgbaImage
{
    /// <summary>
    /// Initializes a new instance with validated constructor state.
    /// </summary>
    /// <param name="width">The width value.</param>
    /// <param name="height">The height value.</param>
    /// <param name="pixels">The pixels value.</param>
    public RgbaImage(int width, int height, byte[] pixels)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ToolkitException("PNG_DIMENSIONS", $"Image dimensions {width}x{height} must be positive.");
        }
        ArgumentNullException.ThrowIfNull(pixels);
        int expected;
        try
        {
            expected = checked(width * height * 4);
        }
        catch (OverflowException ex)
        {
            throw new ToolkitException("PNG_DIMENSIONS", "Image dimensions overflow the supported buffer size.", ex);
        }
        if (pixels.Length != expected)
        {
            throw new ToolkitException("PNG_PIXELS", $"RGBA buffer has {pixels.Length} bytes; expected {expected}.");
        }
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    /// <summary>The width value used by this model or operation.</summary>
    public int Width { get; }
    /// <summary>The height value used by this model or operation.</summary>
    public int Height { get; }
    /// <summary>The pixels value used by this model or operation.</summary>
    public byte[] Pixels { get; }
}

/// <summary>
/// Provides PNG writer operations with strict bounds and format validation.
/// </summary>
public static class PngWriter
{
    /// <summary>The signature value used by this model or operation.</summary>
    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];
    /// <summary>The crc table value used by this model or operation.</summary>
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>
    /// Encodes validated text or binary state into the target representation.
    /// </summary>
    /// <param name="image">The image value.</param>
    /// <param name="limits">Optional conservative safety limits; defaults are used when omitted.</param>
    /// <returns>The resulting binary or typed sequence.</returns>
    public static byte[] Encode(RgbaImage image, FileLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        limits ??= FileLimits.Default;
        long pixelCount = checked((long)image.Width * image.Height);
        if (image.Width > limits.MaximumImageDimension || image.Height > limits.MaximumImageDimension || pixelCount > limits.MaximumImagePixels)
        {
            throw new ToolkitException("PNG_LIMIT", $"Image {image.Width}x{image.Height} exceeds configured limits.");
        }

        int stride = checked(image.Width * 4);
        byte[] filtered = new byte[checked((stride + 1) * image.Height)];
        for (int y = 0; y < image.Height; y++)
        {
            int target = y * (stride + 1);
            filtered[target] = 0;
            image.Pixels.AsSpan(y * stride, stride).CopyTo(filtered.AsSpan(target + 1, stride));
        }

        byte[] compressed;
        using (var compressedStream = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressedStream, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                zlib.Write(filtered);
            }
            compressed = compressedStream.ToArray();
        }

        using var output = new MemoryStream(checked(Signature.Length + compressed.Length + 128));
        output.Write(Signature);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)image.Width));
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], checked((uint)image.Height));
        header[8] = 8;
        header[9] = 6;
        header[10] = 0;
        header[11] = 0;
        header[12] = 0;
        WriteChunk(output, "IHDR"u8, header);
        WriteChunk(output, "IDAT"u8, compressed);
        WriteChunk(output, "IEND"u8, ReadOnlySpan<byte>.Empty);
        return output.ToArray();
    }

    /// <summary>
    /// Writes chunk while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="output">The destination stream, buffer, or model.</param>
    /// <param name="type">The type value.</param>
    /// <param name="data">The binary data to process.</param>
    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        if (type.Length != 4)
        {
            throw new ArgumentException("PNG chunk type must contain four bytes.", nameof(type));
        }
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)data.Length));
        output.Write(length);
        output.Write(type);
        output.Write(data);

        uint crc = 0xFFFFFFFF;
        crc = UpdateCrc(crc, type);
        crc = UpdateCrc(crc, data) ^ 0xFFFFFFFF;
        Span<byte> encodedCrc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(encodedCrc, crc);
        output.Write(encodedCrc);
    }

    /// <summary>
    /// Updates CRC while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="crc">The crc value.</param>
    /// <param name="data">The binary data to process.</param>
    /// <returns>The validated operation result.</returns>
    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte value in data)
        {
            crc = CrcTable[(int)((crc ^ value) & 0xFF)] ^ (crc >> 8);
        }
        return crc;
    }

    /// <summary>
    /// Builds CRC table while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <returns>The resulting binary or typed sequence.</returns>
    /// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>
    private static uint[] BuildCrcTable()
    {
        uint[] table = new uint[256];
        for (int index = 0; index < table.Length; index++)
        {
            uint value = checked((uint)index);
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? 0xEDB88320U ^ (value >> 1) : value >> 1;
            }
            table[index] = value;
        }
        return table;
    }
}

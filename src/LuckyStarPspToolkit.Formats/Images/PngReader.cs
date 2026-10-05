using System.Buffers.Binary;
using System.IO.Compression;
using LuckyStarPspToolkit.Formats.Common;
using LuckyStarPspToolkit.Formats.Fonts;

namespace LuckyStarPspToolkit.Formats.Images;

/// <summary>Decodes bounded, noninterlaced PNG images for palette-preserving game texture replacement.</summary>
public static class PngReader
{
    /// <summary>Validates CRCs, chunk order, pixel budgets and decompressed size before returning RGBA pixels.</summary>
    /// <param name="data">The entire PNG snapshot, limited to 64 MiB.</param>
    /// <param name="limits">Image dimension and pixel limits.</param>
    /// <returns>Eight-bit RGBA pixels; unsupported depths, interlacing and animated PNG are rejected.</returns>
    public static RgbaImage Decode(ReadOnlySpan<byte> data, FileLimits? limits = null)
    {
        limits ??= FileLimits.Default;
        if (data.Length > 64 * 1024 * 1024 || data.Length < 8 || !data[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new ToolkitException("PNG_SIGNATURE", "Invalid or oversized PNG input.");
        int width = 0, height = 0, depth = 0, color = -1, channels = 0;
        byte[]? palette = null, transparency = null;
        bool header = false, pixels = false, pixelsEnded = false, ended = false;
        using MemoryStream compressed = new();
        int position = 8;
        while (position < data.Length)
        {
            if (data.Length - position < 12) throw new ToolkitException("PNG_CHUNK", "Truncated PNG chunk.");
            uint count = BinaryPrimitives.ReadUInt32BigEndian(data[position..]);
            if (count > int.MaxValue || count > data.Length - position - 12) throw new ToolkitException("PNG_CHUNK", "PNG chunk exceeds input.");
            int size = (int)count;
            ReadOnlySpan<byte> type = data.Slice(position + 4, 4);
            for (int letter = 0; letter < 4; letter++)
                if (type[letter] is not (>= (byte)'A' and <= (byte)'Z') and not (>= (byte)'a' and <= (byte)'z'))
                    throw new ToolkitException("PNG_CHUNK_TYPE", "Invalid PNG chunk name.");
            if ((type[2] & 32) != 0) throw new ToolkitException("PNG_CHUNK_TYPE", "PNG reserved chunk-name bit must be zero.");
            ReadOnlySpan<byte> payload = data.Slice(position + 8, size);
            uint expected = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(position + 8 + size, 4));
            if (Crc(data.Slice(position + 4, size + 4)) != expected) throw new ToolkitException("PNG_CRC", "PNG chunk checksum mismatch.");
            position += size + 12;
            if (!header && !type.SequenceEqual("IHDR"u8)) throw new ToolkitException("PNG_ORDER", "IHDR must be first.");
            if (type.SequenceEqual("IHDR"u8))
            {
                if (header || size != 13) throw new ToolkitException("PNG_HEADER", "Invalid or duplicate PNG header.");
                uint w = BinaryPrimitives.ReadUInt32BigEndian(payload), h = BinaryPrimitives.ReadUInt32BigEndian(payload[4..]);
                if (w == 0 || h == 0 || w > int.MaxValue || h > int.MaxValue || w > limits.MaximumImageDimension || h > limits.MaximumImageDimension || (ulong)w * h > (ulong)Math.Max(0, limits.MaximumImagePixels) || (ulong)w * h > int.MaxValue / 4)
                    throw new ToolkitException("PNG_LIMIT", "PNG dimensions exceed configured limits.");
                width = (int)w; height = (int)h; depth = payload[8]; color = payload[9];
                channels = color switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
                if (channels == 0 || (color is 0 or 3 ? depth is not (1 or 2 or 4 or 8) : depth != 8) || payload[10] != 0 || payload[11] != 0 || payload[12] != 0)
                    throw new ToolkitException("PNG_UNSUPPORTED", "Use a noninterlaced 8-bit RGB/RGBA, grayscale or indexed PNG; 16-bit and animated images are unsupported.");
                header = true;
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                if (pixels || palette is not null || transparency is not null || size == 0 || size % 3 != 0 || size > 768 || color is 0 or 4 || (color == 3 && size / 3 > 1 << depth))
                    throw new ToolkitException("PNG_PALETTE", "Invalid PNG palette or palette order.");
                palette = payload.ToArray();
            }
            else if (type.SequenceEqual("tRNS"u8))
            {
                if (pixels || transparency is not null || (color == 3 ? palette is null || size == 0 || size > palette.Length / 3 : color == 0 ? size != 2 : color == 2 ? size != 6 : true))
                    throw new ToolkitException("PNG_TRANSPARENCY", "Invalid PNG transparency chunk.");
                transparency = payload.ToArray();
                if (color is 0 or 2)
                    for (int i = 0; i < size; i += 2)
                        if (BinaryPrimitives.ReadUInt16BigEndian(payload[i..]) >= 1 << depth)
                            throw new ToolkitException("PNG_TRANSPARENCY", "Transparency sample exceeds PNG depth.");
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (pixelsEnded || (color == 3 && palette is null)) throw new ToolkitException("PNG_ORDER", "Invalid PNG image-data order.");
                compressed.Write(payload);
                pixels = true;
            }
            else
            {
                if (pixels) pixelsEnded = true;
                if (type.SequenceEqual("IEND"u8))
                {
                    if (size != 0 || !pixels || position != data.Length) throw new ToolkitException("PNG_END", "Invalid PNG ending or trailing bytes.");
                    ended = true;
                    break;
                }
                if (type.SequenceEqual("acTL"u8) || type.SequenceEqual("fcTL"u8) || type.SequenceEqual("fdAT"u8) || (type[0] & 32) == 0)
                    throw new ToolkitException("PNG_UNSUPPORTED", "Unsupported critical or animation chunk.");
            }
        }
        if (!ended) throw new ToolkitException("PNG_END", "PNG is missing IEND.");
        long rowBytes = ((long)width * channels * depth + 7) / 8;
        if ((rowBytes + 1) * height > int.MaxValue) throw new ToolkitException("PNG_LIMIT", "PNG scanline buffer exceeds supported bounds.");
        int stride = (int)rowBytes;
        int bytesPerPixel = Math.Max(1, (channels * depth + 7) / 8);
        byte[] filtered = new byte[checked((stride + 1) * height)];
        compressed.Position = 0;
        try
        {
            using ZLibStream zlib = new(compressed, CompressionMode.Decompress, leaveOpen: true);
            zlib.ReadExactly(filtered);
            if (zlib.ReadByte() != -1) throw new ToolkitException("PNG_INFLATE_SIZE", "PNG expands beyond its declared dimensions.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            throw new ToolkitException("PNG_INFLATE", "PNG compressed image data is truncated or invalid.", ex);
        }
        byte[] rgba = new byte[checked(width * height * 4)];
        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * (stride + 1);
            int filter = filtered[rowOffset];
            if (filter > 4) throw new ToolkitException("PNG_FILTER", "Unknown PNG row filter.");
            Span<byte> row = filtered.AsSpan(rowOffset + 1, stride);
            ReadOnlySpan<byte> previous = y == 0 ? ReadOnlySpan<byte>.Empty : filtered.AsSpan(rowOffset - stride, stride);
            for (int x = 0; x < stride; x++)
            {
                int left = x < bytesPerPixel ? 0 : row[x - bytesPerPixel];
                int above = y == 0 ? 0 : previous[x];
                int corner = y == 0 || x < bytesPerPixel ? 0 : previous[x - bytesPerPixel];
                int predictor = filter switch { 0 => 0, 1 => left, 2 => above, 3 => (left + above) / 2, _ => Paeth(left, above, corner) };
                row[x] = unchecked((byte)(row[x] + predictor));
            }
            for (int x = 0; x < width; x++)
            {
                int target = (y * width + x) * 4;
                if (color is 2 or 6)
                {
                    int source = x * channels;
                    row.Slice(source, 3).CopyTo(rgba.AsSpan(target, 3));
                    rgba[target + 3] = color == 6 ? row[source + 3] : (byte)255;
                    if (color == 2 && transparency is not null && row[source] == BinaryPrimitives.ReadUInt16BigEndian(transparency) && row[source + 1] == BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(2)) && row[source + 2] == BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(4))) rgba[target + 3] = 0;
                }
                else if (color == 4)
                {
                    rgba[target] = rgba[target + 1] = rgba[target + 2] = row[x * 2];
                    rgba[target + 3] = row[x * 2 + 1];
                }
                else
                {
                    int value = (row[x * depth / 8] >> (8 - depth - (x * depth % 8))) & ((1 << depth) - 1);
                    if (color == 3)
                    {
                        if (palette is null || value >= palette.Length / 3) throw new ToolkitException("PNG_PALETTE_INDEX", "Pixel references a missing palette color.");
                        palette.AsSpan(value * 3, 3).CopyTo(rgba.AsSpan(target, 3));
                        rgba[target + 3] = transparency is not null && value < transparency.Length ? transparency[value] : (byte)255;
                    }
                    else
                    {
                        rgba[target] = rgba[target + 1] = rgba[target + 2] = (byte)(value * 255 / ((1 << depth) - 1));
                        rgba[target + 3] = transparency is not null && value == BinaryPrimitives.ReadUInt16BigEndian(transparency) ? (byte)0 : (byte)255;
                    }
                }
            }
        }
        return new RgbaImage(width, height, rgba);
    }

    /// <summary>Computes the PNG IEEE CRC-32 over a chunk type and payload.</summary>
    /// <param name="bytes">The chunk type followed by its payload.</param>
    /// <returns>The finalized checksum.</returns>
    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xEDB88320u);
        }
        return ~crc;
    }

    /// <summary>Selects the PNG Paeth predictor, preserving specified tie ordering.</summary>
    /// <param name="left">The decoded left byte.</param>
    /// <param name="above">The decoded byte on the previous row.</param>
    /// <param name="corner">The upper-left decoded byte.</param>
    /// <returns>The selected neighbor value.</returns>
    private static int Paeth(int left, int above, int corner)
    {
        int prediction = left + above - corner;
        int dl = Math.Abs(prediction - left), da = Math.Abs(prediction - above), dc = Math.Abs(prediction - corner);
        return dl <= da && dl <= dc ? left : da <= dc ? above : corner;
    }
}

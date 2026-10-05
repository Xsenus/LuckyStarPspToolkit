using System.Buffers.Binary;
using System.IO.Compression;
using LuckyStarPspToolkit.Formats.Common;
using LuckyStarPspToolkit.Formats.Fonts;

namespace LuckyStarPspToolkit.Formats.Images;

/// <summary>An authenticated-resource layout describing one PSP indexed image without changing its palette.</summary>
/// <param name="Id">The image identifier in its resource profile.</param>
/// <param name="PixelOffset">The original pixel or frame-header byte offset.</param>
/// <param name="Capacity">The allocated byte range ending before the next resource.</param>
/// <param name="PaletteOffset">The RGBA palette byte offset.</param>
/// <param name="ColorCount">The supported palette size, 16 or 256.</param>
/// <param name="Width">The original pixel width.</param>
/// <param name="Height">The original pixel height.</param>
/// <param name="Compressed">Whether a bounded offset table describes gzip pixel frames.</param>
public sealed record IndexedTextureLayout(int Id, int PixelOffset, int Capacity, int PaletteOffset, int ColorCount, int Width, int Height, bool Compressed);

/// <summary>Decodes and replaces PSP byte-swizzled, indexed RGBA textures while retaining palette and allocations.</summary>
public static class IndexedTexture
{
    /// <summary>Decodes a verified layout into original-size RGBA pixels.</summary>
    /// <param name="resource">The original binary snapshot.</param>
    /// <param name="layout">A layout bound to the caller's authenticated resource profile.</param>
    /// <returns>The decoded image.</returns>
    public static RgbaImage Decode(ReadOnlySpan<byte> resource, IndexedTextureLayout layout)
    {
        ValidateLayout(resource, layout);
        byte[] packed = ReadPixels(resource, layout);
        byte[] rgba = new byte[checked(layout.Width * layout.Height * 4)];
        for (int y = 0; y < layout.Height; y++)
            for (int x = 0; x < layout.Width; x++)
            {
                int index = GetIndex(packed, layout, x, y);
                resource.Slice(layout.PaletteOffset + index * 4, 4).CopyTo(rgba.AsSpan((y * layout.Width + x) * 4, 4));
            }
        return new RgbaImage(layout.Width, layout.Height, rgba);
    }

    /// <summary>Writes changed indices in a copy, refusing color or compressed-capacity loss unless quantization is requested.</summary>
    /// <param name="resource">The original binary snapshot, left unchanged.</param>
    /// <param name="layout">A layout validated against the resource bounds.</param>
    /// <param name="image">The edited original-size PNG pixels.</param>
    /// <param name="quantize">Whether new RGBA colors may map to the nearest existing palette entry.</param>
    /// <returns>A copy preserving all bytes outside the image allocation and the entire palette.</returns>
    public static byte[] Replace(ReadOnlySpan<byte> resource, IndexedTextureLayout layout, RgbaImage image, bool quantize = false)
    {
        ValidateLayout(resource, layout);
        if (image.Width != layout.Width || image.Height != layout.Height)
            throw new ToolkitException("TEXTURE_DIMENSIONS", "Edited PNG dimensions must match the original image.");
        byte[] packed = ReadPixels(resource, layout);
        bool changed = false;
        for (int y = 0; y < layout.Height; y++)
            for (int x = 0; x < layout.Width; x++)
            {
                int originalIndex = GetIndex(packed, layout, x, y);
                ReadOnlySpan<byte> color = image.Pixels.AsSpan((y * layout.Width + x) * 4, 4);
                ReadOnlySpan<byte> originalColor = resource.Slice(layout.PaletteOffset + originalIndex * 4, 4);
                // Keeping original indices makes an unchanged image byte-identical even with duplicate palette colors.
                if (color.SequenceEqual(originalColor) || (color[3] == 0 && originalColor[3] == 0)) continue;
                int bestIndex = -1, bestDistance = int.MaxValue;
                for (int index = 0; index < layout.ColorCount; index++)
                {
                    ReadOnlySpan<byte> candidate = resource.Slice(layout.PaletteOffset + index * 4, 4);
                    if (color.SequenceEqual(candidate) || (color[3] == 0 && candidate[3] == 0)) { bestIndex = index; bestDistance = 0; break; }
                    if (!quantize) continue;
                    // Compare premultiplied colors to avoid hidden RGB in transparent pixels dominating the match.
                    int distance = 4 * (color[3] - candidate[3]) * (color[3] - candidate[3]);
                    for (int component = 0; component < 3; component++)
                    {
                        int delta = (color[component] * color[3] - candidate[component] * candidate[3]) / 255;
                        distance += delta * delta;
                    }
                    if (distance < bestDistance) { bestDistance = distance; bestIndex = index; }
                }
                if (bestIndex < 0) throw new ToolkitException("TEXTURE_PALETTE", $"PNG color at ({x},{y}) is not in the original palette. Use original colors or explicitly enable --quantize.");
                SetIndex(packed, layout, x, y, bestIndex);
                changed = true;
            }
        byte[] output = resource.ToArray();
        if (!changed) return output;
        if (!layout.Compressed)
        {
            packed.CopyTo(output.AsSpan(layout.PixelOffset, packed.Length));
            return output;
        }
        ReadOnlySpan<byte> original = resource.Slice(layout.PixelOffset, layout.Capacity);
        int count = ReadInt(original, 0);
        int cursor = ReadInt(original, 4), pixelCursor = 0;
        for (int frame = 0; frame < count; frame++)
        {
            int originalOffset = ReadInt(original, 4 + frame * 4);
            int length = ReadInt(original, originalOffset);
            byte[] compressed;
            using (MemoryStream buffer = new())
            {
                using (GZipStream gzip = new(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
                    gzip.Write(packed.AsSpan(pixelCursor, length));
                compressed = buffer.ToArray();
            }
            int end = checked((cursor + 16 + compressed.Length + 15) / 16 * 16);
            if (end > layout.Capacity) throw new ToolkitException("TEXTURE_CAPACITY", "Edited compressed image does not fit its original allocation. Simplify the image; original resource is preserved.");
            Span<byte> target = output.AsSpan(layout.PixelOffset, layout.Capacity);
            BinaryPrimitives.WriteInt32LittleEndian(target.Slice(4 + frame * 4, 4), cursor);
            original.Slice(originalOffset, 16).CopyTo(target.Slice(cursor, 16));
            compressed.CopyTo(target.Slice(cursor + 16, compressed.Length));
            target.Slice(cursor + 16 + compressed.Length, end - cursor - 16 - compressed.Length).Clear();
            cursor = end; pixelCursor += length;
        }
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(layout.PixelOffset + 4 + count * 4, 4), cursor);
        return output;
    }

    /// <summary>Checks palette, allocation, dimensions and the standard 16-byte by 8-row PSP swizzle geometry.</summary>
    /// <param name="resource">The bounded binary snapshot.</param>
    /// <param name="layout">The requested indexed image layout.</param>
    private static void ValidateLayout(ReadOnlySpan<byte> resource, IndexedTextureLayout layout)
    {
        if (layout.ColorCount is not (16 or 256) || layout.Width <= 0 || layout.Height <= 0 || layout.Width > 4096 || layout.Height > 4096 || (long)layout.Width * layout.Height > 16 * 1024 * 1024 || layout.Height % 8 != 0 || layout.Width % (layout.ColorCount == 16 ? 32 : 16) != 0)
            throw new ToolkitException("TEXTURE_LAYOUT", "Unsupported texture dimensions, palette size or swizzle geometry.");
        int bytes = layout.Width * layout.Height / (layout.ColorCount == 16 ? 2 : 1);
        if (layout.PixelOffset < 0 || layout.PaletteOffset < 0 || layout.Capacity <= 0 || layout.Capacity > 64 * 1024 * 1024 || layout.PixelOffset > resource.Length - layout.Capacity || layout.PaletteOffset > resource.Length - layout.ColorCount * 4 || (!layout.Compressed && bytes > layout.Capacity))
            throw new ToolkitException("TEXTURE_RANGE", "Texture or palette exceeds the resource bounds.");
        if (layout.PixelOffset < layout.PaletteOffset + layout.ColorCount * 4 && layout.PaletteOffset < (long)layout.PixelOffset + layout.Capacity)
            throw new ToolkitException("TEXTURE_RANGE", "Texture allocation overlaps its palette.");
    }

    /// <summary>Reads raw indices or validates and expands every gzip frame to the exact expected image size.</summary>
    /// <param name="resource">The original resource snapshot.</param>
    /// <param name="layout">The validated resource layout.</param>
    /// <returns>The swizzled index bytes.</returns>
    private static byte[] ReadPixels(ReadOnlySpan<byte> resource, IndexedTextureLayout layout)
    {
        int expected = layout.Width * layout.Height / (layout.ColorCount == 16 ? 2 : 1);
        if (!layout.Compressed) return resource.Slice(layout.PixelOffset, expected).ToArray();
        ReadOnlySpan<byte> block = resource.Slice(layout.PixelOffset, layout.Capacity);
        int count = ReadInt(block, 0);
        if (count is < 1 or > 256 || (count + 2) * 4 > block.Length) throw new ToolkitException("TEXTURE_FRAMES", "Invalid texture frame count.");
        int first = ReadInt(block, 4);
        if (first < (count + 2) * 4) throw new ToolkitException("TEXTURE_FRAMES", "Frame data overlaps the offset table.");
        byte[] packed = new byte[expected];
        int pixelCursor = 0;
        for (int frame = 0; frame < count; frame++)
        {
            int start = ReadInt(block, 4 + frame * 4), end = ReadInt(block, 8 + frame * 4);
            if (start < first || end > block.Length || start > end - 16 || start % 16 != 0 || end % 16 != 0) throw new ToolkitException("TEXTURE_FRAMES", "Invalid texture frame boundaries.");
            int size = ReadInt(block, start);
            if (size <= 0 || size > expected - pixelCursor) throw new ToolkitException("TEXTURE_INFLATE_SIZE", "Texture frame expands beyond the image dimensions.");
            try
            {
                using MemoryStream input = new(block.Slice(start + 16, end - start - 16).ToArray(), writable: false);
                using GZipStream gzip = new(input, CompressionMode.Decompress);
                gzip.ReadExactly(packed.AsSpan(pixelCursor, size));
                if (gzip.ReadByte() != -1) throw new ToolkitException("TEXTURE_INFLATE_SIZE", "Texture frame expands beyond its declared size.");
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                throw new ToolkitException("TEXTURE_GZIP", "Texture frame contains invalid or truncated gzip data.", ex);
            }
            pixelCursor += size;
        }
        if (pixelCursor != expected) throw new ToolkitException("TEXTURE_INFLATE_SIZE", "Texture frame sizes do not match the image dimensions.");
        return packed;
    }

    /// <summary>Reads a nonnegative bounded little-endian frame value.</summary>
    /// <param name="data">The frame allocation.</param>
    /// <param name="offset">The field offset.</param>
    /// <returns>The validated integer.</returns>
    private static int ReadInt(ReadOnlySpan<byte> data, int offset)
    {
        if (offset < 0 || offset > data.Length - 4) throw new ToolkitException("TEXTURE_FRAMES", "Truncated texture frame field.");
        int value = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
        if (value < 0) throw new ToolkitException("TEXTURE_FRAMES", "Negative or oversized texture frame field.");
        return value;
    }

    /// <summary>Computes a swizzled byte address from a linear pixel coordinate.</summary>
    /// <param name="layout">The validated texture layout.</param>
    /// <param name="x">The pixel column.</param>
    /// <param name="y">The pixel row.</param>
    /// <returns>The packed byte offset.</returns>
    private static int Address(IndexedTextureLayout layout, int x, int y)
    {
        int stride = layout.Width / (layout.ColorCount == 16 ? 2 : 1);
        int byteX = x / (layout.ColorCount == 16 ? 2 : 1);
        return ((y / 8) * (stride / 16) + byteX / 16) * 128 + (y % 8) * 16 + byteX % 16;
    }

    /// <summary>Reads the original palette index, including the low-nibble-first four-bit format.</summary>
    /// <param name="bytes">The swizzled image bytes.</param>
    /// <param name="layout">The validated layout.</param>
    /// <param name="x">The pixel column.</param>
    /// <param name="y">The pixel row.</param>
    /// <returns>The palette index.</returns>
    private static int GetIndex(ReadOnlySpan<byte> bytes, IndexedTextureLayout layout, int x, int y)
        => layout.ColorCount == 16 ? (bytes[Address(layout, x, y)] >> ((x & 1) * 4)) & 15 : bytes[Address(layout, x, y)];

    /// <summary>Changes one packed index while retaining the neighboring nibble.</summary>
    /// <param name="bytes">The swizzled pixel copy.</param>
    /// <param name="layout">The validated layout.</param>
    /// <param name="x">The pixel column.</param>
    /// <param name="y">The pixel row.</param>
    /// <param name="index">The selected original-palette index.</param>
    private static void SetIndex(Span<byte> bytes, IndexedTextureLayout layout, int x, int y, int index)
    {
        int address = Address(layout, x, y);
        if (layout.ColorCount == 256) bytes[address] = (byte)index;
        else
        {
            int shift = (x & 1) * 4;
            bytes[address] = (byte)((bytes[address] & ~(15 << shift)) | (index << shift));
        }
    }
}
